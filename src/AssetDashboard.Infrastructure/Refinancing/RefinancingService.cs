using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Dashboard;
using AssetDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetDashboard.Infrastructure.Refinancing;

/// <summary>Refinancing pipeline, expected loss and maturity wall for the filtered portfolio.</summary>
public sealed record RefinancingSummary(
    int Submitted,
    int UnderReview,
    int Approved,
    int Declined,
    int Withdrawn,
    // Approved ÷ (approved + declined), last 12 months.
    decimal ApprovalRate,
    double AverageDaysToDecision,
    decimal PendingAmount,
    decimal ExpectedLoss,
    IReadOnlyList<ExpectedLossByClass> ExpectedLossByClass,
    IReadOnlyList<MaturityQuarter> MaturityWall,
    IReadOnlyList<ReasonStat> ByReason,
    PortfolioFilter Filter);

/// <summary>Exposure and expected loss (PD × LGD × exposure) of one asset class.</summary>
public sealed record ExpectedLossByClass(AssetClass AssetClass, decimal Exposure, decimal ExpectedLoss);

/// <summary>Exposure on contracts maturing in a quarter, and the balloon / residual amounts due then.</summary>
public sealed record MaturityQuarter(int Year, int Quarter, int Contracts, decimal Exposure, decimal BalloonDue);

/// <summary>Requests and decisions for one refinancing reason.</summary>
public sealed record ReasonStat(RefinancingReason Reason, int Requests, int Approved, int Declined);

/// <summary>A refinancing request with the risk picture at request time and the decision.</summary>
public sealed record RefinancingRequestItem(
    long Id, long ContractId, string ContractNumber, string CustomerName, AssetClass AssetClass,
    DateTime RequestedAt, RefinancingReason Reason, decimal RequestedAmount, int RequestedTermMonths,
    decimal CurrentRate, decimal? ProposedRate, decimal LtvAtRequest, decimal ForcedSaleCoverAtRequest,
    int RiskGradeAtRequest, decimal ExpectedLossAtRequest, RefinancingStatus Status, DateTime? DecidedAt, string? DecisionNote);

/// <summary>An open contract worth a refinancing conversation: maturing soon, balloon due, underwater or in arrears.</summary>
public sealed record RefinancingCandidate(
    long ContractId, string ContractNumber, string CustomerName, int RiskGrade, AssetClass AssetClass, ProductType ProductType,
    decimal Exposure, decimal MarketValue, decimal ForcedSaleValue, decimal LoanToValue, int DaysPastDue,
    DateOnly MaturityDate, decimal BalloonDue, decimal ProbabilityOfDefault, decimal LossGivenDefault, decimal ExpectedLoss,
    IReadOnlyList<string> Triggers);

public sealed class RefinancingService(AssetDbContext db)
{
    private const int CandidateHorizonMonths = 6;

    public async Task<RefinancingSummary> GetSummaryAsync(PortfolioFilter? filter, CancellationToken ct = default)
    {
        filter = (filter ?? PortfolioFilter.None).Normalize();
        var requests = Apply(filter, db.RefinancingRequests.AsNoTracking());
        var yearAgo = DateTime.UtcNow.AddYears(-1);

        var counts = await requests.GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var decidedLastYear = await requests
            .Where(r => r.DecidedAt != null && r.DecidedAt >= yearAgo && (r.Status == RefinancingStatus.Approved || r.Status == RefinancingStatus.Declined))
            .GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var approved = decidedLastYear.GetValueOrDefault(RefinancingStatus.Approved);
        var declined = decidedLastYear.GetValueOrDefault(RefinancingStatus.Declined);

        var decisionDays = await requests.Where(r => r.DecidedAt != null)
            .Select(r => new { r.RequestedAt, r.DecidedAt }).ToListAsync(ct);
        var pendingAmount = await requests
            .Where(r => r.Status == RefinancingStatus.Submitted || r.Status == RefinancingStatus.UnderReview)
            .SumAsync(r => r.RequestedAmount, ct);

        var byReason = await requests.GroupBy(r => r.Reason)
            .Select(g => new ReasonStat(g.Key, g.Count(),
                g.Count(r => r.Status == RefinancingStatus.Approved), g.Count(r => r.Status == RefinancingStatus.Declined)))
            .ToListAsync(ct);

        var open = OpenContracts(filter);
        var elByClass = await open
            .Select(c => new
            {
                c.AssetClass,
                c.OutstandingPrincipal,
                c.DaysPastDue,
                // Same numbers as CreditRisk.GradePd and its arrears uplift, written so EF Core emits SQL CASE expressions.
                Pd = c.DaysPastDue >= 90 ? 1m : c.DaysPastDue >= 60 ? 5m : c.DaysPastDue >= 30 ? 3m : c.DaysPastDue > 0 ? 1.5m : 1m,
                Grade = c.Customer.RiskGrade <= 1 ? 0.003m : c.Customer.RiskGrade == 2 ? 0.005m : c.Customer.RiskGrade == 3 ? 0.008m
                    : c.Customer.RiskGrade == 4 ? 0.012m : c.Customer.RiskGrade == 5 ? 0.020m : c.Customer.RiskGrade == 6 ? 0.032m
                    : c.Customer.RiskGrade == 7 ? 0.050m : c.Customer.RiskGrade == 8 ? 0.080m : c.Customer.RiskGrade == 9 ? 0.120m : 0.200m,
                Uncovered = c.OutstandingPrincipal
                            - c.Assets.Where(a => a.Status != AssetStatus.Sold).Sum(a => a.ForcedSaleValue) * (1 - CreditRisk.RecoveryCostRate),
            })
            .GroupBy(x => x.AssetClass)
            .Select(g => new
            {
                AssetClass = g.Key,
                Exposure = g.Sum(x => x.OutstandingPrincipal),
                // EL = min(1, PD × uplift) × max(0, exposure − net forced sale value)
                ExpectedLoss = g.Sum(x => (x.DaysPastDue >= 90 ? 1m : (x.Grade * x.Pd > 1m ? 1m : x.Grade * x.Pd))
                                          * (x.Uncovered > 0 ? x.Uncovered : 0)),
            })
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var horizon = today.AddMonths(24);
        var wall = await open
            .Where(c => c.MaturityDate >= today && c.MaturityDate < horizon)
            .GroupBy(c => new { c.MaturityDate.Year, Quarter = (c.MaturityDate.Month - 1) / 3 + 1 })
            .Select(g => new MaturityQuarter(g.Key.Year, g.Key.Quarter, g.Count(), g.Sum(c => c.OutstandingPrincipal), g.Sum(c => c.ResidualValue)))
            .ToListAsync(ct);

        return new RefinancingSummary(
            counts.GetValueOrDefault(RefinancingStatus.Submitted),
            counts.GetValueOrDefault(RefinancingStatus.UnderReview),
            counts.GetValueOrDefault(RefinancingStatus.Approved),
            counts.GetValueOrDefault(RefinancingStatus.Declined),
            counts.GetValueOrDefault(RefinancingStatus.Withdrawn),
            approved + declined == 0 ? 0 : Math.Round((decimal)approved / (approved + declined), 4),
            decisionDays.Count == 0 ? 0 : Math.Round(decisionDays.Average(d => (d.DecidedAt!.Value - d.RequestedAt).TotalDays), 1),
            pendingAmount,
            Math.Round(elByClass.Sum(x => x.ExpectedLoss), 2),
            elByClass.Select(x => new ExpectedLossByClass(x.AssetClass, x.Exposure, Math.Round(x.ExpectedLoss, 2)))
                .OrderByDescending(x => x.ExpectedLoss).ToList(),
            wall.OrderBy(q => q.Year).ThenBy(q => q.Quarter).ToList(),
            byReason.OrderByDescending(r => r.Requests).ToList(),
            filter);
    }

    public async Task<PagedResult<RefinancingRequestItem>> GetRequestsAsync(PortfolioFilter? filter, RefinancingStatus? status,
        int page, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(1, page);
        var query = Apply((filter ?? PortfolioFilter.None).Normalize(), db.RefinancingRequests.AsNoTracking());
        if (status is not null) query = query.Where(r => r.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.RequestedAt).ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new RefinancingRequestItem(r.Id, r.ContractId, r.Contract.ContractNumber, r.Contract.Customer.Name,
                r.Contract.AssetClass, r.RequestedAt, r.Reason, r.RequestedAmount, r.RequestedTermMonths, r.CurrentRate, r.ProposedRate,
                r.LtvAtRequest, r.ForcedSaleCoverAtRequest, r.RiskGradeAtRequest, r.ExpectedLossAtRequest, r.Status, r.DecidedAt, r.DecisionNote))
            .ToListAsync(ct);
        return new PagedResult<RefinancingRequestItem>(items, total, page, pageSize);
    }

    /// <summary>
    /// Open contracts that need attention: maturing within 6 months, balloon due, underwater or 30+ dpd.
    /// Filtering happens in SQL; risk figures are computed per page and the page is sorted by expected loss.
    /// </summary>
    public async Task<PagedResult<RefinancingCandidate>> GetCandidatesAsync(PortfolioFilter? filter, int page, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(1, page);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var horizon = today.AddMonths(CandidateHorizonMonths);

        var query = OpenContracts((filter ?? PortfolioFilter.None).Normalize())
            .Select(c => new
            {
                c.Id,
                c.ContractNumber,
                CustomerName = c.Customer.Name,
                c.Customer.RiskGrade,
                c.AssetClass,
                c.ProductType,
                c.OutstandingPrincipal,
                c.DaysPastDue,
                c.MaturityDate,
                c.ResidualValue,
                Market = c.Assets.Where(a => a.Status != AssetStatus.Sold).Sum(a => a.MarketValue),
                Forced = c.Assets.Where(a => a.Status != AssetStatus.Sold).Sum(a => a.ForcedSaleValue),
            })
            .Where(c => c.MaturityDate < horizon || c.DaysPastDue >= 30 || c.OutstandingPrincipal > c.Market);

        var total = await query.CountAsync(ct);
        // Rank by the uncovered part of the exposure (the driver of expected loss), then compute exact risk per row.
        var rows = await query
            .OrderByDescending(c => c.OutstandingPrincipal - c.Forced * (1 - CreditRisk.RecoveryCostRate))
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        var items = rows.Select(c =>
        {
            var triggers = new List<string>();
            if (c.MaturityDate < horizon) triggers.Add(c.ResidualValue > 0 ? "Balloon due" : "Maturing");
            if (c.OutstandingPrincipal > c.Market) triggers.Add("Underwater");
            if (c.DaysPastDue >= 30) triggers.Add("In arrears");
            return new RefinancingCandidate(c.Id, c.ContractNumber, c.CustomerName, c.RiskGrade, c.AssetClass, c.ProductType,
                c.OutstandingPrincipal, c.Market, c.Forced, c.Market == 0 ? 0 : Math.Round(c.OutstandingPrincipal / c.Market, 4),
                c.DaysPastDue, c.MaturityDate, c.MaturityDate < horizon ? c.ResidualValue : 0,
                CreditRisk.ProbabilityOfDefault(c.RiskGrade, c.DaysPastDue),
                CreditRisk.LossGivenDefault(c.OutstandingPrincipal, c.Forced),
                CreditRisk.ExpectedLoss(c.OutstandingPrincipal, c.Forced, c.RiskGrade, c.DaysPastDue),
                triggers);
        }).ToList();
        return new PagedResult<RefinancingCandidate>(items, total, page, pageSize);
    }

    private IQueryable<Contract> OpenContracts(PortfolioFilter filter) =>
        filter.Apply(db.Contracts.AsNoTracking().Where(c => c.Status != ContractStatus.Closed));

    private static IQueryable<RefinancingRequest> Apply(PortfolioFilter f, IQueryable<RefinancingRequest> q)
    {
        if (f.AssetClass is { } cls) q = q.Where(r => r.Contract.AssetClass == cls);
        if (f.Country is { } country) q = q.Where(r => r.Contract.Customer.Country == country);
        if (f.VendorId is { } vendorId) q = q.Where(r => r.Contract.VendorId == vendorId);
        if (f.ProductType is { } product) q = q.Where(r => r.Contract.ProductType == product);
        if (f.UnderwaterOnly) q = q.Where(r => r.LtvAtRequest > 1);
        return q;
    }
}
