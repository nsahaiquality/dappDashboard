using System.Runtime.CompilerServices;
using Microsoft.Playwright;

namespace AssetDashboard.EndToEndTests;

/// <summary>
/// Base for end-to-end tests: one browser context per page, with a Playwright trace saved for every
/// test (open with `pwsh playwright.ps1 show-trace <file>` or https://trace.playwright.dev).
/// </summary>
public abstract class E2ETest(SystemFixture system) : IAsyncLifetime
{
    protected SystemFixture System { get; } = system;
    private readonly List<(IBrowserContext Context, string Name)> _contexts = [];

    protected async Task<IPage> OpenAsync(string path = "/", int width = 1440, int height = 900, ColorScheme colorScheme = ColorScheme.Light,
        [CallerMemberName] string test = "")
    {
        var context = await System.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = colorScheme,
            AcceptDownloads = true,
            Locale = "en-IE",
        });
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Title = test });
        _contexts.Add((context, $"{GetType().Name}.{test}-{Guid.NewGuid().ToString("N")[..8]}"));

        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(System.WebUrl, path).ToString());
        return page;
    }

    /// <summary>Waits for the first snapshot (the overview's KPI cards, or the asset table).</summary>
    protected static Task WaitForDataAsync(IPage page, string selector = ".kpi-grid .kpi") =>
        page.Locator(selector).First.WaitForAsync(new() { Timeout = 30_000 });

    protected static async Task ScreenshotAsync(IPage page, string name) =>
        await page.ScreenshotAsync(new() { Path = Path.Combine(SystemFixture.ArtifactsDir, "screenshots", name + ".png"), FullPage = true });

    /// <summary>Formats a number exactly as the app does (en-IE, compact euros with 3 significant digits).</summary>
    protected static Task<string> FormatKpiEurAsync(IPage page, decimal value) =>
        page.EvaluateAsync<string>("v => new Intl.NumberFormat('en-IE', { style: 'currency', currency: 'EUR', notation: 'compact', " +
                                   "minimumSignificantDigits: 3, maximumSignificantDigits: 3 }).format(v)", (double)value);

    protected static Task<string> FormatPercentAsync(IPage page, decimal value) =>
        page.EvaluateAsync<string>("v => new Intl.NumberFormat('en-IE', { style: 'percent', maximumFractionDigits: 1 }).format(v)", (double)value);

    protected static Task<string> FormatIntAsync(IPage page, long value) =>
        page.EvaluateAsync<string>("v => new Intl.NumberFormat('en-IE').format(v)", value);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var (context, name) in _contexts)
        {
            await context.Tracing.StopAsync(new() { Path = Path.Combine(SystemFixture.ArtifactsDir, "traces", name + ".zip") });
            await context.DisposeAsync();
        }
    }
}
