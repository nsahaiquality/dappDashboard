using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace AssetDashboard.EndToEndTests;

/// <summary>The asset explorer: filters carried from the overview, sorting, detail panel, CSV export.</summary>
[Collection(SystemCollection.Name)]
public class AssetExplorerTests(SystemFixture system) : E2ETest(system)
{
    private static ILocator Rows(IPage page) => page.Locator("tbody tr.clickable");

    private static async Task<long> ShownTotalAsync(IPage page)
    {
        var text = await page.Locator(".table-tools .filter-count").InnerTextAsync(); // "2,345 assets"
        return long.Parse(Regex.Replace(text, "[^0-9]", ""));
    }

    [Fact]
    public async Task A_filter_set_on_the_overview_carries_to_the_asset_explorer()
    {
        var page = await OpenAsync();
        await WaitForDataAsync(page);
        await page.GetByLabel("Country").SelectOptionAsync("NL");
        await Assertions.Expect(page.Locator(".page-title .crumb")).ToContainTextAsync("Country: NL");

        await page.GetByRole(AriaRole.Link, new() { Name = "Assets" }).ClickAsync();
        await Rows(page).First.WaitForAsync();

        await Assertions.Expect(page.Locator(".chip.removable").Filter(new() { HasText = "Country: NL" })).ToBeVisibleAsync();
        var expected = await System.Sql.ScalarAsync<long>("""SELECT COUNT(*) FROM "Assets" WHERE "Country" = 'NL'""");
        await Assertions.Expect(page.Locator(".table-tools .filter-count")).ToHaveTextAsync($"{await FormatIntAsync(page, expected)} assets");
        Assert.All(await page.Locator("tbody tr.clickable td:nth-child(3)").AllInnerTextsAsync(), loc => Assert.EndsWith(", NL", loc));

        // Removing the chip clears the global filter.
        await page.GetByRole(AriaRole.Button, new() { Name = "Remove filter Country: NL" }).ClickAsync();
        await Assertions.Expect(page.Locator(".chip.removable")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Sorting_by_ltv_puts_the_highest_first()
    {
        var page = await OpenAsync("/assets");
        await Rows(page).First.WaitForAsync();

        // The asset the API ranks first by LTV; waiting for it on screen means the re-sorted table has rendered.
        using var top = JsonDocument.Parse(await System.Api.GetStringAsync("/api/assets?sort=Ltv&pageSize=1"));
        var expectedFirst = top.RootElement.GetProperty("items")[0].GetProperty("serialNumber").GetString()!;

        await page.Locator("button.sort").Filter(new() { HasText = "LTV" }).ClickAsync();
        await Assertions.Expect(page.Locator("th[aria-sort='descending']")).ToContainTextAsync("LTV");
        await Assertions.Expect(Rows(page).First.Locator(".mono").First).ToHaveTextAsync(expectedFirst);

        var ltvs = (await page.Locator("tbody tr.clickable td:nth-child(6)").AllInnerTextsAsync())
            .Select(t => t.Trim()).Where(t => t.EndsWith('%')).Select(t => decimal.Parse(t.TrimEnd('%'), CultureInfo.InvariantCulture)).ToList();
        Assert.True(ltvs.Count >= 2);
        Assert.Equal(ltvs.OrderByDescending(x => x), ltvs);
    }

    [Fact]
    public async Task Selecting_an_asset_opens_its_detail_with_contract_and_valuation_history()
    {
        var page = await OpenAsync("/assets");
        await Rows(page).First.WaitForAsync();

        var row = Rows(page).Nth(2);
        var serial = (await row.Locator(".mono").First.InnerTextAsync()).Trim();
        await row.ClickAsync();

        var detail = page.Locator("aside.detail");
        await Assertions.Expect(detail).ToContainTextAsync(serial);
        await Assertions.Expect(detail.GetByText("Valuation history")).ToBeVisibleAsync();
        await Assertions.Expect(detail.Locator("dl.kv")).ToContainTextAsync("Contract");
        await ScreenshotAsync(page, "assets-detail-light");
    }

    [Fact]
    public async Task Csv_export_contains_exactly_the_filtered_assets()
    {
        var page = await OpenAsync("/assets");
        await Rows(page).First.WaitForAsync();
        await page.RunAndWaitForResponseAsync(
            () => page.GetByLabel("Status").Nth(0).SelectOptionAsync("Sold"),
            r => r.Url.Contains("/api/assets?") && r.Url.Contains("status=Sold"));
        await Assertions.Expect(page.Locator(".chip.removable").Filter(new() { HasText = "Status: Sold" })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".table-wrap")).Not.ToHaveClassAsync(new Regex("loading"));
        var total = await ShownTotalAsync(page);

        var download = await page.RunAndWaitForDownloadAsync(() =>
            page.GetByRole(AriaRole.Link, new() { Name = "Export CSV" }).ClickAsync());
        var file = Path.Combine(SystemFixture.ArtifactsDir, "downloads", download.SuggestedFilename);
        await download.SaveAsAsync(file);
        var lines = (await File.ReadAllLinesAsync(file)).Where(l => l.Length > 0).ToList();

        Assert.StartsWith("Serial,", lines[0]);
        Assert.Equal(total, lines.Count - 1);
        Assert.All(lines.Skip(1), l => Assert.Contains(",Sold,", l));
    }
}
