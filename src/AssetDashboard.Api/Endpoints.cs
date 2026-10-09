using AssetDashboard.Api.RealTime;
using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Dashboard;
using AssetDashboard.Infrastructure.Simulation;

namespace AssetDashboard.Api;

public static class Endpoints
{
    public static void MapDashboardApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/dashboard/snapshot", async (SnapshotBroadcaster broadcaster, PortfolioStatsService stats, CancellationToken ct) =>
            broadcaster.Latest ?? await stats.GetSnapshotAsync(ct));

        api.MapGet("/dashboard/events", (SignalREventSink events) => events.Recent());

        api.MapGet("/assets", (PortfolioStatsService stats, AssetClass? assetClass, AssetStatus? status, string? search,
                int page = 1, int pageSize = 25, CancellationToken ct = default) =>
            stats.GetAssetsAsync(assetClass, status, search, page, pageSize, ct));

        api.MapGet("/assets/{id:long}", async (long id, PortfolioStatsService stats, CancellationToken ct) =>
            await stats.GetAssetAsync(id, ct) is { } asset ? Results.Ok(asset) : Results.NotFound());

        var simulator = api.MapGroup("/simulator");
        simulator.MapGet("/", (MarketSimulator sim) => new { running = sim.IsRunning });
        simulator.MapPost("/pause", (MarketSimulator sim) => { sim.Pause(); return new { running = sim.IsRunning }; });
        simulator.MapPost("/resume", (MarketSimulator sim) => { sim.Resume(); return new { running = sim.IsRunning }; });
    }
}
