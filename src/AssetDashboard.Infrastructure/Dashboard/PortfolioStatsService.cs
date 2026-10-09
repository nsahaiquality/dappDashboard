using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetDashboard.Infrastructure.Dashboard;

/// <summary>Computes portfolio statistics with server-side (SQL) aggregation.</summary>
public sealed class PortfolioStatsService(AssetDbContext db)
{
    public async Task<PortfolioSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var openContracts = db.Contracts.AsNoTracking().Where(c => c.Status != ContractStatus.Closed);
        var activeAssets = db.Assets.AsNoTracking().Where(a => a.Status != AssetStatus.Sold);

        var contractTotals = await openContracts
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Exposure = g.Sum(c => c.OutstandingPrincipal),
                AtRisk = g.Where(c => c.DaysPastDue >= 30).Sum(c => c.OutstandingPrincipal),
            })
            .FirstOrDefaultAsync(ct);

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
            .Select(g => new VendorStat(g.Key.VendorId, g.Key.Name, g.Count(), g.Sum(c => c.OutstandingPrincipal)))
            .OrderByDescending(v => v.Exposure)
            .Take(10)
            .ToListAsync(ct);

        var byCountry = await activeAssets
            .GroupBy(a => a.Country)
            .Select(g => new CountryStat(g.Key, g.Count(), g.Sum(a => a.MarketValue)))
            .OrderByDescending(c => c.MarketValue)
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
            Remarketing: await GetRemarketingAsync(ct),
            TopVendors: topVendors,
            ByCountry: byCountry);
    }

    private async Task<RemarketingStat> GetRemarketingAsync(CancellationToken ct)
    {
        var cases = db.RemarketingCases.AsNoTracking();
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
            .FirstOrDefaultAsync(ct);

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

    public async Task<PagedResult<AssetListItem>> GetAssetsAsync(AssetClass? assetClass, AssetStatus? status,
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(1, page);

        var query = db.Assets.AsNoTracking();
        if (assetClass is not null) query = query.Where(a => a.AssetClass == assetClass);
        if (status is not null) query = query.Where(a => a.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(a => EF.Functions.ILike(a.SerialNumber, pattern)
                                     || EF.Functions.ILike(a.Category, pattern)
                                     || EF.Functions.ILike(a.Contract.ContractNumber, pattern));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.MarketValue)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToListItem)
            .ToListAsync(ct);

        return new PagedResult<AssetListItem>(items, total, page, pageSize);
    }

    public async Task<AssetDetail?> GetAssetAsync(long id, CancellationToken ct = default)
    {
        var asset = await db.Assets.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new
            {
                Item = new AssetListItem(a.Id, a.SerialNumber, a.AssetClass, a.Category, a.Manufacturer, a.Model,
                    a.YearOfManufacture, a.Status, a.Condition, a.Country, a.City, a.MarketValue, a.ForcedSaleValue,
                    a.LastValuedAt, a.Contract.ContractNumber, a.Contract.DaysPastDue),
                a.OriginalCost,
                a.Contract.OutstandingPrincipal,
                CustomerName = a.Contract.Customer.Name,
                a.Contract.Customer.RiskGrade,
                VendorName = a.Contract.Vendor.Name,
                Valuations = a.Valuations.OrderBy(v => v.ValuedAt)
                    .Select(v => new ValuationPoint(v.ValuedAt, v.MarketValue, v.ForcedSaleValue, v.Method)).ToList(),
            })
            .FirstOrDefaultAsync(ct);

        return asset is null
            ? null
            : new AssetDetail(asset.Item, asset.OriginalCost, asset.OutstandingPrincipal, asset.CustomerName,
                asset.RiskGrade, asset.VendorName, asset.Valuations);
    }

    private static readonly System.Linq.Expressions.Expression<Func<Asset, AssetListItem>> ToListItem = a =>
        new AssetListItem(a.Id, a.SerialNumber, a.AssetClass, a.Category, a.Manufacturer, a.Model,
            a.YearOfManufacture, a.Status, a.Condition, a.Country, a.City, a.MarketValue, a.ForcedSaleValue,
            a.LastValuedAt, a.Contract.ContractNumber, a.Contract.DaysPastDue);

    private static decimal Ratio(decimal numerator, decimal denominator) =>
        denominator == 0 ? 0 : Math.Round(numerator / denominator, 4);
}
