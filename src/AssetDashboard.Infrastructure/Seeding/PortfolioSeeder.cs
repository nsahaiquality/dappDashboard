using System.Diagnostics;
using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AssetDashboard.Infrastructure.Seeding;

public sealed class SeedOptions
{
    public bool Enabled { get; set; } = true;
    public int Contracts { get; set; } = 20_000;
    public int RandomSeed { get; set; } = 42;
    public int BatchSize { get; set; } = 2_000;
}

/// <summary>Fills an empty database with a synthetic portfolio.</summary>
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
        var portfolio = new PortfolioGenerator(options.RandomSeed).Generate(options.Contracts, DateTime.UtcNow);
        db.ChangeTracker.AutoDetectChangesEnabled = false;

        db.Vendors.AddRange(portfolio.Vendors);
        db.Customers.AddRange(portfolio.Customers);
        db.Auctions.AddRange(portfolio.Auctions);
        await db.SaveChangesAsync(ct);

        foreach (var batch in portfolio.Contracts.Chunk(options.BatchSize))
        {
            foreach (var contract in batch)
            {
                // Vendors/customers are already saved; reference them by key only.
                contract.VendorId = contract.Vendor.Id;
                contract.CustomerId = contract.Customer.Id;
                contract.Vendor = null!;
                contract.Customer = null!;
                foreach (var @case in contract.Assets.Select(a => a.RemarketingCase).OfType<RemarketingCase>().Where(c => c.Auction is not null))
                {
                    @case.AuctionId = @case.Auction!.Id;
                    @case.Auction = null;
                }
            }
            db.Contracts.AddRange(batch);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        logger.LogInformation("Seeded {Contracts} contracts with {Assets} assets in {Elapsed}",
            portfolio.Contracts.Count, portfolio.Contracts.Sum(c => c.Assets.Count), sw.Elapsed);
    }
}
