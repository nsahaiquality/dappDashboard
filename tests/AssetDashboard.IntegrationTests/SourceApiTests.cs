using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Dashboard;
using AssetDashboard.Infrastructure.Data;
using AssetDashboard.Infrastructure.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace AssetDashboard.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SourceApiTests(ApiFixture api)
{
    [Fact]
    public async Task Sources_report_freshness_success_rate_and_volumes()
    {
        var sources = await api.GetAsync<List<SourceStatus>>("/api/sources");

        Assert.NotNull(sources);
        Assert.Equal(SourceCatalog.Sources.Length, sources.Count);
        Assert.All(sources, s =>
        {
            Assert.NotNull(s.LastRunAt);
            Assert.InRange(s.SuccessRate7Days, 0.5m, 1m);
            Assert.True(s.Runs7Days > 0);
            Assert.True(s.AverageDurationSeconds7Days > 0);
        });
        var core = sources.Single(s => s.Kind == SourceKind.CoreLeasing);
        Assert.InRange(core.Runs7Days, 7 * 96 - 5, 7 * 96 + 5); // every 15 minutes
    }

    [Fact]
    public async Task Runs_are_paged_newest_first_and_filter_by_status()
    {
        var core = (await api.GetAsync<List<SourceStatus>>("/api/sources"))!.Single(s => s.Kind == SourceKind.CoreLeasing);
        var page = await api.GetAsync<PagedResult<LoadRunItem>>($"/api/sources/{core.Id}/runs?pageSize=20");
        var failed = await api.GetAsync<PagedResult<LoadRunItem>>($"/api/sources/{core.Id}/runs?status=Failed");

        Assert.Equal(20, page!.Items.Count);
        Assert.Equal(page.Items.Select(r => r.StartedAt).OrderByDescending(d => d), page.Items.Select(r => r.StartedAt));
        Assert.All(failed!.Items, r => { Assert.Equal(LoadStatus.Failed, r.Status); Assert.NotNull(r.Message); });
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await api.Client.GetAsync("/api/sources/999/runs")).StatusCode);
    }
}

[Collection(MutableApiCollection.Name)]
public class SourceFeedSimulatorTests(MutableApiFixture fixture)
{
    [Fact]
    public async Task Due_loads_run_once_per_source_and_not_again_until_their_interval_passes()
    {
        await using var scope = fixture.Api.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetDbContext>();
        var later = DateTime.UtcNow.AddDays(2); // every source is due
        var before = db.LoadRuns.Count();

        await SourceFeedSimulator.RunDueLoadsAsync(db, later, new Random(1));
        var afterFirst = db.LoadRuns.Count();
        await SourceFeedSimulator.RunDueLoadsAsync(db, later.AddMinutes(1), new Random(2));

        Assert.Equal(before + SourceCatalog.Sources.Length, afterFirst);
        Assert.Equal(afterFirst, db.LoadRuns.Count());
    }
}
