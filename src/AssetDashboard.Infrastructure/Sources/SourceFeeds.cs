using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Dashboard;
using AssetDashboard.Infrastructure.Data;
using AssetDashboard.Infrastructure.Simulation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetDashboard.Infrastructure.Sources;

public sealed class SourceFeedOptions
{
    public bool Enabled { get; set; } = true;
    public int HistoryDays { get; set; } = 30;
    public int CheckIntervalSeconds { get; set; } = 30;
}

/// <summary>Fictional upstream systems and how their loads behave.</summary>
public static class SourceCatalog
{
    public sealed record Profile(string Name, SourceKind Kind, string Owner, string Description, int IntervalMinutes,
        int MinRows, int MaxRows, int MinSeconds, int MaxSeconds, double FailureRate, double WarningRate, string[] Failures, string[] Warnings);

    public static readonly Profile[] Sources =
    [
        new("Core leasing system", SourceKind.CoreLeasing, "Finance IT",
            "Contracts, balances, payments and arrears (change capture).", 15, 200, 4_000, 20, 180, 0.02, 0.04,
            ["Connection to source database timed out", "Change-capture log position lost; full resync required"],
            ["Rows with unknown product code skipped", "Late-arriving payments applied out of order"]),
        new("Equipment valuation feed", SourceKind.ValuationProvider, "Asset Management",
            "Index-based and inspection valuations from the valuation provider.", 1_440, 5_000, 30_000, 300, 1_500, 0.03, 0.08,
            ["Provider file missing at 06:00 cut-off", "File checksum mismatch"],
            ["Valuations for unknown serial numbers rejected", "Stale market index for one asset class"]),
        new("Auction results feed", SourceKind.AuctionHouse, "Remarketing",
            "Lot results, bids and hammer prices from auction partners.", 60, 0, 400, 5, 90, 0.04, 0.05,
            ["Partner API returned HTTP 503", "Authentication token expired"],
            ["Lots without matching repossession case"]),
        new("Credit bureau scores", SourceKind.CreditBureau, "Credit Risk",
            "Customer risk grades and bureau alerts.", 1_440, 8_000, 15_000, 120, 900, 0.02, 0.03,
            ["Bureau batch delayed by provider"],
            ["Customers not found at bureau"]),
        new("General ledger (ERP)", SourceKind.Erp, "Finance",
            "Write-offs, provisions and recovery postings.", 360, 1_000, 6_000, 60, 400, 0.02, 0.06,
            ["Period closed in ERP; postings locked"],
            ["Cost centre mapping missing for some postings"]),
    ];

    public static Profile For(SourceKind kind) => Sources.Single(s => s.Kind == kind);

    /// <summary>Simulates one load; deterministic for a given random generator.</summary>
    public static LoadRun SimulateRun(Profile p, int sourceId, DateTime startedAt, Random rng)
    {
        var roll = rng.NextDouble();
        var status = roll < p.FailureRate ? LoadStatus.Failed : roll < p.FailureRate + p.WarningRate ? LoadStatus.SucceededWithWarnings : LoadStatus.Succeeded;
        var rows = status == LoadStatus.Failed ? 0 : rng.Next(p.MinRows, p.MaxRows + 1);
        return new LoadRun
        {
            SourceSystemId = sourceId,
            StartedAt = startedAt,
            FinishedAt = startedAt.AddSeconds(rng.Next(p.MinSeconds, p.MaxSeconds + 1)),
            Status = status,
            RowsRead = rows,
            RowsRejected = status == LoadStatus.SucceededWithWarnings ? Math.Max(1, (int)(rows * rng.NextDouble() * 0.01)) : 0,
            Message = status switch
            {
                LoadStatus.Failed => p.Failures[rng.Next(p.Failures.Length)],
                LoadStatus.SucceededWithWarnings => p.Warnings[rng.Next(p.Warnings.Length)],
                _ => null,
            },
        };
    }
}

/// <summary>Seeds the source systems with load history and runs due loads on their schedule.</summary>
public sealed class SourceFeedSimulator(
    IServiceScopeFactory scopeFactory,
    IPortfolioEventSink sink,
    IOptions<SourceFeedOptions> options,
    ILogger<SourceFeedSimulator> logger) : BackgroundService
{
    private readonly Random _rng = new();

    /// <summary>Creates the source systems with <paramref name="days"/> of load history, if none exist.</summary>
    public static async Task EnsureSeededAsync(AssetDbContext db, int days, int seed, CancellationToken ct = default)
    {
        if (await db.SourceSystems.AnyAsync(ct)) return;
        var rng = new Random(seed);
        var now = DateTime.UtcNow;

        foreach (var p in SourceCatalog.Sources)
        {
            var source = new SourceSystem { Name = p.Name, Kind = p.Kind, Owner = p.Owner, Description = p.Description, ExpectedIntervalMinutes = p.IntervalMinutes };
            db.SourceSystems.Add(source);
            await db.SaveChangesAsync(ct);

            var runs = new List<LoadRun>();
            // Scheduled start times with a little jitter; daily feeds run early morning.
            for (var t = now.AddDays(-days); t < now; t = t.AddMinutes(p.IntervalMinutes))
            {
                var start = t.AddSeconds(rng.Next(0, Math.Min(600, p.IntervalMinutes * 6)));
                if (start < now) runs.Add(SourceCatalog.SimulateRun(p, source.Id, start, rng));
            }
            db.LoadRuns.AddRange(runs);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.CheckIntervalSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AssetDbContext>();
                foreach (var evt in await RunDueLoadsAsync(db, DateTime.UtcNow, _rng, stoppingToken))
                    await sink.PublishAsync(evt, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Source feed simulation failed");
            }
        }
    }

    /// <summary>
    /// Runs one load for every source whose interval has elapsed since its last run. Returns events for
    /// failures and for the first success after a failure (the feed's "noise" stays out of the live feed).
    /// </summary>
    public static async Task<IReadOnlyList<PortfolioEvent>> RunDueLoadsAsync(AssetDbContext db, DateTime now, Random rng, CancellationToken ct = default)
    {
        var sources = await db.SourceSystems
            .Select(s => new
            {
                Source = s,
                Last = s.Runs.OrderByDescending(r => r.StartedAt).Select(r => new { r.StartedAt, r.Status }).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var events = new List<PortfolioEvent>();
        foreach (var x in sources)
        {
            if (x.Last is not null && x.Last.StartedAt.AddMinutes(x.Source.ExpectedIntervalMinutes) > now) continue;

            var run = SourceCatalog.SimulateRun(SourceCatalog.For(x.Source.Kind), x.Source.Id, now, rng);
            db.LoadRuns.Add(run);
            if (run.Status == LoadStatus.Failed)
                events.Add(new PortfolioEvent(now, PortfolioEventType.DataLoadFailed, $"{x.Source.Name} load failed: {run.Message}"));
            else if (x.Last?.Status == LoadStatus.Failed)
                events.Add(new PortfolioEvent(now, PortfolioEventType.DataLoadRecovered, $"{x.Source.Name} recovered: {run.RowsRead:N0} rows loaded"));
        }
        await db.SaveChangesAsync(ct);
        return events;
    }
}
