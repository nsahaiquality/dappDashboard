using AssetDashboard.Infrastructure.Dashboard;
using Microsoft.AspNetCore.SignalR;

namespace AssetDashboard.Api.RealTime;

/// <summary>Server → client messages. Method names are what the Angular client subscribes to.</summary>
public interface IDashboardClient
{
    Task Snapshot(PortfolioSnapshot snapshot);
    Task PortfolioEvent(PortfolioEvent portfolioEvent);
}

/// <summary>
/// Real-time channel for the dashboard. Each connection watches one <see cref="PortfolioFilter"/>
/// (initially the whole portfolio) and is a member of that filter's group, so it receives only the
/// snapshot matching its filter. Events go to everyone; clients narrow them locally.
/// </summary>
public sealed class DashboardHub(SnapshotBroadcaster broadcaster, SignalREventSink events, PortfolioStatsService stats)
    : Hub<IDashboardClient>
{
    private const string FilterKey = "filter";

    public override async Task OnConnectedAsync()
    {
        await JoinAsync(PortfolioFilter.None);
        if (broadcaster.Latest is { } snapshot)
            await Clients.Caller.Snapshot(snapshot);
        foreach (var evt in events.Recent())
            await Clients.Caller.PortfolioEvent(evt);
        await base.OnConnectedAsync();
    }

    /// <summary>Switches this connection to another filter and immediately sends its snapshot.</summary>
    public async Task SetFilter(PortfolioFilter filter)
    {
        filter = filter.Normalize();
        if (Context.Items[FilterKey] is PortfolioFilter current)
        {
            if (current.Key == filter.Key) return;
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, current.Key);
            broadcaster.Unwatch(current);
        }
        await JoinAsync(filter);

        var snapshot = broadcaster.Get(filter);
        if (snapshot is null)
        {
            snapshot = await stats.GetSnapshotAsync(filter, Context.ConnectionAborted);
            broadcaster.Remember(snapshot);
        }
        await Clients.Caller.Snapshot(snapshot);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items[FilterKey] is PortfolioFilter current)
            broadcaster.Unwatch(current);
        return base.OnDisconnectedAsync(exception);
    }

    private async Task JoinAsync(PortfolioFilter filter)
    {
        Context.Items[FilterKey] = filter;
        broadcaster.Watch(filter);
        await Groups.AddToGroupAsync(Context.ConnectionId, filter.Key);
    }
}
