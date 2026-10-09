using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Dashboard;
using AssetDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetDashboard.Infrastructure.Vendors;

public enum VendorSort
{
    Exposure,
    DefaultRate,
    Origination,
    TargetAttainment,
}

/// <summary>A vendor programme: terms, exposure, risk and origination against target.</summary>
public sealed record VendorPerformance(
    int Id, string Name, string Country, AssetClass AssetClass, VendorProgramType ProgramType, RecourseType Recourse, string Rating,
    DateOnly OnboardedOn, int Contracts, int OpenContracts, decimal Exposure, decimal MarketValue, decimal LoanToValue,
    // Contracts that ever defaulted (still defaulted, or closed through remarketing) ÷ all contracts.
    decimal DefaultRate,
    decimal? RecoveryRate,
    decimal Origination12Months,
    decimal AnnualVolumeTarget,
    decimal TargetAttainment);

/// <summary>New contracts and financed amount in one month.</summary>
public sealed record MonthlyOrigination(int Year, int Month, int Contracts, decimal Financed);

/// <summary>Contracts and exposure in one category (asset class, product or customer).</summary>
public sealed record VendorMix(string Key, int Contracts, decimal Exposure);

/// <summary>One vendor with origination history, mixes, delinquency and top customers.</summary>
public sealed record VendorDetail(
    VendorPerformance Performance,
    IReadOnlyList<MonthlyOrigination> Origination,
    IReadOnlyList<VendorMix> ByAssetClass,
    IReadOnlyList<VendorMix> ByProduct,
    IReadOnlyList<DelinquencyBucket> Delinquency,
    IReadOnlyList<VendorMix> TopCustomers);

/// <summary>Vendor (partner programme) performance. Aggregates run in SQL per vendor and are joined in memory.</summary>
public sealed class VendorService(AssetDbContext db)
{
    public async Task<PagedResult<VendorPerformance>> GetVendorsAsync(PortfolioFilter? filter, VendorSort sort, int page, int pageSize,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(1, page);
        var all = await PerformanceAsync((filter ?? PortfolioFilter.None).Normalize(), null, ct);
        IOrderedEnumerable<VendorPerformance> sorted = sort switch
        {
            VendorSort.DefaultRate => all.OrderByDescending(v => v.DefaultRate),
            VendorSort.Origination => all.OrderByDescending(v => v.Origination12Months),
            VendorSort.TargetAttainment => all.OrderByDescending(v => v.TargetAttainment),
            _ => all.OrderByDescending(v => v.Exposure),
        };
        var items = sorted.ThenBy(v => v.Id).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResult<VendorPerformance>(items, all.Count, page, pageSize);
    }

    public async Task<VendorDetail?> GetVendorAsync(int id, CancellationToken ct = default)
    {
        var performance = (await PerformanceAsync(PortfolioFilter.None, id, ct)).SingleOrDefault();
        if (performance is null) return null;

        var contracts = db.Contracts.AsNoTracking().Where(c => c.VendorId == id);
        var open = contracts.Where(c => c.Status != ContractStatus.Closed);
        var since = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(-23);

        var monthly = await contracts.Where(c => c.StartDate >= since)
            .GroupBy(c => new { c.StartDate.Year, c.StartDate.Month })
            .Select(g => new MonthlyOrigination(g.Key.Year, g.Key.Month, g.Count(), g.Sum(c => c.FinancedAmount)))
            .ToListAsync(ct);
        // Every month in the window, including months without new business.
        var origination = Enumerable.Range(0, 24).Select(i => since.AddMonths(i))
            .Select(m => monthly.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month) ?? new MonthlyOrigination(m.Year, m.Month, 0, 0))
            .ToList();

        var byClass = await open.GroupBy(c => c.AssetClass)
            .Select(g => new { g.Key, Count = g.Count(), Exposure = g.Sum(c => c.OutstandingPrincipal) }).ToListAsync(ct);
        var byProduct = await open.GroupBy(c => c.ProductType)
            .Select(g => new { g.Key, Count = g.Count(), Exposure = g.Sum(c => c.OutstandingPrincipal) }).ToListAsync(ct);
        var buckets = await open
            .GroupBy(c => c.DaysPastDue == 0 ? 0 : c.DaysPastDue < 30 ? 1 : c.DaysPastDue < 60 ? 2 : c.DaysPastDue < 90 ? 3 : 4)
            .Select(g => new { Bucket = g.Key, Count = g.Count(), Exposure = g.Sum(c => c.OutstandingPrincipal) }).ToListAsync(ct);
        var topCustomers = await open.GroupBy(c => c.Customer.Name)
            .Select(g => new { g.Key, Count = g.Count(), Exposure = g.Sum(c => c.OutstandingPrincipal) })
            .OrderByDescending(x => x.Exposure).Take(5).ToListAsync(ct);

        string[] bucketNames = ["Current", "1-29 dpd", "30-59 dpd", "60-89 dpd", "90+ dpd"];
        return new VendorDetail(
            performance,
            origination,
            byClass.OrderByDescending(x => x.Exposure).Select(x => new VendorMix(x.Key.ToString(), x.Count, x.Exposure)).ToList(),
            byProduct.OrderByDescending(x => x.Exposure).Select(x => new VendorMix(x.Key.ToString(), x.Count, x.Exposure)).ToList(),
            bucketNames.Select((n, i) => buckets.FirstOrDefault(b => b.Bucket == i) is { } b
                ? new DelinquencyBucket(n, b.Count, b.Exposure) : new DelinquencyBucket(n, 0, 0)).ToList(),
            topCustomers.Select(x => new VendorMix(x.Key, x.Count, x.Exposure)).ToList());
    }

    private async Task<List<VendorPerformance>> PerformanceAsync(PortfolioFilter filter, int? vendorId, CancellationToken ct)
    {
        var contracts = filter.Apply(db.Contracts.AsNoTracking());
        if (vendorId is { } id) contracts = contracts.Where(c => c.VendorId == id);
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1);

        var totals = await contracts.GroupBy(c => c.VendorId)
            .Select(g => new
            {
                VendorId = g.Key,
                Contracts = g.Count(),
                Open = g.Count(c => c.Status != ContractStatus.Closed),
                Exposure = g.Where(c => c.Status != ContractStatus.Closed).Sum(c => c.OutstandingPrincipal),
                Origination = g.Where(c => c.StartDate >= since).Sum(c => c.FinancedAmount),
            })
            .ToDictionaryAsync(x => x.VendorId, ct);
        var defaulted = await contracts
            .Where(c => c.Status == ContractStatus.Defaulted || c.Assets.Any(a => a.RemarketingCase != null))
            .GroupBy(c => c.VendorId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var market = await contracts.Where(c => c.Status != ContractStatus.Closed)
            .SelectMany(c => c.Assets.Where(a => a.Status != AssetStatus.Sold))
            .GroupBy(a => a.Contract.VendorId).Select(g => new { g.Key, Market = g.Sum(a => a.MarketValue) })
            .ToDictionaryAsync(x => x.Key, x => x.Market, ct);
        var recovery = await contracts
            .SelectMany(c => c.Assets.Where(a => a.RemarketingCase != null && a.RemarketingCase.Status == RemarketingStatus.Sold))
            .GroupBy(a => a.Contract.VendorId)
            .Select(g => new
            {
                g.Key,
                Net = g.Sum(a => (a.RemarketingCase!.SalePrice ?? 0) - a.RemarketingCase.RecoveryCosts),
                Ead = g.Sum(a => a.RemarketingCase!.ExposureAtDefault),
            })
            .ToDictionaryAsync(x => x.Key, ct);

        var vendors = await db.Vendors.AsNoTracking()
            .Where(v => totals.Keys.Contains(v.Id))
            .ToListAsync(ct);

        return vendors.Select(v =>
        {
            var t = totals[v.Id];
            var mv = market.GetValueOrDefault(v.Id);
            var r = recovery.GetValueOrDefault(v.Id);
            return new VendorPerformance(v.Id, v.Name, v.Country, v.PrimaryAssetClass, v.ProgramType, v.Recourse, v.Rating, v.OnboardedOn,
                t.Contracts, t.Open, t.Exposure, mv, mv == 0 ? 0 : Math.Round(t.Exposure / mv, 4),
                t.Contracts == 0 ? 0 : Math.Round((decimal)defaulted.GetValueOrDefault(v.Id) / t.Contracts, 4),
                r is null || r.Ead == 0 ? null : Math.Round(r.Net / r.Ead, 4),
                t.Origination, v.AnnualVolumeTarget,
                v.AnnualVolumeTarget == 0 ? 0 : Math.Round(t.Origination / v.AnnualVolumeTarget, 4));
        }).ToList();
    }
}
