using System.Linq.Expressions;
using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetDashboard.Infrastructure.Dashboard;

public enum AssetSort
{
    MarketValue,
    Ltv,
    DaysPastDue,
}

/// <summary>Computes portfolio statistics with server-side (SQL) aggregation.</summary>
public sealed class PortfolioStatsService(AssetDbContext db)
{
    public async Task<PortfolioSnapshot> GetSnapshotAsync(PortfolioFilter? filter = null, CancellationToken ct = default)
    {
        filter = (filter ?? PortfolioFilter.None).Normalize();
        var openContracts = filter.Apply(db.Contracts.AsNoTracking().Where(c => c.Status != ContractStatus.Closed));
        var activeAssets = filter.Apply(db.Assets.AsNoTracking().Where(a => a.Status != AssetStatus.Sold));

        var contractTotals = await openContracts
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Exposure = g.Sum(c => c.OutstandingPrincipal),
                AtRisk = g.Where(c => c.DaysPastDue >= 30).Sum(c => c.OutstandingPrincipal),
            })
            .SingleOrDefaultAsync(ct);

        var shortfall = await openContracts
            .Select(c => c.OutstandingPrincipal - c.Assets.Where(a => a.Status != AssetStatus.Sold).Sum(a => a.ForcedSaleValue))
            .Where(gap => gap > 0)
            .SumAsync(ct);

        var assetsByClass = await activeAssets
            .GroupBy(a => a.AssetClass)
            .Select(g => new { AssetClass = g.Key, Count = g.Count(), Market = g.Sum(a => a.MarketValue), Forced = g.Sum(a => a.ForcedSaleValue) })
            .ToListAsync(ct);

        var exposureByClass = await openContracts
            .GroupBy(c => c.AssetClass)
            .Select(g => new { AssetClass = g.Key, Exposure = g.Sum(c => c.OutstandingPrincipal) })
            .ToDictionaryAsync(x => x.AssetClass, x => x.Exposure, ct);

        var byClass = assetsByClass
            .Select(a =>
            {
                var exposure = exposureByClass.GetValueOrDefault(a.AssetClass);
                return new AssetClassStat(a.AssetClass, a.Count, a.Market, exposure, Ratio(exposure, a.Market));
            })
            .OrderByDescending(x => x.Exposure)
            .ToList();

        var delinquency = await openContracts
            .GroupBy(c => c.DaysPastDue == 0 ? 0
                : c.DaysPastDue < 30 ? 1
                : c.DaysPastDue < 60 ? 2
                : c.DaysPastDue < 90 ? 3 : 4)
            .Select(g => new { Bucket = g.Key, Count = g.Count(), Exposure = g.Sum(c => c.OutstandingPrincipal) })
            .ToListAsync(ct);
        string[] bucketNames = ["Current", "1-29 dpd", "30-59 dpd", "60-89 dpd", "90+ dpd"];
        var buckets = bucketNames
            .Select((name, i) => delinquency.FirstOrDefault(d => d.Bucket == i) is { } d
                ? new DelinquencyBucket(name, d.Count, d.Exposure)
                : new DelinquencyBucket(name, 0, 0))
            .ToList();

        var topVendors = await openContracts
            .GroupBy(c => new { c.VendorId, c.Vendor.Name })
            .Select(g => new { g.Key.VendorId, g.Key.Name, Count = g.Count(), Exposure = g.Sum(c => c.OutstandingPrincipal) })
            .OrderByDescending(v => v.Exposure)
            .Take(10)
            .Select(v => new VendorStat(v.VendorId, v.Name, v.Count, v.Exposure))
            .ToListAsync(ct);

        var byCountry = await activeAssets
            .GroupBy(a => a.Country)
            .Select(g => new { Country = g.Key, Count = g.Count(), Market = g.Sum(a => a.MarketValue) })
            .OrderByDescending(c => c.Market)
            .Select(c => new CountryStat(c.Country, c.Count, c.Market))
            .ToListAsync(ct);

        var marketValue = assetsByClass.Sum(a => a.Market);
        var exposure = contractTotals?.Exposure ?? 0;

        return new PortfolioSnapshot(
            GeneratedAt: DateTime.UtcNow,
            OpenContracts: contractTotals?.Count ?? 0,
            ActiveAssets: assetsByClass.Sum(a => a.Count),
            TotalExposure: exposure,
            TotalMarketValue: marketValue,
            TotalForcedSaleValue: assetsByClass.Sum(a => a.Forced),
            LoanToValue: Ratio(exposure, marketValue),
            ExposureAtRisk: contractTotals?.AtRisk ?? 0,
            ForcedSaleShortfall: shortfall,
            ByAssetClass: byClass,
            Delinquency: buckets,
            Remarketing: await GetRemarketingAsync(filter, ct),
            TopVendors: topVendors,
            ByCountry: byCountry,
            Filter: filter);
    }

    private async Task<RemarketingStat> GetRemarketingAsync(PortfolioFilter filter, CancellationToken ct)
    {
        var cases = filter.Apply(db.RemarketingCases.AsNoTracking());
        var counts = await cases
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

        var sold = await cases
            .Where(c => c.Status == RemarketingStatus.Sold)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Ead = g.Sum(c => c.ExposureAtDefault),
                Proceeds = g.Sum(c => c.SalePrice ?? 0),
                Costs = g.Sum(c => c.RecoveryCosts),
            })
            .SingleOrDefaultAsync(ct);

        var averageDaysToSell = await cases
            .Where(c => c.Status == RemarketingStatus.Sold && c.SoldOn != null)
            .Select(c => (double?)(c.SoldOn!.Value.DayNumber - c.RepossessedOn.DayNumber))
            .AverageAsync(ct) ?? 0;

        var ead = sold?.Ead ?? 0;
        var proceeds = sold?.Proceeds ?? 0;
        var costs = sold?.Costs ?? 0;
        return new RemarketingStat(
            counts.GetValueOrDefault(RemarketingStatus.Repossessed),
            counts.GetValueOrDefault(RemarketingStatus.Listed),
            counts.GetValueOrDefault(RemarketingStatus.Sold),
            ead, proceeds, costs,
            Ratio(proceeds - costs, ead),
            Math.Round(averageDaysToSell, 1));
    }

    public async Task<ReferenceData> GetReferenceDataAsync(CancellationToken ct = default)
    {
        var vendors = await db.Vendors.AsNoTracking()
            .OrderBy(v => v.Name)
            .Select(v => new VendorOption(v.Id, v.Name, v.PrimaryAssetClass))
            .ToListAsync(ct);
        var countries = await db.Customers.AsNoTracking()
            .Select(c => c.Country).Distinct().OrderBy(c => c)
            .ToListAsync(ct);
        return new ReferenceData(vendors, countries);
    }

    public async Task<PagedResult<AssetListItem>> GetAssetsAsync(PortfolioFilter filter, AssetStatus? status, string? search,
        AssetSort sort, int page, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(1, page);

        var query = FilteredAssets(filter, status, search);
        var total = await query.CountAsync(ct);
        var items = await Sorted(query, sort)
            .Select(ToListItem)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<AssetListItem>(items, total, page, pageSize);
    }

    /// <summary>Streams every matching asset (capped) for CSV export.</summary>
    public IAsyncEnumerable<AssetListItem> StreamAssetsAsync(PortfolioFilter filter, AssetStatus? status, string? search,
        AssetSort sort, int max) =>
        Sorted(FilteredAssets(filter, status, search), sort).Select(ToListItem).Take(max).AsAsyncEnumerable();

    private IQueryable<Asset> FilteredAssets(PortfolioFilter filter, AssetStatus? status, string? search)
    {
        var query = filter.Normalize().Apply(db.Assets.AsNoTracking());
        if (status is not null) query = query.Where(a => a.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(a => EF.Functions.ILike(a.SerialNumber, pattern)
                                     || EF.Functions.ILike(a.Category, pattern)
                                     || EF.Functions.ILike(a.Contract.ContractNumber, pattern));
        }
        return query;
    }

    // Sorting happens on the entity, before projecting to the DTO, so EF Core can translate it.
    private static IQueryable<Asset> Sorted(IQueryable<Asset> query, AssetSort sort) => sort switch
    {
        AssetSort.Ltv => query
            .OrderByDescending(a => a.Status == AssetStatus.Sold
                                    || a.Contract.Assets.Where(x => x.Status != AssetStatus.Sold).Sum(x => x.MarketValue) == 0
                ? 0
                : a.Contract.OutstandingPrincipal / a.Contract.Assets.Where(x => x.Status != AssetStatus.Sold).Sum(x => x.MarketValue))
            .ThenBy(a => a.Id),
        AssetSort.DaysPastDue => query.OrderByDescending(a => a.Contract.DaysPastDue).ThenByDescending(a => a.MarketValue).ThenBy(a => a.Id),
        _ => query.OrderByDescending(a => a.MarketValue).ThenBy(a => a.Id),
    };

    public async Task<AssetDetail?> GetAssetAsync(long id, CancellationToken ct = default)
    {
        var row = await db.Assets.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new
            {
                Item = new AssetListItem(a.Id, a.SerialNumber, a.AssetClass, a.Category, a.Manufacturer, a.Model,
                    a.YearOfManufacture, a.Status, a.Condition, a.Country, a.City, a.MarketValue, a.ForcedSaleValue,
                    a.LastValuedAt, a.Contract.ContractNumber, a.Contract.DaysPastDue,
                    a.Status == AssetStatus.Sold ? (decimal?)null
                        : a.Contract.Assets.Where(x => x.Status != AssetStatus.Sold).Sum(x => x.MarketValue) == 0 ? (decimal?)null
                        : a.Contract.OutstandingPrincipal / a.Contract.Assets.Where(x => x.Status != AssetStatus.Sold).Sum(x => x.MarketValue)),
                a.OriginalCost,
                ContractCost = a.Contract.Assets.Sum(x => x.OriginalCost),
                a.Contract.OutstandingPrincipal,
                a.Contract.FinancedAmount,
                a.Contract.InterestRate,
                a.Contract.MonthlyInstallment,
                a.Contract.StartDate,
                a.Contract.TermMonths,
                a.Contract.ProductType,
                a.Contract.Status,
                CustomerName = a.Contract.Customer.Name,
                a.Contract.Customer.RiskGrade,
                VendorName = a.Contract.Vendor.Name,
                Valuations = a.Valuations.OrderBy(v => v.ValuedAt)
                    .Select(v => new { v.ValuedAt, v.MarketValue, v.ForcedSaleValue, v.Method }).ToList(),
            })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;

        // This asset's share of the contract balance, along the amortisation schedule at each valuation date.
        var share = row.ContractCost == 0 ? 0 : row.OriginalCost / row.ContractCost;
        var latest = row.Valuations.Count - 1;
        var points = row.Valuations.Select((v, i) =>
        {
            var exposure = i == latest || row.Status == ContractStatus.Closed
                ? row.OutstandingPrincipal
                : Amortization.OutstandingAfter(row.FinancedAmount, row.InterestRate, row.MonthlyInstallment,
                    MonthsBetween(row.StartDate, DateOnly.FromDateTime(v.ValuedAt)));
            return new ValuationPoint(v.ValuedAt, v.MarketValue, v.ForcedSaleValue, v.Method, Math.Round(exposure * share, 2));
        }).ToList();

        return new AssetDetail(row.Item, row.OriginalCost, row.OutstandingPrincipal, Math.Round(row.OutstandingPrincipal * share, 2),
            row.ProductType, row.TermMonths, row.StartDate, row.CustomerName, row.RiskGrade, row.VendorName, points);
    }

    private static readonly Expression<Func<Asset, AssetListItem>> ToListItem = a =>
        new AssetListItem(a.Id, a.SerialNumber, a.AssetClass, a.Category, a.Manufacturer, a.Model,
            a.YearOfManufacture, a.Status, a.Condition, a.Country, a.City, a.MarketValue, a.ForcedSaleValue,
            a.LastValuedAt, a.Contract.ContractNumber, a.Contract.DaysPastDue,
            a.Status == AssetStatus.Sold ? (decimal?)null
                : a.Contract.Assets.Where(x => x.Status != AssetStatus.Sold).Sum(x => x.MarketValue) == 0 ? (decimal?)null
                : a.Contract.OutstandingPrincipal / a.Contract.Assets.Where(x => x.Status != AssetStatus.Sold).Sum(x => x.MarketValue));

    private static int MonthsBetween(DateOnly from, DateOnly to) =>
        Math.Max(0, (to.Year - from.Year) * 12 + to.Month - from.Month - (to.Day < from.Day ? 1 : 0));

    private static decimal Ratio(decimal numerator, decimal denominator) =>
        denominator == 0 ? 0 : Math.Round(numerator / denominator, 4);
}
