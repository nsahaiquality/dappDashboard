using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Dashboard;
using AssetDashboard.Infrastructure.Data;
using AssetDashboard.Infrastructure.Refinancing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssetDashboard.IntegrationTests;

[Collection(ApiCollection.Name)]
public class RefinancingApiTests(ApiFixture api)
{
    [Fact]
    public async Task Summary_has_requests_decisions_expected_loss_and_maturity_wall()
    {
        var s = await api.GetAsync<RefinancingSummary>("/api/refinancing/summary");

        Assert.NotNull(s);
        Assert.True(s.Approved + s.Declined > 0);
        Assert.InRange(s.ApprovalRate, 0.01m, 0.99m);
        Assert.True(s.ExpectedLoss > 0);
        Assert.Equal(s.ExpectedLoss, s.ExpectedLossByClass.Sum(c => c.ExpectedLoss), 1);
        Assert.All(s.ExpectedLossByClass, c => Assert.True(c.ExpectedLoss <= c.Exposure));
        Assert.NotEmpty(s.MaturityWall);
        Assert.Equal(s.MaturityWall.OrderBy(q => q.Year).ThenBy(q => q.Quarter), s.MaturityWall);
    }

    [Fact]
    public async Task Filtered_summary_is_a_subset()
    {
        var all = await api.GetAsync<RefinancingSummary>("/api/refinancing/summary");
        var hc = await api.GetAsync<RefinancingSummary>("/api/refinancing/summary?assetClass=Healthcare&country=NL");

        Assert.True(hc!.ExpectedLoss <= all!.ExpectedLoss);
        Assert.All(hc.ExpectedLossByClass, c => Assert.Equal(AssetClass.Healthcare, c.AssetClass));
    }

    [Theory]
    [InlineData("")]
    [InlineData("status=Declined")]
    [InlineData("status=Approved&assetClass=Construction")]
    [InlineData("underwaterOnly=true")]
    public async Task Requests_list_filters_and_pages(string query)
    {
        var page = await api.GetAsync<PagedResult<RefinancingRequestItem>>($"/api/refinancing/requests?{query}&pageSize=10");

        Assert.NotNull(page);
        Assert.True(page.Items.Count <= 10);
        if (query.Contains("Declined")) Assert.All(page.Items, r => Assert.Equal(RefinancingStatus.Declined, r.Status));
        Assert.Equal(page.Items.Select(r => r.RequestedAt).OrderByDescending(d => d), page.Items.Select(r => r.RequestedAt));
    }

    [Fact]
    public async Task Candidates_have_triggers_and_risk_figures()
    {
        var page = await api.GetAsync<PagedResult<RefinancingCandidate>>("/api/refinancing/candidates?pageSize=25");

        Assert.NotNull(page);
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, c =>
        {
            Assert.NotEmpty(c.Triggers);
            Assert.InRange(c.ProbabilityOfDefault, 0.003m, 1m);
            Assert.InRange(c.LossGivenDefault, 0m, 1m);
            Assert.True(c.ExpectedLoss <= c.Exposure);
        });
    }

    /// <summary>
    /// The summary computes expected loss in SQL (PD as a CASE expression); the domain rule computes it in C#.
    /// Recomputing every open contract with CreditRisk.ExpectedLoss must give the same total.
    /// </summary>
    [Fact]
    public async Task Sql_expected_loss_matches_the_domain_rule_contract_by_contract()
    {
        var summary = await api.GetAsync<RefinancingSummary>("/api/refinancing/summary");

        await using var scope = api.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetDbContext>();
        var contracts = await db.Contracts.AsNoTracking()
            .Where(c => c.Status != ContractStatus.Closed)
            .Select(c => new
            {
                c.OutstandingPrincipal,
                c.DaysPastDue,
                c.Customer.RiskGrade,
                Forced = c.Assets.Where(a => a.Status != AssetStatus.Sold).Sum(a => a.ForcedSaleValue),
            })
            .ToListAsync();
        var expected = contracts.Sum(c => CreditRisk.ExpectedLoss(c.OutstandingPrincipal, c.Forced, c.RiskGrade, c.DaysPastDue));

        // C# rounds LGD to 4 decimals and each contract to cents; allow 0.1% for that rounding.
        Assert.InRange(summary!.ExpectedLoss, expected * 0.999m, expected * 1.001m);
    }
}
