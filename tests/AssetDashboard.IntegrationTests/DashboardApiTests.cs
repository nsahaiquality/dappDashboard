using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Dashboard;

namespace AssetDashboard.IntegrationTests;

/// <summary>Runs every dashboard query against real PostgreSQL, so EF Core translation problems fail here, not at runtime.</summary>
[Collection(ApiCollection.Name)]
public class DashboardApiTests(ApiFixture api)
{
    [Fact]
    public async Task Unfiltered_snapshot_is_internally_consistent()
    {
        var s = await api.GetAsync<PortfolioSnapshot>("/api/dashboard/snapshot");

        Assert.NotNull(s);
        Assert.InRange(s.OpenContracts, ApiFixture.SeedContracts * 9 / 10, ApiFixture.SeedContracts);
        Assert.Equal(s.TotalExposure, s.ByAssetClass.Sum(c => c.Exposure));
        Assert.Equal(s.TotalExposure, s.Delinquency.Sum(d => d.Exposure));
        Assert.Equal(s.ActiveAssets, s.ByCountry.Sum(c => c.Assets));
        Assert.Equal(PortfolioFilter.None.Key, s.Filter.Key);
    }

    public static TheoryData<string> FilterQueries => new()
    {
        "assetClass=Healthcare",
        "country=nl",
        "productType=Loan",
        "underwaterOnly=true",
        "assetClass=Agriculture&country=DE&productType=FinanceLease",
    };

    [Theory]
    [MemberData(nameof(FilterQueries))]
    public async Task Filtered_snapshot_is_a_subset_of_the_portfolio(string query)
    {
        var all = await api.GetAsync<PortfolioSnapshot>("/api/dashboard/snapshot");
        var filtered = await api.GetAsync<PortfolioSnapshot>($"/api/dashboard/snapshot?{query}");

        Assert.NotNull(filtered);
        Assert.True(filtered.OpenContracts <= all!.OpenContracts);
        Assert.True(filtered.TotalExposure <= all.TotalExposure);
        Assert.Equal(filtered.TotalExposure, filtered.ByAssetClass.Sum(c => c.Exposure));
    }

    [Fact]
    public async Task Underwater_filter_only_returns_contracts_above_100_percent_ltv()
    {
        var s = await api.GetAsync<PortfolioSnapshot>("/api/dashboard/snapshot?underwaterOnly=true");

        Assert.NotNull(s);
        Assert.True(s.OpenContracts > 0);
        Assert.True(s.LoanToValue > 1);
    }

    [Fact]
    public async Task Vendor_filter_matches_the_top_vendor_figures()
    {
        var all = await api.GetAsync<PortfolioSnapshot>("/api/dashboard/snapshot");
        var top = all!.TopVendors[0];

        var s = await api.GetAsync<PortfolioSnapshot>($"/api/dashboard/snapshot?vendorId={top.VendorId}");

        Assert.Equal(top.Contracts, s!.OpenContracts);
        Assert.Equal(top.Exposure, s.TotalExposure);
    }

    [Fact]
    public async Task Reference_data_lists_vendors_and_countries()
    {
        var r = await api.GetAsync<ReferenceData>("/api/reference");

        Assert.NotEmpty(r!.Vendors);
        Assert.Contains("NL", r.Countries);
    }

    [Theory]
    [InlineData("sort=MarketValue")]
    [InlineData("sort=Ltv")]
    [InlineData("sort=DaysPastDue")]
    [InlineData("sort=Ltv&underwaterOnly=true&assetClass=Healthcare")]
    [InlineData("status=Sold")]
    [InlineData("search=tractor&country=NL")]
    public async Task Asset_list_queries_translate_and_page(string query)
    {
        var page = await api.GetAsync<PagedResult<AssetListItem>>($"/api/assets?{query}&pageSize=20");

        Assert.NotNull(page);
        Assert.True(page.Items.Count <= 20);
        Assert.True(page.Total >= page.Items.Count);
    }

    [Fact]
    public async Task Ltv_sort_is_descending_and_sold_assets_have_no_ltv()
    {
        var byLtv = await api.GetAsync<PagedResult<AssetListItem>>("/api/assets?sort=Ltv&pageSize=50");
        var ltvs = byLtv!.Items.Select(a => a.ContractLtv ?? 0).ToList();
        Assert.Equal(ltvs.OrderByDescending(x => x), ltvs);

        var sold = await api.GetAsync<PagedResult<AssetListItem>>("/api/assets?status=Sold&pageSize=20");
        Assert.All(sold!.Items, a => Assert.Null(a.ContractLtv));
    }

    [Fact]
    public async Task Asset_detail_includes_exposure_history()
    {
        var page = await api.GetAsync<PagedResult<AssetListItem>>("/api/assets?status=InUse&pageSize=1");
        var detail = await api.GetAsync<AssetDetail>($"/api/assets/{page!.Items[0].Id}");

        Assert.NotNull(detail);
        Assert.NotEmpty(detail.Valuations);
        Assert.Equal(detail.AssetExposure, detail.Valuations[^1].ExposureShare);
        Assert.True(detail.AssetExposure <= detail.ContractExposure);
    }

    [Fact]
    public async Task Csv_export_has_header_and_one_line_per_asset()
    {
        var page = await api.GetAsync<PagedResult<AssetListItem>>("/api/assets?assetClass=CleanTech&pageSize=1");
        var csv = await api.Client.GetStringAsync("/api/assets/export?assetClass=CleanTech");
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("Serial,Category", lines[0]);
        Assert.Equal(page!.Total, lines.Length - 1);
    }
}
