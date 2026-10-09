using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Dashboard;
using AssetDashboard.Infrastructure.Remarketing;

namespace AssetDashboard.IntegrationTests;

[Collection(ApiCollection.Name)]
public class RemarketingApiTests(ApiFixture api)
{
    [Fact]
    public async Task Summary_has_pipeline_ageing_recovery_costs_and_upcoming_auctions()
    {
        var s = await api.GetAsync<RemarketingSummary>("/api/remarketing/summary");

        Assert.NotNull(s);
        Assert.True(s.Repossessed + s.Listed > 0);
        Assert.Equal(5, s.Ageing.Count);
        Assert.Equal(s.Repossessed + s.Listed, s.Ageing.Sum(a => a.Cases));
        Assert.NotEmpty(s.ByChannel);
        Assert.All(s.ByChannel, c => Assert.InRange(c.RecoveryRate, -1m, 2m));
        Assert.True(s.CostsLast12Months.Total > 0);
        Assert.NotEmpty(s.UpcomingAuctions);
        Assert.All(s.UpcomingAuctions, a => Assert.Equal(AuctionStatus.Scheduled, a.Status));
    }

    [Fact]
    public async Task Filtered_summary_only_counts_matching_assets()
    {
        var all = await api.GetAsync<RemarketingSummary>("/api/remarketing/summary");
        var agri = await api.GetAsync<RemarketingSummary>("/api/remarketing/summary?assetClass=Agriculture");

        Assert.True(agri!.Repossessed + agri.Listed <= all!.Repossessed + all.Listed);
        Assert.All(agri.ByClass, c => Assert.Equal(AssetClass.Agriculture, c.AssetClass));
    }

    [Theory]
    [InlineData("")]
    [InlineData("status=Sold")]
    [InlineData("status=Listed&channel=LiveAuction")]
    [InlineData("country=DE&assetClass=Construction")]
    public async Task Cases_list_filters_and_reports_costs(string query)
    {
        var page = await api.GetAsync<PagedResult<RemarketingCaseItem>>($"/api/remarketing/cases?{query}&pageSize=20");

        Assert.NotNull(page);
        Assert.All(page.Items, c =>
        {
            Assert.True(c.DaysInStock >= 0);
            Assert.Equal(c.TransportCost + c.StorageCost + c.RefurbishmentCost + c.SellingFees, c.TotalCosts);
            if (c.Status == RemarketingStatus.Sold) Assert.NotNull(c.SalePrice);
            if (query.Contains("LiveAuction")) Assert.NotNull(c.AuctionName);
        });
    }

    [Fact]
    public async Task Completed_auctions_have_lots_hammer_and_sell_through()
    {
        var page = await api.GetAsync<PagedResult<AuctionItem>>("/api/remarketing/auctions?status=Completed&pageSize=20");

        Assert.NotEmpty(page!.Items);
        Assert.All(page.Items, a =>
        {
            Assert.True(a.LotsSold > 0);
            Assert.True(a.HammerTotal > 0);
            Assert.InRange(a.SellThrough, 0.01m, 1m);
        });
        Assert.Equal(page.Items.Select(a => a.Date).OrderByDescending(d => d), page.Items.Select(a => a.Date));
    }
}
