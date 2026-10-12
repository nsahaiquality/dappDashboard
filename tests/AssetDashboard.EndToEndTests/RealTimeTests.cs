using System.Diagnostics;
using System.Globalization;
using Microsoft.Playwright;

namespace AssetDashboard.EndToEndTests;

/// <summary>Changes on the server reach the screen without a reload, within seconds.</summary>
[Collection(SystemCollection.Name)]
public class RealTimeTests(SystemFixture system) : E2ETest(system)
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task Live_changes_reach_the_screen_within_three_seconds()
    {
        var page = await OpenAsync();
        await WaitForDataAsync(page);
        var firstEvent = page.Locator("ol.events li").First;
        var before = await firstEvent.InnerTextAsync();
        var updatedBefore = await page.Locator(".live").InnerTextAsync();

        var clock = Stopwatch.StartNew();
        (await System.Api.PostAsync("/api/simulator/resume", null)).EnsureSuccessStatusCode();
        try
        {
            // A new event appears at the top of the feed...
            await Assertions.Expect(firstEvent).Not.ToHaveTextAsync(before, new() { Timeout = (float)Limit.TotalMilliseconds });
            var eventLatency = clock.Elapsed;
            // ...and the figures are recalculated and pushed (the "updated" time moves on).
            await Assertions.Expect(page.Locator(".live")).Not.ToHaveTextAsync(updatedBefore, new() { Timeout = (float)Limit.TotalMilliseconds });
            var snapshotLatency = clock.Elapsed;

            Assert.True(eventLatency < Limit, $"first event after {eventLatency.TotalMilliseconds:N0} ms");
            Assert.True(snapshotLatency < Limit, $"first snapshot after {snapshotLatency.TotalMilliseconds:N0} ms");
            await File.AppendAllTextAsync(Path.Combine(SystemFixture.ArtifactsDir, "latency.txt"),
                $"event: {eventLatency.TotalMilliseconds.ToString("N0", CultureInfo.InvariantCulture)} ms, snapshot: {snapshotLatency.TotalMilliseconds.ToString("N0", CultureInfo.InvariantCulture)} ms\n");
        }
        finally
        {
            (await System.Api.PostAsync("/api/simulator/pause", null)).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Pause_and_resume_buttons_control_the_live_feed()
    {
        var page = await OpenAsync();
        await WaitForDataAsync(page);

        await Assertions.Expect(page.Locator(".live")).ToContainTextAsync("Paused");
        await page.GetByRole(AriaRole.Button, new() { Name = "Resume feed" }).ClickAsync();
        await Assertions.Expect(page.Locator(".live")).ToContainTextAsync("Live");
        await page.GetByRole(AriaRole.Button, new() { Name = "Pause feed" }).ClickAsync();
        await Assertions.Expect(page.Locator(".live")).ToContainTextAsync("Paused");
    }
}
