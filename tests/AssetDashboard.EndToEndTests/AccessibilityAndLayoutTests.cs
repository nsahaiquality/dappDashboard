using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace AssetDashboard.EndToEndTests;

/// <summary>WCAG 2.1 AA checks (axe) and responsive layout, in light and dark mode.</summary>
[Collection(SystemCollection.Name)]
public class AccessibilityAndLayoutTests(SystemFixture system) : E2ETest(system)
{
    public static TheoryData<string, ColorScheme> Pages => new()
    {
        { "/", ColorScheme.Light },
        { "/", ColorScheme.Dark },
        { "/assets", ColorScheme.Light },
        { "/assets", ColorScheme.Dark },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Page_has_no_serious_accessibility_violations(string path, ColorScheme scheme)
    {
        var page = await OpenAsync(path, colorScheme: scheme);
        await WaitForDataAsync(page, path == "/" ? ".kpi-grid .kpi" : "tbody tr.clickable");
        await page.WaitForTimeoutAsync(500); // let bars and charts finish their transitions

        var result = await page.RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] },
        });

        var serious = result.Violations.Where(v => v.Impact is "serious" or "critical").ToList();
        var report = string.Join("\n", result.Violations.Select(v =>
            $"[{v.Impact}] {v.Id}: {v.Help} ({v.Nodes.Length}x) e.g. {v.Nodes.First().Target}"));
        await File.AppendAllTextAsync(Path.Combine(SystemFixture.ArtifactsDir, "accessibility.txt"),
            $"{path} {scheme}: {result.Violations.Length} violations, {result.Passes.Length} rules passed\n{report}\n\n");
        Assert.True(serious.Count == 0, $"Serious accessibility violations on {path} ({scheme}):\n{report}");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/assets")]
    public async Task Phone_layout_never_scrolls_sideways(string path)
    {
        var page = await OpenAsync(path, width: 390, height: 844);
        await WaitForDataAsync(page, path == "/" ? ".kpi-grid .kpi" : "tbody tr.clickable");

        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.Equal(0, overflow);
        await ScreenshotAsync(page, $"mobile{path.Replace('/', '-').TrimEnd('-')}");
    }

    [Fact]
    public async Task Dark_mode_uses_dark_surfaces()
    {
        var page = await OpenAsync(colorScheme: ColorScheme.Dark);
        await WaitForDataAsync(page);

        var background = await page.EvaluateAsync<string>("() => getComputedStyle(document.body).backgroundColor");
        Assert.Equal("rgb(11, 15, 22)", background); // --page in dark mode (#0b0f16)
        await ScreenshotAsync(page, "overview-dark");
    }
}
