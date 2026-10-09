using System.Collections.Concurrent;
using AssetDashboard.Infrastructure.Dashboard;
using Microsoft.AspNetCore.SignalR;

namespace AssetDashboard.Api.RealTime;

/// <summary>
/// Keeps one portfolio snapshot per filter that at least one client is watching, recomputes them
/// when something changed and pushes each to the SignalR group of that filter.
/// Changes are coalesced: however many events arrive, each snapshot is recomputed at most once per
/// <see cref="RefreshInterval"/>.
/// </summary>
public sealed class SnapshotBroadcaster(
    IServiceScopeFactory scopeFactory,
    IHubContext<DashboardHub, IDashboardClient> hub,
    ILogger<SnapshotBroadcaster> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

    private sealed class Entry(PortfolioFilter filter)
    {
        public PortfolioFilter Filter { get; } = filter;
        public int Watchers;
        public PortfolioSnapshot? Latest;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private int _dirty = 1;

    /// <summary>Latest snapshot of the whole (unfiltered) portfolio.</summary>
    public PortfolioSnapshot? Latest => Get(PortfolioFilter.None);

    public PortfolioSnapshot? Get(PortfolioFilter filter) =>
        _entries.TryGetValue(filter.Normalize().Key, out var e) ? e.Latest : null;

    public void MarkDirty() => Interlocked.Exchange(ref _dirty, 1);

    public void Watch(PortfolioFilter filter)
    {
        filter = filter.Normalize();
        var entry = _entries.GetOrAdd(filter.Key, _ => new Entry(filter));
        Interlocked.Increment(ref entry.Watchers);
    }

    public void Unwatch(PortfolioFilter filter)
    {
        if (_entries.TryGetValue(filter.Normalize().Key, out var entry))
            Interlocked.Decrement(ref entry.Watchers);
    }

    /// <summary>Stores a snapshot computed outside the loop (e.g. on filter change) so others can reuse it.</summary>
    public void Remember(PortfolioSnapshot snapshot)
    {
        if (_entries.TryGetValue(snapshot.Filter.Key, out var entry)) entry.Latest = snapshot;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The unfiltered snapshot is always kept warm for REST callers and new connections.
        _entries.GetOrAdd(PortfolioFilter.None.Key, _ => new Entry(PortfolioFilter.None)).Watchers++;

        using var timer = new PeriodicTimer(RefreshInterval);
        do
        {
            var dirty = Interlocked.Exchange(ref _dirty, 0) == 1;
            foreach (var (key, entry) in _entries)
            {
                if (entry.Watchers <= 0)
                {
                    _entries.TryRemove(key, out _);
                    continue;
                }
                var stale = entry.Latest is null || DateTime.UtcNow - entry.Latest.GeneratedAt > MaxAge;
                if (!dirty && !stale) continue;

                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var stats = scope.ServiceProvider.GetRequiredService<PortfolioStatsService>();
                    entry.Latest = await stats.GetSnapshotAsync(entry.Filter, stoppingToken);
                    await hub.Clients.Group(key).Snapshot(entry.Latest);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Snapshot refresh failed for {Filter}", key);
                    MarkDirty();
                }
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
