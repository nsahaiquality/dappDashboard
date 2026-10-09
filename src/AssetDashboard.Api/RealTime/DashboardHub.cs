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
/// Real-time channel for the dashboard. Clients only listen; all data flows server → client.
/// New connections immediately receive the latest snapshot and recent events.
/// </summary>
public sealed class DashboardHub(SnapshotBroadcaster broadcaster, SignalREventSink events) : Hub<IDashboardClient>
{
    public override async Task OnConnectedAsync()
    {
        if (broadcaster.Latest is { } snapshot)
            await Clients.Caller.Snapshot(snapshot);
        foreach (var evt in events.Recent())
            await Clients.Caller.PortfolioEvent(evt);
        await base.OnConnectedAsync();
    }
}
