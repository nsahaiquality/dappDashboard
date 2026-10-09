using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.History;

namespace AssetDashboard.IntegrationTests;

[Collection(ApiCollection.Name)]
public class HistoryApiTests(ApiFixture api)
{
    [Fact]
    public async Task Total_history_covers_the_backfill_and_ends_with_a_live_row()
    {
        var points = await api.GetAsync<List<HistoryPoint>>("/api/history?days=365");

        Assert.NotNull(points);
        Assert.Equal(121, points.Count); // 120 backfilled days + today
        Assert.All(points[..^1], p => Assert.True(p.IsBackfilled));
        Assert.False(points[^1].IsBackfilled);
        Assert.Equal(points.Select(p => p.Date).OrderBy(d => d), points.Select(p => p.Date));
    }

    [Fact]
    public async Task Total_equals_the_sum_of_asset_classes_each_day()
    {
        var total = await api.GetAsync<List<HistoryPoint>>("/api/history?days=30");
        var byClass = new List<List<HistoryPoint>>();
        foreach (var cls in Enum.GetValues<AssetClass>())
            byClass.Add((await api.GetAsync<List<HistoryPoint>>($"/api/history?days=30&assetClass={cls}"))!);

        for (var i = 0; i < total!.Count; i++)
            Assert.Equal(total[i].Exposure, byClass.Sum(c => c[i].Exposure));
    }

    [Fact]
    public async Task Backfill_joins_up_with_today_and_grows_into_it()
    {
        var points = await api.GetAsync<List<HistoryPoint>>("/api/history?days=365&assetClass=CleanTech");
        var yesterday = points![^2];
        var today = points[^1];

        // Day-to-day change at the seam is small; the series starts lower than it ends (portfolio growth).
        Assert.InRange((double)(yesterday.Exposure / today.Exposure), 0.97, 1.03);
        Assert.True(points[0].Exposure < today.Exposure);
        Assert.All(points, p => Assert.InRange((double)p.LoanToValue, 0.3, 2.5));
    }
}
