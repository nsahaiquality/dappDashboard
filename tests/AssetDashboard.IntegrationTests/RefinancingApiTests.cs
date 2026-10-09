using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Dashboard;
using AssetDashboard.Infrastructure.Refinancing;

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
}
