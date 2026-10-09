using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetDashboard.Infrastructure.History;

/// <summary>Portfolio figures on one day; IsBackfilled marks generated history.</summary>
public sealed record HistoryPoint(
    DateOnly Date,
    int OpenContracts,
    int ActiveAssets,
    decimal Exposure,
    decimal MarketValue,
    decimal ForcedSaleValue,
    decimal LoanToValue,
    decimal ExposureAtRisk,
    decimal DefaultedExposure,
    decimal NetRecoveries,
    bool IsBackfilled);

/// <summary>Daily portfolio history: backfill, live capture of today's figures, and reads for trend charts.</summary>
public sealed class PortfolioHistoryService(AssetDbContext db)
{
    /// <summary>Annual growth of exposure per class, used to walk the backfill back in time.</summary>
    private static readonly Dictionary<AssetClass, double> AnnualGrowth = new()
    {
        [AssetClass.Agriculture] = 0.06,
        [AssetClass.Construction] = 0.09,
        [AssetClass.Healthcare] = 0.10,
        [AssetClass.Technology] = 0.12,
        [AssetClass.Transportation] = 0.07,
        [AssetClass.MaterialHandling] = 0.08,
        [AssetClass.CleanTech] = 0.18,
    };

    public async Task<IReadOnlyList<HistoryPoint>> GetAsync(int days, AssetClass? assetClass, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 3_650);
        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-days);
        return await db.PortfolioHistory.AsNoTracking()
            .Where(p => p.Date > from && p.AssetClass == assetClass)
            .OrderBy(p => p.Date)
            .Select(p => new HistoryPoint(p.Date, p.OpenContracts, p.ActiveAssets, p.Exposure, p.MarketValue, p.ForcedSaleValue,
                p.MarketValue == 0 ? 0 : Math.Round(p.Exposure / p.MarketValue, 4),
                p.ExposureAtRisk, p.DefaultedExposure, p.NetRecoveries, p.IsBackfilled))
            .ToListAsync(ct);
    }

    /// <summary>Recomputes today's rows (total and per class) from the live portfolio.</summary>
    public async Task CaptureTodayAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = await CurrentFiguresAsync(today, ct);
        rows.ForEach(r => r.IsBackfilled = false);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.PortfolioHistory.Where(p => p.Date == today).ExecuteDeleteAsync(ct);
        db.PortfolioHistory.AddRange(rows);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Generates <paramref name="days"/> of plausible history ending yesterday, anchored on today's live
    /// figures: exposure grows into today, LTV and arrears follow mean-reverting random walks with a
    /// seasonal arrears cycle. Does nothing if any history exists.
    /// </summary>
    public async Task<int> EnsureBackfilledAsync(int days, int seed, CancellationToken ct = default)
    {
        if (await db.PortfolioHistory.AnyAsync(ct)) return 0;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var current = await CurrentFiguresAsync(today, ct);
        var averageRecoveries = await AverageDailyRecoveriesAsync(ct);
        var generated = new List<PortfolioHistoryPoint>();

        foreach (var (anchor, index) in current.Where(c => c.AssetClass is not null).Select((c, i) => (c, i)))
        {
            var cls = anchor.AssetClass!.Value;
            var rng = new Random(seed * 31 + index);
            var growth = AnnualGrowth.GetValueOrDefault(cls, 0.08);
            var ltvToday = anchor.MarketValue == 0 ? 0.75 : (double)(anchor.Exposure / anchor.MarketValue);
            var fsvRatio = anchor.MarketValue == 0 ? 0.6 : (double)(anchor.ForcedSaleValue / anchor.MarketValue);
            var atRiskShare = anchor.Exposure == 0 ? 0.08 : (double)(anchor.ExposureAtRisk / anchor.Exposure);
            var defaultedShare = anchor.Exposure == 0 ? 0.03 : (double)(anchor.DefaultedExposure / anchor.Exposure);
            double wExposure = 0, wLtv = 0, wRisk = 0;

            for (var d = 1; d <= days; d++)
            {
                var date = today.AddDays(-d);
                var years = d / 365.0;
                // Mean-reverting random walks, anchored at today (d = 0).
                wExposure = 0.98 * wExposure + Normal(rng, 0, 0.002);
                wLtv = 0.97 * wLtv + Normal(rng, 0, 0.004);
                wRisk = 0.95 * wRisk + Normal(rng, 0, 0.03);
                var season = 1 + 0.15 * Math.Sin(2 * Math.PI * (date.DayOfYear - 30) / 365.0);

                var exposure = (double)anchor.Exposure * Math.Exp(-growth * years) * (1 + wExposure);
                var ltv = Math.Max(0.3, ltvToday - 0.02 * years + wLtv);
                var market = exposure / ltv;
                var scale = anchor.Exposure == 0 ? 1 : exposure / (double)anchor.Exposure;
                var recoveries = (double)averageRecoveries.GetValueOrDefault(cls) * Math.Max(0, Normal(rng, 1, 0.8));

                generated.Add(new PortfolioHistoryPoint
                {
                    Date = date,
                    AssetClass = cls,
                    OpenContracts = (int)Math.Round(anchor.OpenContracts * scale),
                    ActiveAssets = (int)Math.Round(anchor.ActiveAssets * scale),
                    Exposure = Money(exposure),
                    MarketValue = Money(market),
                    ForcedSaleValue = Money(market * fsvRatio),
                    ExposureAtRisk = Money(exposure * atRiskShare * season * Math.Max(0.3, 1 + wRisk)),
                    DefaultedExposure = Money(exposure * defaultedShare * Math.Max(0.3, 1 + wRisk / 2)),
                    NetRecoveries = Money(recoveries),
                    IsBackfilled = true,
                });
            }
        }

        // The portfolio total is the sum of the classes, so totals and breakdowns always agree.
        generated.AddRange(generated.GroupBy(p => p.Date).Select(g => Total(g.Key, g, backfilled: true)).ToList());

        db.ChangeTracker.AutoDetectChangesEnabled = false;
        foreach (var batch in generated.Chunk(5_000))
        {
            db.PortfolioHistory.AddRange(batch);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        db.ChangeTracker.AutoDetectChangesEnabled = true;

        await CaptureTodayAsync(ct);
        return generated.Count;
    }

    /// <summary>Today's figures per asset class plus the portfolio total.</summary>
    private async Task<List<PortfolioHistoryPoint>> CurrentFiguresAsync(DateOnly today, CancellationToken ct)
    {
        var contracts = await db.Contracts.AsNoTracking()
            .Where(c => c.Status != ContractStatus.Closed)
            .GroupBy(c => c.AssetClass)
            .Select(g => new
            {
                AssetClass = g.Key,
                Count = g.Count(),
                Exposure = g.Sum(c => c.OutstandingPrincipal),
                AtRisk = g.Where(c => c.DaysPastDue >= 30).Sum(c => c.OutstandingPrincipal),
                Defaulted = g.Where(c => c.DaysPastDue >= 90).Sum(c => c.OutstandingPrincipal),
            })
            .ToListAsync(ct);
        var assets = await db.Assets.AsNoTracking()
            .Where(a => a.Status != AssetStatus.Sold)
            .GroupBy(a => a.AssetClass)
            .Select(g => new { AssetClass = g.Key, Count = g.Count(), Market = g.Sum(a => a.MarketValue), Forced = g.Sum(a => a.ForcedSaleValue) })
            .ToDictionaryAsync(x => x.AssetClass, ct);
        var recoveries = await db.RemarketingCases.AsNoTracking()
            .Where(c => c.Status == RemarketingStatus.Sold && c.SoldOn == today)
            .GroupBy(c => c.Asset.AssetClass)
            .Select(g => new { AssetClass = g.Key, Net = g.Sum(c => (c.SalePrice ?? 0) - c.RecoveryCosts) })
            .ToDictionaryAsync(x => x.AssetClass, x => x.Net, ct);

        var rows = Enum.GetValues<AssetClass>().Select(cls =>
        {
            var c = contracts.FirstOrDefault(x => x.AssetClass == cls);
            var a = assets.GetValueOrDefault(cls);
            return new PortfolioHistoryPoint
            {
                Date = today,
                AssetClass = cls,
                OpenContracts = c?.Count ?? 0,
                ActiveAssets = a?.Count ?? 0,
                Exposure = c?.Exposure ?? 0,
                MarketValue = a?.Market ?? 0,
                ForcedSaleValue = a?.Forced ?? 0,
                ExposureAtRisk = c?.AtRisk ?? 0,
                DefaultedExposure = c?.Defaulted ?? 0,
                NetRecoveries = recoveries.GetValueOrDefault(cls),
            };
        }).ToList();
        rows.Add(Total(today, rows, backfilled: false));
        return rows;
    }

    /// <summary>Average net recoveries per day per class over the last year of sales.</summary>
    private async Task<Dictionary<AssetClass, decimal>> AverageDailyRecoveriesAsync(CancellationToken ct)
    {
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-365);
        var sums = await db.RemarketingCases.AsNoTracking()
            .Where(c => c.Status == RemarketingStatus.Sold && c.SoldOn >= since)
            .GroupBy(c => c.Asset.AssetClass)
            .Select(g => new { AssetClass = g.Key, Net = g.Sum(c => (c.SalePrice ?? 0) - c.RecoveryCosts) })
            .ToListAsync(ct);
        return sums.ToDictionary(x => x.AssetClass, x => Math.Round(x.Net / 365, 2));
    }

    private static PortfolioHistoryPoint Total(DateOnly date, IEnumerable<PortfolioHistoryPoint> classes, bool backfilled)
    {
        var list = classes.Where(c => c.AssetClass is not null).ToList();
        return new PortfolioHistoryPoint
        {
            Date = date,
            AssetClass = null,
            OpenContracts = list.Sum(x => x.OpenContracts),
            ActiveAssets = list.Sum(x => x.ActiveAssets),
            Exposure = list.Sum(x => x.Exposure),
            MarketValue = list.Sum(x => x.MarketValue),
            ForcedSaleValue = list.Sum(x => x.ForcedSaleValue),
            ExposureAtRisk = list.Sum(x => x.ExposureAtRisk),
            DefaultedExposure = list.Sum(x => x.DefaultedExposure),
            NetRecoveries = list.Sum(x => x.NetRecoveries),
            IsBackfilled = backfilled,
        };
    }

    private static decimal Money(double value) => Math.Round((decimal)Math.Max(0, value), 2);

    private static double Normal(Random rng, double mean, double stdDev)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return mean + stdDev * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
