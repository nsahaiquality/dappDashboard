using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace AssetDashboard.EndToEndTests;

/// <summary>The portfolio overview, driven in a real browser against the real system.</summary>
[Collection(SystemCollection.Name)]
public class OverviewTests(SystemFixture system) : E2ETest(system)
{
    /// <summary>The simulator is paused; give the last snapshot time to settle before comparing figures.</summary>
    private static Task SettleAsync() => Task.Delay(2500);

    private static ILocator Kpi(IPage page, string label) =>
        page.Locator(".kpi").Filter(new() { HasText = label }).Locator(".kpi-value");

    [Fact]
    public async Task Overview_loads_with_live_connection_and_all_sections()
    {
        var page = await OpenAsync();
        await WaitForDataAsync(page);

        await Assertions.Expect(page.Locator(".live")).ToContainTextAsync(new Regex("Live|Paused"));
        await Assertions.Expect(page.Locator(".kpi")).ToHaveCountAsync(6);
        foreach (var heading in new[] { "Exposure vs collateral by asset class", "Delinquency", "Remarketing & recovery",
                     "Top vendors by exposure", "Refinancing watchlist", "Live events" })
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = heading })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("button.class-row:not(.head)")).ToHaveCountAsync(7);
        await ScreenshotAsync(page, "overview-light");
    }

    [Fact]
    public async Task Headline_figures_on_screen_equal_the_database()
    {
        await SettleAsync();
        var page = await OpenAsync();
        await WaitForDataAsync(page);

        var exposure = await System.Sql.ScalarAsync<decimal>($"""SELECT SUM(c."OutstandingPrincipal") {Sql.OpenContracts}""");
        var market = await System.Sql.ScalarAsync<decimal>($"""SELECT SUM(a."MarketValue") {Sql.ActiveAssets}""");
        var contracts = await System.Sql.ScalarAsync<long>($"""SELECT COUNT(*) {Sql.OpenContracts}""");
        var assets = await System.Sql.ScalarAsync<long>($"""SELECT COUNT(*) {Sql.ActiveAssets}""");

        await Assertions.Expect(Kpi(page, "Total exposure")).ToHaveTextAsync(await FormatKpiEurAsync(page, exposure));
        await Assertions.Expect(Kpi(page, "Collateral market value")).ToHaveTextAsync(await FormatKpiEurAsync(page, market));
        await Assertions.Expect(Kpi(page, "Loan-to-value")).ToHaveTextAsync(await FormatPercentAsync(page, Math.Round(exposure / market, 4)));
        await Assertions.Expect(page.Locator(".filter-bar .filter-count"))
            .ToHaveTextAsync($"{await FormatIntAsync(page, contracts)} open contracts · {await FormatIntAsync(page, assets)} assets");
    }

    [Fact]
    public async Task Clicking_an_asset_class_filters_the_whole_page_and_clicking_again_clears_it()
    {
        await SettleAsync();
        var page = await OpenAsync();
        await WaitForDataAsync(page);

        await page.Locator("button.class-row:not(.head)").Filter(new() { HasText = "Healthcare" }).ClickAsync();
        await Assertions.Expect(page.Locator(".page-title .crumb")).ToContainTextAsync("Healthcare");
        await Assertions.Expect(page.Locator(".filter-bar")).Not.ToHaveClassAsync(new Regex("pending"));

        var contracts = await System.Sql.ScalarAsync<long>($"""SELECT COUNT(*) {Sql.OpenContracts} AND c."AssetClass" = 'Healthcare'""");
        var assets = await System.Sql.ScalarAsync<long>($"""SELECT COUNT(*) {Sql.ActiveAssets} AND a."AssetClass" = 'Healthcare'""");
        await Assertions.Expect(page.Locator(".filter-bar .filter-count"))
            .ToHaveTextAsync($"{await FormatIntAsync(page, contracts)} open contracts · {await FormatIntAsync(page, assets)} assets");
        await Assertions.Expect(page.Locator("button.class-row:not(.head)")).ToHaveCountAsync(1);

        await page.Locator("button.class-row:not(.head)").Filter(new() { HasText = "Healthcare" }).ClickAsync();
        await Assertions.Expect(page.Locator(".page-title .crumb")).ToContainTextAsync("All contracts");
        await Assertions.Expect(page.Locator("button.class-row:not(.head)")).ToHaveCountAsync(7);
    }

    [Fact]
    public async Task Underwater_only_shows_contracts_whose_debt_exceeds_collateral()
    {
        await SettleAsync();
        var page = await OpenAsync();
        await WaitForDataAsync(page);

        await page.GetByRole(AriaRole.Button, new() { Name = "Underwater only" }).ClickAsync();
        await Assertions.Expect(page.Locator(".filter-bar")).Not.ToHaveClassAsync(new Regex("pending"));

        var contracts = await System.Sql.ScalarAsync<long>($"""
            SELECT COUNT(*) {Sql.OpenContracts} AND c."OutstandingPrincipal" > COALESCE((SELECT SUM(a."MarketValue") FROM "Assets" a
                WHERE a."ContractId" = c."Id" AND a."Status" <> 'Sold'), 0)
            """);
        await Assertions.Expect(page.Locator(".filter-bar .filter-count")).ToContainTextAsync($"{await FormatIntAsync(page, contracts)} open contracts");
        var ltv = await Kpi(page, "Loan-to-value").InnerTextAsync();
        Assert.True(decimal.Parse(ltv.TrimEnd('%'), CultureInfo.InvariantCulture) > 100, $"underwater LTV should exceed 100%, was {ltv}");
    }
}
