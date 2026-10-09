using AssetDashboard.Infrastructure.Dashboard;
using AssetDashboard.Infrastructure.Vendors;

namespace AssetDashboard.IntegrationTests;

[Collection(ApiCollection.Name)]
public class VendorApiTests(ApiFixture api)
{
    [Theory]
    [InlineData("sort=Exposure")]
    [InlineData("sort=DefaultRate")]
    [InlineData("sort=Origination")]
    [InlineData("sort=TargetAttainment&assetClass=Healthcare")]
    public async Task Vendor_list_sorts_and_reports_performance(string query)
    {
        var page = await api.GetAsync<PagedResult<VendorPerformance>>($"/api/vendors?{query}&pageSize=50");

        Assert.NotNull(page);
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, v =>
        {
            Assert.InRange(v.DefaultRate, 0m, 1m);
            Assert.True(v.OpenContracts <= v.Contracts);
            Assert.True(v.AnnualVolumeTarget > 0);
        });
    }

    [Fact]
    public async Task Vendor_exposure_matches_the_portfolio_snapshot()
    {
        var snapshot = await api.GetAsync<PortfolioSnapshot>("/api/dashboard/snapshot");
        var vendors = await api.GetAsync<PagedResult<VendorPerformance>>("/api/vendors?pageSize=200");

        Assert.Equal(snapshot!.TotalExposure, vendors!.Items.Sum(v => v.Exposure));
        Assert.Equal(snapshot.TopVendors[0].VendorId, vendors.Items[0].Id);
    }

    [Fact]
    public async Task Vendor_detail_has_24_months_of_origination_and_mixes()
    {
        var top = (await api.GetAsync<PagedResult<VendorPerformance>>("/api/vendors?pageSize=1"))!.Items[0];
        var detail = await api.GetAsync<VendorDetail>($"/api/vendors/{top.Id}");

        Assert.NotNull(detail);
        Assert.Equal(24, detail.Origination.Count);
        Assert.Equal(top.Exposure, detail.ByAssetClass.Sum(c => c.Exposure));
        Assert.Equal(top.Exposure, detail.Delinquency.Sum(d => d.Exposure));
        Assert.InRange(detail.TopCustomers.Count, 1, 5);
    }

    [Fact]
    public async Task Unknown_vendor_is_not_found()
    {
        var response = await api.Client.GetAsync("/api/vendors/999999");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
