using System.Collections.Concurrent;
using AssetDashboard.Infrastructure.Dashboard;
using AssetDashboard.Infrastructure.Simulation;
using Microsoft.AspNetCore.SignalR;

namespace AssetDashboard.Api.RealTime;

/// <summary>Forwards each portfolio change to all clients and flags the snapshot for recalculation.</summary>
public sealed class SignalREventSink(IHubContext<DashboardHub, IDashboardClient> hub, SnapshotBroadcaster broadcaster)
    : IPortfolioEventSink
{
    private const int RecentCapacity = 50;
    private readonly ConcurrentQueue<PortfolioEvent> _recent = new();

    public async ValueTask PublishAsync(PortfolioEvent portfolioEvent, CancellationToken ct = default)
    {
        _recent.Enqueue(portfolioEvent);
        while (_recent.Count > RecentCapacity) _recent.TryDequeue(out _);

        broadcaster.MarkDirty();
        await hub.Clients.All.PortfolioEvent(portfolioEvent);
    }

    /// <summary>Most recent events, oldest first.</summary>
    public IReadOnlyList<PortfolioEvent> Recent() => _recent.ToArray();
}
