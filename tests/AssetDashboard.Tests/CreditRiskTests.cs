using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Seeding;

namespace AssetDashboard.Tests;

public class CreditRiskTests
{
    [Fact]
    public void Pd_rises_with_grade_and_arrears_and_is_certain_at_default()
    {
        Assert.True(CreditRisk.ProbabilityOfDefault(2, 0) < CreditRisk.ProbabilityOfDefault(8, 0));
        Assert.True(CreditRisk.ProbabilityOfDefault(5, 0) < CreditRisk.ProbabilityOfDefault(5, 45));
        Assert.Equal(1m, CreditRisk.ProbabilityOfDefault(1, 95));
    }

    [Theory]
    [InlineData(100_000, 200_000, 0)]        // well covered: no loss
    [InlineData(100_000, 50_000, 0.53)]      // 100k − 50k × 0.94 = 53k uncovered
    [InlineData(100_000, 0, 1)]              // no collateral
    public void Lgd_is_the_share_not_covered_by_a_net_forced_sale(decimal exposure, decimal forced, decimal expected)
    {
        Assert.Equal(expected, CreditRisk.LossGivenDefault(exposure, forced));
    }

    [Fact]
    public void Expected_loss_is_pd_times_lgd_times_exposure()
    {
        // Grade 5 → PD 2%; LGD 0.53 → EL = 0.02 × 0.53 × 100k = 1,060.
        Assert.Equal(1_060m, CreditRisk.ExpectedLoss(100_000, 50_000, 5, 0));
    }
}

public class RefinancingPolicyTests
{
    [Theory]
    [InlineData(RefinancingReason.Expansion, 1.2, 3, 0, RefinancingStatus.Declined)]   // LTV above 110%
    [InlineData(RefinancingReason.Expansion, 0.7, 9, 0, RefinancingStatus.Declined)]   // grade outside appetite
    [InlineData(RefinancingReason.CashFlowStress, 0.9, 6, 120, RefinancingStatus.Declined)] // already defaulted
    [InlineData(RefinancingReason.RateReduction, 0.6, 6, 0, RefinancingStatus.Declined)]    // rate cut needs grade ≤ 4
    [InlineData(RefinancingReason.Expansion, 0.7, 4, 0, RefinancingStatus.Approved)]
    [InlineData(RefinancingReason.CashFlowStress, 0.95, 6, 45, RefinancingStatus.Approved)]
    public void Policy_decides_by_ltv_grade_and_arrears(RefinancingReason reason, double ltv, int grade, int dpd, RefinancingStatus expected)
    {
        Assert.Equal(expected, RefinancingPolicy.Decide(reason, (decimal)ltv, grade, dpd, 5m).Status);
    }

    [Fact]
    public void Riskier_approvals_are_priced_higher()
    {
        var safe = RefinancingPolicy.Decide(RefinancingReason.Expansion, 0.6m, 3, 0, 5m).ProposedRate!.Value;
        var risky = RefinancingPolicy.Decide(RefinancingReason.Expansion, 0.95m, 7, 35, 5m).ProposedRate!.Value;
        Assert.True(risky > safe);
    }

    [Fact]
    public void Generated_requests_are_consistent()
    {
        var asOf = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);
        var contracts = new PortfolioGenerator(11).Generate(4_000, asOf).Contracts;
        var requests = contracts.SelectMany(c => c.RefinancingRequests.Select(r => (c, r))).ToList();

        Assert.InRange(requests.Count, 100, 400);
        Assert.All(contracts, c => Assert.Equal(c.StartDate.AddMonths(c.TermMonths), c.MaturityDate));
        Assert.All(requests, x =>
        {
            Assert.True(x.r.RequestedAt <= asOf.AddDays(1));
            if (x.r.DecidedAt is { } decided) Assert.InRange(decided, x.r.RequestedAt, asOf.AddDays(1));
            if (x.r.Status == RefinancingStatus.Approved) Assert.NotNull(x.r.ProposedRate);
        });
        Assert.Contains(requests, x => x.r.Status == RefinancingStatus.Declined);
        Assert.Contains(requests, x => x.r.Status is RefinancingStatus.Submitted or RefinancingStatus.UnderReview);
    }
}
