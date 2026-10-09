using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Dashboard;
using AssetDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetDashboard.Infrastructure.Remarketing;

/// <summary>Recovery pipeline, stock ageing, recovery rates, costs and upcoming auctions.</summary>
public sealed record RemarketingSummary(
    int Repossessed,
    int Listed,
    int SoldLast12Months,
    // Unsold stock: forced sale value, exposure at default and costs accrued so far.
    decimal StockForcedSaleValue,
    decimal StockExposureAtDefault,
    decimal StockAccruedCosts,
    IReadOnlyList<AgeingBucket> Ageing,
    IReadOnlyList<ChannelRecovery> ByChannel,
    IReadOnlyList<ClassRecovery> ByClass,
    CostBreakdown CostsLast12Months,
    IReadOnlyList<AuctionItem> UpcomingAuctions,
    PortfolioFilter Filter);

/// <summary>Unsold repossessed stock by days since repossession.</summary>
public sealed record AgeingBucket(string Bucket, int Cases, decimal ForcedSaleValue, decimal AccruedCosts);

/// <summary>Sales and recovery through one channel in the last 12 months.</summary>
public sealed record ChannelRecovery(RemarketingChannel Channel, int Sold, decimal Proceeds, decimal Costs, decimal ExposureAtDefault,
    decimal RecoveryRate, double AverageDaysToSell);

/// <summary>Sales and recovery for one asset class in the last 12 months.</summary>
public sealed record ClassRecovery(AssetClass AssetClass, int Sold, decimal RecoveryRate, double AverageDaysToSell);

/// <summary>Remarketing costs by type.</summary>
public sealed record CostBreakdown(decimal Transport, decimal Storage, decimal Refurbishment, decimal SellingFees)
{
    public decimal Total => Transport + Storage + Refurbishment + SellingFees;
}

/// <summary>An auction with lot statistics; SellThrough = sold ÷ (sold + passed in).</summary>
public sealed record AuctionItem(int Id, string Name, RemarketingChannel Channel, DateOnly Date, string Location, AuctionStatus Status,
    int Lots, int LotsSold, int LotsPassedIn, decimal HammerTotal, decimal ReserveTotal, double AverageBids, decimal SellThrough);

/// <summary>A repossessed asset on its way to sale, with costs and recovery.</summary>
public sealed record RemarketingCaseItem(
    long Id, long AssetId, string SerialNumber, string Category, AssetClass AssetClass, string Country, string? Yard,
    RemarketingStatus Status, RemarketingChannel Channel, DateOnly RepossessedOn, DateOnly? ListedOn, DateOnly? SoldOn, int DaysInStock,
    decimal ExposureAtDefault, decimal ForcedSaleValue, decimal ReservePrice, decimal? AskingPrice, int PriceReductions, decimal? SalePrice,
    decimal TransportCost, decimal StorageCost, decimal RefurbishmentCost, decimal SellingFees, decimal TotalCosts, decimal? RecoveryRate,
    string? AuctionName, DateOnly? AuctionDate, int? Bids, int TimesPassedIn);

public sealed class RemarketingService(AssetDbContext db)
{
    private static readonly string[] AgeingNames = ["0–30 days", "31–60 days", "61–90 days", "91–180 days", "180+ days"];

    public async Task<RemarketingSummary> GetSummaryAsync(PortfolioFilter? filter, CancellationToken ct = default)
    {
        filter = (filter ?? PortfolioFilter.None).Normalize();
        var cases = filter.Apply(db.RemarketingCases.AsNoTracking());
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var todayNumber = today.DayNumber;
        var yearAgo = today.AddYears(-1);

        var counts = await cases.GroupBy(c => c.Status).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var stock = cases.Where(c => c.Status != RemarketingStatus.Sold);
        var ageing = await stock
            .Select(c => new
            {
                Days = todayNumber - c.RepossessedOn.DayNumber,
                c.Asset.ForcedSaleValue,
                Accrued = c.TransportCost + c.RefurbishmentCost + (todayNumber - c.RepossessedOn.DayNumber) * c.DailyStorageRate,
                c.ExposureAtDefault,
            })
            .GroupBy(x => x.Days <= 30 ? 0 : x.Days <= 60 ? 1 : x.Days <= 90 ? 2 : x.Days <= 180 ? 3 : 4)
            .Select(g => new { Bucket = g.Key, Count = g.Count(), Fsv = g.Sum(x => x.ForcedSaleValue), Accrued = g.Sum(x => x.Accrued), Ead = g.Sum(x => x.ExposureAtDefault) })
            .ToListAsync(ct);

        var soldRecent = cases.Where(c => c.Status == RemarketingStatus.Sold && c.SoldOn >= yearAgo);
        var byChannel = await soldRecent
            .GroupBy(c => c.Channel)
            .Select(g => new
            {
                Channel = g.Key,
                Sold = g.Count(),
                Proceeds = g.Sum(c => c.SalePrice ?? 0),
                Costs = g.Sum(c => c.RecoveryCosts),
                Ead = g.Sum(c => c.ExposureAtDefault),
                Days = g.Average(c => (double)(c.SoldOn!.Value.DayNumber - c.RepossessedOn.DayNumber)),
            })
            .ToListAsync(ct);
        var byClass = await soldRecent
            .GroupBy(c => c.Asset.AssetClass)
            .Select(g => new
            {
                AssetClass = g.Key,
                Sold = g.Count(),
                Net = g.Sum(c => (c.SalePrice ?? 0) - c.RecoveryCosts),
                Ead = g.Sum(c => c.ExposureAtDefault),
                Days = g.Average(c => (double)(c.SoldOn!.Value.DayNumber - c.RepossessedOn.DayNumber)),
            })
            .ToListAsync(ct);
        var costs = await soldRecent
            .GroupBy(_ => 1)
            .Select(g => new CostBreakdown(g.Sum(c => c.TransportCost), g.Sum(c => c.StorageCost), g.Sum(c => c.RefurbishmentCost), g.Sum(c => c.SellingFees)))
            .SingleOrDefaultAsync(ct) ?? new CostBreakdown(0, 0, 0, 0);

        var upcoming = await Project(Auctions(filter, AuctionStatus.Scheduled).OrderBy(a => a.Date).ThenBy(a => a.Id).Take(5), filter)
            .ToListAsync(ct);

        return new RemarketingSummary(
            counts.GetValueOrDefault(RemarketingStatus.Repossessed),
            counts.GetValueOrDefault(RemarketingStatus.Listed),
            byChannel.Sum(c => c.Sold),
            ageing.Sum(a => a.Fsv),
            ageing.Sum(a => a.Ead),
            Math.Round(ageing.Sum(a => a.Accrued), 2),
            AgeingNames.Select((name, i) => ageing.FirstOrDefault(a => a.Bucket == i) is { } a
                ? new AgeingBucket(name, a.Count, a.Fsv, Math.Round(a.Accrued, 2))
                : new AgeingBucket(name, 0, 0, 0)).ToList(),
            byChannel.Select(c => new ChannelRecovery(c.Channel, c.Sold, c.Proceeds, c.Costs, c.Ead,
                    c.Ead == 0 ? 0 : Math.Round((c.Proceeds - c.Costs) / c.Ead, 4), Math.Round(c.Days, 1)))
                .OrderByDescending(c => c.Sold).ToList(),
            byClass.Select(c => new ClassRecovery(c.AssetClass, c.Sold, c.Ead == 0 ? 0 : Math.Round(c.Net / c.Ead, 4), Math.Round(c.Days, 1)))
                .OrderByDescending(c => c.Sold).ToList(),
            costs,
            upcoming.Select(ToItem).ToList(),
            filter);
    }

    public async Task<PagedResult<RemarketingCaseItem>> GetCasesAsync(PortfolioFilter? filter, RemarketingStatus? status,
        RemarketingChannel? channel, int page, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(1, page);
        var todayNumber = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;
        var query = (filter ?? PortfolioFilter.None).Normalize().Apply(db.RemarketingCases.AsNoTracking());
        if (status is not null) query = query.Where(c => c.Status == status);
        if (channel is not null) query = query.Where(c => c.Channel == channel);

        var total = await query.CountAsync(ct);
        // Unsold stock first, oldest first; then sales, most recent first.
        var items = await query
            .OrderBy(c => c.Status == RemarketingStatus.Sold)
            .ThenBy(c => c.Status == RemarketingStatus.Sold ? 0 : c.RepossessedOn.DayNumber)
            .ThenByDescending(c => c.SoldOn)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new
            {
                c.Id, c.AssetId, c.Asset.SerialNumber, c.Asset.Category, c.Asset.AssetClass, c.Asset.Country, c.Yard,
                c.Status, c.Channel, c.RepossessedOn, c.ListedOn, c.SoldOn, c.ExposureAtDefault, c.Asset.ForcedSaleValue,
                c.ReservePrice, c.AskingPrice, c.PriceReductions, c.SalePrice, c.TransportCost, c.StorageCost, c.RefurbishmentCost,
                c.SellingFees, c.RecoveryCosts, c.DailyStorageRate, AuctionName = c.Auction != null ? c.Auction.Name : null,
                AuctionDate = c.Auction != null ? c.Auction.Date : (DateOnly?)null, c.Bids, c.TimesPassedIn,
            })
            .ToListAsync(ct);

        return new PagedResult<RemarketingCaseItem>(items.Select(c =>
        {
            var end = c.SoldOn?.DayNumber ?? todayNumber;
            var days = end - c.RepossessedOn.DayNumber;
            // Storage accrues until the sale; for unsold stock, count up to today.
            var storage = c.SoldOn is null ? Math.Round(days * c.DailyStorageRate, 2) : c.StorageCost;
            var totalCosts = c.TransportCost + storage + c.RefurbishmentCost + c.SellingFees;
            decimal? recovery = c.SalePrice is { } sale && c.ExposureAtDefault > 0 ? Math.Round((sale - totalCosts) / c.ExposureAtDefault, 4) : null;
            return new RemarketingCaseItem(c.Id, c.AssetId, c.SerialNumber, c.Category, c.AssetClass, c.Country, c.Yard, c.Status, c.Channel,
                c.RepossessedOn, c.ListedOn, c.SoldOn, days, c.ExposureAtDefault, c.ForcedSaleValue, c.ReservePrice, c.AskingPrice,
                c.PriceReductions, c.SalePrice, c.TransportCost, storage, c.RefurbishmentCost, c.SellingFees, totalCosts, recovery,
                c.AuctionName, c.AuctionDate, c.Bids, c.TimesPassedIn);
        }).ToList(), total, page, pageSize);
    }

    public async Task<PagedResult<AuctionItem>> GetAuctionsAsync(PortfolioFilter? filter, AuctionStatus? status, int page, int pageSize,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(1, page);
        filter = (filter ?? PortfolioFilter.None).Normalize();
        var query = Auctions(filter, status);
        var total = await query.CountAsync(ct);
        // Sort and page the auctions first, then project; EF Core cannot sort on the projected record.
        var ordered = status == AuctionStatus.Scheduled
            ? query.OrderBy(a => a.Date).ThenBy(a => a.Id)
            : query.OrderByDescending(a => a.Date).ThenBy(a => a.Id);
        var rows = await Project(ordered.Skip((page - 1) * pageSize).Take(pageSize), filter).ToListAsync(ct);
        return new PagedResult<AuctionItem>(rows.Select(ToItem).ToList(), total, page, pageSize);
    }

    private sealed record AuctionRow(int Id, string Name, RemarketingChannel Channel, DateOnly Date, string Location, AuctionStatus Status,
        int Lots, int LotsSold, int LotsPassedIn, decimal HammerTotal, decimal ReserveTotal, double AverageBids);

    /// <summary>Auctions in a status; with a filter, only auctions holding at least one matching lot.</summary>
    private IQueryable<Auction> Auctions(PortfolioFilter filter, AuctionStatus? status)
    {
        var auctions = db.Auctions.AsNoTracking();
        if (status is not null) auctions = auctions.Where(a => a.Status == status);
        if (filter != PortfolioFilter.None)
        {
            var lots = filter.Apply(db.RemarketingCases.AsNoTracking());
            auctions = auctions.Where(a => lots.Any(l => l.AuctionId == a.Id));
        }
        return auctions;
    }

    /// <summary>Lot statistics per auction, over the lots matching the filter.</summary>
    private IQueryable<AuctionRow> Project(IQueryable<Auction> auctions, PortfolioFilter filter)
    {
        var lots = filter.Apply(db.RemarketingCases.AsNoTracking());
        return auctions.Select(a => new AuctionRow(a.Id, a.Name, a.Channel, a.Date, a.Location, a.Status,
            lots.Count(l => l.AuctionId == a.Id),
            lots.Count(l => l.AuctionId == a.Id && l.Status == RemarketingStatus.Sold),
            a.LotsPassedIn,
            lots.Where(l => l.AuctionId == a.Id).Sum(l => l.SalePrice ?? 0),
            lots.Where(l => l.AuctionId == a.Id).Sum(l => l.ReservePrice),
            lots.Where(l => l.AuctionId == a.Id && l.Bids != null).Average(l => (double?)l.Bids) ?? 0));
    }

    private static AuctionItem ToItem(AuctionRow a)
    {
        var offered = a.LotsSold + a.LotsPassedIn;
        return new AuctionItem(a.Id, a.Name, a.Channel, a.Date, a.Location, a.Status, a.Lots, a.LotsSold, a.LotsPassedIn,
            a.HammerTotal, a.ReserveTotal, Math.Round(a.AverageBids, 1),
            a.Status == AuctionStatus.Completed && offered > 0 ? Math.Round((decimal)a.LotsSold / offered, 4) : 0);
    }
}
