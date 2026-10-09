using System.Globalization;
using System.Text;
using AssetDashboard.Api.RealTime;
using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Dashboard;
using AssetDashboard.Infrastructure.History;
using AssetDashboard.Infrastructure.Refinancing;
using AssetDashboard.Infrastructure.Remarketing;
using AssetDashboard.Infrastructure.Simulation;

namespace AssetDashboard.Api;

public static class Endpoints
{
    private const int MaxExportRows = 100_000;

    public static void MapDashboardApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/dashboard/snapshot", async ([AsParameters] FilterQuery q, SnapshotBroadcaster broadcaster,
                PortfolioStatsService stats, CancellationToken ct) =>
            broadcaster.Get(q.ToFilter()) ?? await stats.GetSnapshotAsync(q.ToFilter(), ct));

        api.MapGet("/dashboard/events", (SignalREventSink events) => events.Recent());

        api.MapGet("/history", (PortfolioHistoryService history, AssetClass? assetClass, int days = 365, CancellationToken ct = default) =>
            history.GetAsync(days, assetClass, ct));

        var refinancing = api.MapGroup("/refinancing");
        refinancing.MapGet("/summary", ([AsParameters] FilterQuery q, RefinancingService svc, CancellationToken ct) =>
            svc.GetSummaryAsync(q.ToFilter(), ct));
        refinancing.MapGet("/requests", ([AsParameters] FilterQuery q, RefinancingService svc, RefinancingStatus? status,
                int page = 1, int pageSize = 25, CancellationToken ct = default) =>
            svc.GetRequestsAsync(q.ToFilter(), status, page, pageSize, ct));
        refinancing.MapGet("/candidates", ([AsParameters] FilterQuery q, RefinancingService svc,
                int page = 1, int pageSize = 25, CancellationToken ct = default) =>
            svc.GetCandidatesAsync(q.ToFilter(), page, pageSize, ct));

        var remarketing = api.MapGroup("/remarketing");
        remarketing.MapGet("/summary", ([AsParameters] FilterQuery q, RemarketingService svc, CancellationToken ct) =>
            svc.GetSummaryAsync(q.ToFilter(), ct));
        remarketing.MapGet("/cases", ([AsParameters] FilterQuery q, RemarketingService svc, RemarketingStatus? status,
                RemarketingChannel? channel, int page = 1, int pageSize = 25, CancellationToken ct = default) =>
            svc.GetCasesAsync(q.ToFilter(), status, channel, page, pageSize, ct));
        remarketing.MapGet("/auctions", ([AsParameters] FilterQuery q, RemarketingService svc, AuctionStatus? status,
                int page = 1, int pageSize = 25, CancellationToken ct = default) =>
            svc.GetAuctionsAsync(q.ToFilter(), status, page, pageSize, ct));

        api.MapGet("/reference", (PortfolioStatsService stats, CancellationToken ct) => stats.GetReferenceDataAsync(ct));

        api.MapGet("/assets", ([AsParameters] FilterQuery q, PortfolioStatsService stats, AssetStatus? status, string? search,
                AssetSort sort = AssetSort.MarketValue, int page = 1, int pageSize = 25, CancellationToken ct = default) =>
            stats.GetAssetsAsync(q.ToFilter(), status, search, sort, page, pageSize, ct));

        api.MapGet("/assets/export", ([AsParameters] FilterQuery q, PortfolioStatsService stats, HttpContext http,
            AssetStatus? status, string? search, AssetSort sort = AssetSort.MarketValue) =>
        {
            http.Response.Headers.ContentDisposition = $"attachment; filename=\"assets-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv\"";
            return Results.Stream(async body =>
            {
                await using var writer = new StreamWriter(body, new UTF8Encoding(false));
                await writer.WriteLineAsync("Serial,Category,Manufacturer,Model,Year,AssetClass,Status,Condition,Country,City,MarketValue,ForcedSaleValue,ContractLtv,Contract,DaysPastDue,LastValuedAt");
                await foreach (var a in stats.StreamAssetsAsync(q.ToFilter(), status, search, sort, MaxExportRows))
                {
                    string[] cells =
                    [
                        a.SerialNumber, a.Category, a.Manufacturer, a.Model, a.YearOfManufacture.ToString(CultureInfo.InvariantCulture),
                        a.AssetClass.ToString(), a.Status.ToString(), a.Condition.ToString(), a.Country, a.City,
                        a.MarketValue.ToString(CultureInfo.InvariantCulture), a.ForcedSaleValue.ToString(CultureInfo.InvariantCulture),
                        a.ContractLtv?.ToString("0.####", CultureInfo.InvariantCulture) ?? "", a.ContractNumber,
                        a.DaysPastDue.ToString(CultureInfo.InvariantCulture), a.LastValuedAt.ToString("O", CultureInfo.InvariantCulture),
                    ];
                    await writer.WriteLineAsync(string.Join(',', cells.Select(Csv)));
                }
            }, "text/csv; charset=utf-8");
        });

        api.MapGet("/assets/{id:long}", async (long id, PortfolioStatsService stats, CancellationToken ct) =>
            await stats.GetAssetAsync(id, ct) is { } asset ? Results.Ok(asset) : Results.NotFound());

        var simulator = api.MapGroup("/simulator");
        simulator.MapGet("/", (MarketSimulator sim) => new { running = sim.IsRunning });
        simulator.MapPost("/pause", (MarketSimulator sim) => { sim.Pause(); return new { running = sim.IsRunning }; });
        simulator.MapPost("/resume", (MarketSimulator sim) => { sim.Resume(); return new { running = sim.IsRunning }; });
    }

    private static string Csv(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    /// <summary>Query-string form of <see cref="PortfolioFilter"/>.</summary>
    public sealed record FilterQuery(AssetClass? AssetClass, string? Country, int? VendorId, ProductType? ProductType, bool? UnderwaterOnly)
    {
        public PortfolioFilter ToFilter() =>
            new PortfolioFilter(AssetClass, Country, VendorId, ProductType, UnderwaterOnly ?? false).Normalize();
    }
}
