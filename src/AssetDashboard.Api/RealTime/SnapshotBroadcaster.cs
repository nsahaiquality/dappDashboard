using AssetDashboard.Infrastructure.Dashboard;
using Microsoft.AspNetCore.SignalR;

namespace AssetDashboard.Api.RealTime;

/// <summary>
/// Recomputes the portfolio snapshot when something changed and pushes it to all clients.
/// Changes are coalesced: however many events arrive, the (expensive) aggregation runs at most
/// once per <see cref="RefreshInterval"/>.
/// </summary>
public sealed class SnapshotBroadcaster(
    IServiceScopeFactory scopeFactory,
    IHubContext<DashboardHub, IDashboardClient> hub,
    ILogger<SnapshotBroadcaster> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);
    private int _dirty = 1;

    public PortfolioSnapshot? Latest { get; private set; }

    public void MarkDirty() => Interlocked.Exchange(ref _dirty, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        do
        {
            var stale = Latest is null || DateTime.UtcNow - Latest.GeneratedAt > MaxAge;
            if (Interlocked.Exchange(ref _dirty, 0) == 0 && !stale) continue;

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var stats = scope.ServiceProvider.GetRequiredService<PortfolioStatsService>();
                Latest = await stats.GetSnapshotAsync(stoppingToken);
                await hub.Clients.All.Snapshot(Latest);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Snapshot refresh failed");
                MarkDirty();
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
