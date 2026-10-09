using System.Diagnostics;
using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AssetDashboard.Infrastructure.Seeding;

public sealed class SeedOptions
{
    public bool Enabled { get; set; } = true;
    public int Contracts { get; set; } = 20_000;
    public int RandomSeed { get; set; } = 42;
    /// <summary>Contracts generated and written per COPY round.</summary>
    public int BatchSize { get; set; } = 10_000;
}

/// <summary>
/// Fills an empty database with a synthetic portfolio. Contracts are generated in chunks and written
/// with binary COPY, so a million contracts take minutes and memory stays flat.
/// </summary>
public sealed class PortfolioSeeder(AssetDbContext db, ILogger<PortfolioSeeder> logger)
{
    public async Task SeedAsync(SeedOptions options, CancellationToken ct = default)
    {
        if (await db.Contracts.AnyAsync(ct))
        {
            logger.LogInformation("Database already contains data; skipping seed");
            return;
        }

        var sw = Stopwatch.StartNew();
        var generator = new PortfolioGenerator(options.RandomSeed);
        var (vendors, customers) = generator.CreateParties(options.Contracts, DateTime.UtcNow);
        var ids = new IdSequence();
        vendors.ForEach(v => v.Id = ids.NextInt<Vendor>());
        customers.ForEach(c => c.Id = ids.NextInt<Customer>());

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            // Vendors are written now and updated with their volume targets at the end.
            await BulkCopy.WriteAsync(db, connection, vendors, ct);
            await BulkCopy.WriteAsync(db, connection, customers, ct);

            var writtenAuctions = new HashSet<Auction>();
            long assets = 0, valuations = 0;
            foreach (var chunk in generator.CreateContracts(options.Contracts).Chunk(Math.Max(1_000, options.BatchSize)))
            {
                // Auctions referenced by this chunk must exist before its lots.
                var newAuctions = generator.Auctions.Where(writtenAuctions.Add).ToList();
                newAuctions.ForEach(a => a.Id = ids.NextInt<Auction>());
                await BulkCopy.WriteAsync(db, connection, newAuctions, ct);

                var rows = Flatten(chunk, ids);
                await BulkCopy.WriteAsync(db, connection, rows.Contracts, ct);
                await BulkCopy.WriteAsync(db, connection, rows.Assets, ct);
                await BulkCopy.WriteAsync(db, connection, rows.Valuations, ct);
                await BulkCopy.WriteAsync(db, connection, rows.Cases, ct);
                await BulkCopy.WriteAsync(db, connection, rows.Requests, ct);
                assets += rows.Assets.Count;
                valuations += rows.Valuations.Count;
                logger.LogInformation("Seeded {Done:N0}/{Total:N0} contracts ({Elapsed:mm\\:ss})",
                    ids.Last<Contract>(), options.Contracts, sw.Elapsed);
            }

            // Auctions can still change status/counters after their first lots were written; store their final state.
            generator.ApplyVolumeTargets();
            await UpdateFinalStateAsync(vendors, writtenAuctions, ct);

            foreach (var reset in new Func<CancellationToken, Task>[]
                     {
                         c => BulkCopy.ResetIdentityAsync<Vendor>(db, c), c => BulkCopy.ResetIdentityAsync<Customer>(db, c),
                         c => BulkCopy.ResetIdentityAsync<Auction>(db, c), c => BulkCopy.ResetIdentityAsync<Contract>(db, c),
                         c => BulkCopy.ResetIdentityAsync<Asset>(db, c), c => BulkCopy.ResetIdentityAsync<AssetValuation>(db, c),
                         c => BulkCopy.ResetIdentityAsync<RemarketingCase>(db, c), c => BulkCopy.ResetIdentityAsync<RefinancingRequest>(db, c),
                     })
                await reset(ct);
            await db.Database.ExecuteSqlRawAsync("ANALYZE", ct);

            logger.LogInformation("Seeded {Contracts:N0} contracts, {Assets:N0} assets and {Valuations:N0} valuations in {Elapsed}",
                options.Contracts, assets, valuations, sw.Elapsed);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task UpdateFinalStateAsync(List<Vendor> vendors, IEnumerable<Auction> auctions, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        foreach (var v in vendors)
        {
            db.Vendors.Attach(v);
            db.Entry(v).Property(x => x.AnnualVolumeTarget).IsModified = true;
        }
        foreach (var a in auctions)
        {
            db.Auctions.Attach(a);
            db.Entry(a).Property(x => x.LotsPassedIn).IsModified = true;
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private sealed record Rows(List<Contract> Contracts, List<Asset> Assets, List<AssetValuation> Valuations,
        List<RemarketingCase> Cases, List<RefinancingRequest> Requests);

    /// <summary>Assigns keys and foreign keys for one chunk and splits the object graph into per-table lists.</summary>
    private static Rows Flatten(Contract[] chunk, IdSequence ids)
    {
        var rows = new Rows([], [], [], [], []);
        foreach (var contract in chunk)
        {
            contract.Id = ids.Next<Contract>();
            contract.VendorId = contract.Vendor.Id;
            contract.CustomerId = contract.Customer.Id;
            rows.Contracts.Add(contract);
            foreach (var asset in contract.Assets)
            {
                asset.Id = ids.Next<Asset>();
                asset.ContractId = contract.Id;
                rows.Assets.Add(asset);
                foreach (var v in asset.Valuations)
                {
                    v.Id = ids.Next<AssetValuation>();
                    v.AssetId = asset.Id;
                    rows.Valuations.Add(v);
                }
                if (asset.RemarketingCase is { } c)
                {
                    c.Id = ids.Next<RemarketingCase>();
                    c.AssetId = asset.Id;
                    c.AuctionId = c.Auction?.Id;
                    rows.Cases.Add(c);
                }
            }
            foreach (var r in contract.RefinancingRequests)
            {
                r.Id = ids.Next<RefinancingRequest>();
                r.ContractId = contract.Id;
                rows.Requests.Add(r);
            }
        }
        return rows;
    }

    /// <summary>Client-side key generation per table (sequences are reset after the COPY).</summary>
    private sealed class IdSequence
    {
        private readonly Dictionary<Type, long> _last = [];

        public long Next<T>()
        {
            var next = _last.GetValueOrDefault(typeof(T)) + 1;
            _last[typeof(T)] = next;
            return next;
        }

        /// <summary>For tables with int keys.</summary>
        public int NextInt<T>() => checked((int)Next<T>());

        public long Last<T>() => _last.GetValueOrDefault(typeof(T));
    }
}
