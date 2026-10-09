using System.ComponentModel;
using System.Globalization;
using System.Text;
using AssetDashboard.Api.RealTime;
using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Dashboard;
using AssetDashboard.Infrastructure.History;
using AssetDashboard.Infrastructure.Refinancing;
using AssetDashboard.Infrastructure.Remarketing;
using AssetDashboard.Infrastructure.Simulation;
using AssetDashboard.Infrastructure.Sources;
using AssetDashboard.Infrastructure.Vendors;

namespace AssetDashboard.Api;

/// <summary>
/// HTTP API. Every endpoint carries a tag, summary and description; they appear on the Swagger page (/swagger).
/// Endpoints that accept the portfolio filter take the same query parameters (see <see cref="FilterQuery"/>).
/// </summary>
public static class Endpoints
{
    private const int MaxExportRows = 100_000;
    private const string FilterNote = " Accepts the portfolio filter (assetClass, country, vendorId, productType, underwaterOnly).";

    public static void MapDashboardApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        MapDashboard(api.MapGroup("/dashboard").WithTags("Dashboard"));
        MapAssets(api.MapGroup("/assets").WithTags("Assets"));
        MapHistory(api.MapGroup("/history").WithTags("History"));
        MapRefinancing(api.MapGroup("/refinancing").WithTags("Refinancing"));
        MapRemarketing(api.MapGroup("/remarketing").WithTags("Remarketing"));
        MapVendors(api.MapGroup("/vendors").WithTags("Vendors"));
        MapSources(api.MapGroup("/sources").WithTags("Data sources"));
        MapReference(api.MapGroup("/reference").WithTags("Reference data"));
        MapSimulator(api.MapGroup("/simulator").WithTags("Simulator"));
    }

    private static void MapDashboard(RouteGroupBuilder g)
    {
        g.MapGet("/snapshot", async ([AsParameters] FilterQuery q, SnapshotBroadcaster broadcaster, PortfolioStatsService stats,
                CancellationToken ct) => broadcaster.Get(q.ToFilter()) ?? await stats.GetSnapshotAsync(q.ToFilter(), ct))
            .WithSummary("Portfolio snapshot")
            .WithDescription("All headline figures and breakdowns for the overview: exposure, collateral, loan-to-value, arrears, " +
                             "forced-sale shortfall, by asset class, delinquency buckets, remarketing, top vendors and countries. " +
                             "The same object is pushed live over SignalR (/hubs/dashboard, message 'Snapshot')." + FilterNote);

        g.MapGet("/events", (SignalREventSink events) => events.Recent())
            .WithSummary("Recent live events")
            .WithDescription("The last 50 portfolio events (payments, defaults, revaluations, auctions, refinancing, data loads), " +
                             "oldest first. New events are pushed live over SignalR (message 'PortfolioEvent').");
    }

    private static void MapAssets(RouteGroupBuilder g)
    {
        g.MapGet("/", ([AsParameters] FilterQuery q, PortfolioStatsService stats,
                    [Description("Only assets in this status.")] AssetStatus? status,
                    [Description("Case-insensitive match on serial number, category or contract number.")] string? search,
                    [Description("Sort order, highest first.")] AssetSort sort = AssetSort.MarketValue,
                    [Description("Page number, starting at 1.")] int page = 1,
                    [Description("Rows per page, 1–200.")] int pageSize = 25,
                    CancellationToken ct = default) =>
                stats.GetAssetsAsync(q.ToFilter(), status, search, sort, page, pageSize, ct))
            .WithSummary("List assets")
            .WithDescription("Paged list of financed assets with current values, contract loan-to-value and days past due." + FilterNote);

        g.MapGet("/export", ([AsParameters] FilterQuery q, PortfolioStatsService stats, HttpContext http,
                [Description("Only assets in this status.")] AssetStatus? status,
                [Description("Case-insensitive match on serial number, category or contract number.")] string? search,
                [Description("Sort order, highest first.")] AssetSort sort = AssetSort.MarketValue) =>
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
            })
            .WithSummary("Export assets as CSV")
            .WithDescription($"Same query as 'List assets', streamed as a CSV download (up to {MaxExportRows:N0} rows)." + FilterNote)
            .Produces(StatusCodes.Status200OK, contentType: "text/csv");

        g.MapGet("/{id:long}", async ([Description("Asset id.")] long id, PortfolioStatsService stats, CancellationToken ct) =>
                await stats.GetAssetAsync(id, ct) is { } asset ? Results.Ok(asset) : Results.NotFound())
            .WithSummary("Asset detail")
            .WithDescription("One asset with its contract, customer, vendor and full valuation history; each valuation point carries " +
                             "the asset's share of the contract balance at that date, from the repayment schedule.")
            .Produces<AssetDetail>()
            .Produces(StatusCodes.Status404NotFound);
    }

    private static void MapHistory(RouteGroupBuilder g)
    {
        g.MapGet("/", (PortfolioHistoryService history,
                    [Description("Only this asset class; omit for the whole portfolio.")] AssetClass? assetClass,
                    [Description("Number of days back from today, 1–3650.")] int days = 365,
                    CancellationToken ct = default) =>
                history.GetAsync(days, assetClass, ct))
            .WithSummary("Daily portfolio history")
            .WithDescription("One point per day: exposure, collateral, forced sale value, loan-to-value, 30+ and 90+ days past due, " +
                             "net recoveries. Older points are a synthetic backfill (isBackfilled = true); today's point is live.");
    }

    private static void MapRefinancing(RouteGroupBuilder g)
    {
        g.MapGet("/summary", ([AsParameters] FilterQuery q, RefinancingService svc, CancellationToken ct) =>
                svc.GetSummaryAsync(q.ToFilter(), ct))
            .WithSummary("Refinancing summary")
            .WithDescription("Requests by status, 12-month approval rate, days to decision, pending amount, expected loss " +
                             "(PD × LGD × exposure) by asset class, the 8-quarter maturity wall with balloon amounts, and requests by reason." + FilterNote);

        g.MapGet("/requests", ([AsParameters] FilterQuery q, RefinancingService svc,
                    [Description("Only requests in this status.")] RefinancingStatus? status,
                    [Description("Page number, starting at 1.")] int page = 1,
                    [Description("Rows per page, 1–200.")] int pageSize = 25,
                    CancellationToken ct = default) =>
                svc.GetRequestsAsync(q.ToFilter(), status, page, pageSize, ct))
            .WithSummary("Refinancing requests")
            .WithDescription("Requests newest first, with the risk picture at request time and the credit decision." + FilterNote);

        g.MapGet("/candidates", ([AsParameters] FilterQuery q, RefinancingService svc,
                    [Description("Page number, starting at 1.")] int page = 1,
                    [Description("Rows per page, 1–200.")] int pageSize = 25,
                    CancellationToken ct = default) =>
                svc.GetCandidatesAsync(q.ToFilter(), page, pageSize, ct))
            .WithSummary("Refinancing candidates")
            .WithDescription("Open contracts that need a conversation: maturing within 6 months, balloon due, underwater or 30+ days " +
                             "past due, ranked by uncovered exposure, with probability of default, loss given default and expected loss." + FilterNote);
    }

    private static void MapRemarketing(RouteGroupBuilder g)
    {
        g.MapGet("/summary", ([AsParameters] FilterQuery q, RemarketingService svc, CancellationToken ct) =>
                svc.GetSummaryAsync(q.ToFilter(), ct))
            .WithSummary("Remarketing summary")
            .WithDescription("Repossessed and listed stock with ageing, recovery by channel and asset class (last 12 months), " +
                             "cost breakdown, and the next five auctions." + FilterNote);

        g.MapGet("/cases", ([AsParameters] FilterQuery q, RemarketingService svc,
                    [Description("Only cases in this status.")] RemarketingStatus? status,
                    [Description("Only cases sold or offered through this channel.")] RemarketingChannel? channel,
                    [Description("Page number, starting at 1.")] int page = 1,
                    [Description("Rows per page, 1–200.")] int pageSize = 25,
                    CancellationToken ct = default) =>
                svc.GetCasesAsync(q.ToFilter(), status, channel, page, pageSize, ct))
            .WithSummary("Recovery cases")
            .WithDescription("Repossessed assets: unsold stock oldest first, then sales newest first, with days in stock, " +
                             "cost breakdown (storage accrues until sale) and recovery rate." + FilterNote);

        g.MapGet("/auctions", ([AsParameters] FilterQuery q, RemarketingService svc,
                    [Description("Scheduled (upcoming) or Completed.")] AuctionStatus? status,
                    [Description("Page number, starting at 1.")] int page = 1,
                    [Description("Rows per page, 1–200.")] int pageSize = 25,
                    CancellationToken ct = default) =>
                svc.GetAuctionsAsync(q.ToFilter(), status, page, pageSize, ct))
            .WithSummary("Auctions")
            .WithDescription("Live and online auctions with lots, lots sold and passed in, hammer total, average bids and " +
                             "sell-through. With a filter, only auctions holding matching lots." + FilterNote);
    }

    private static void MapVendors(RouteGroupBuilder g)
    {
        g.MapGet("/", ([AsParameters] FilterQuery q, VendorService svc,
                    [Description("Sort order, highest first.")] VendorSort sort = VendorSort.Exposure,
                    [Description("Page number, starting at 1.")] int page = 1,
                    [Description("Rows per page, 1–200.")] int pageSize = 25,
                    CancellationToken ct = default) =>
                svc.GetVendorsAsync(q.ToFilter(), sort, page, pageSize, ct))
            .WithSummary("Vendor performance")
            .WithDescription("Vendors (partner programmes) with programme terms, exposure, loan-to-value, default rate, recovery, " +
                             "12-month origination and attainment of the annual volume target." + FilterNote);

        g.MapGet("/{id:int}", async ([Description("Vendor id.")] int id, VendorService svc, CancellationToken ct) =>
                await svc.GetVendorAsync(id, ct) is { } vendor ? Results.Ok(vendor) : Results.NotFound())
            .WithSummary("Vendor detail")
            .WithDescription("One vendor: performance, 24 months of origination, mix by asset class and product, delinquency and top customers.")
            .Produces<VendorDetail>()
            .Produces(StatusCodes.Status404NotFound);
    }

    private static void MapSources(RouteGroupBuilder g)
    {
        g.MapGet("/", (SourceService svc, CancellationToken ct) => svc.GetSourcesAsync(ct))
            .WithSummary("Source systems")
            .WithDescription("Upstream systems feeding the dashboard, with last load, freshness (Fresh / Late / Stale against the " +
                             "expected interval), 7-day success rate, rows loaded in 24 hours and average load duration.");

        g.MapGet("/{id:int}/runs", async ([Description("Source system id.")] int id, SourceService svc,
                    [Description("Only runs with this outcome.")] LoadStatus? status,
                    [Description("Page number, starting at 1.")] int page = 1,
                    [Description("Rows per page, 1–200.")] int pageSize = 50,
                    CancellationToken ct = default) =>
                await svc.GetRunsAsync(id, status, page, pageSize, ct) is { } runs ? Results.Ok(runs) : Results.NotFound())
            .WithSummary("Load runs of a source")
            .WithDescription("Loads newest first, with duration, rows read and rejected, and the failure or warning message.")
            .Produces<PagedResult<LoadRunItem>>()
            .Produces(StatusCodes.Status404NotFound);
    }

    private static void MapReference(RouteGroupBuilder g)
    {
        g.MapGet("/", (PortfolioStatsService stats, CancellationToken ct) => stats.GetReferenceDataAsync(ct))
            .WithSummary("Reference data")
            .WithDescription("Vendors and countries for filter dropdowns.");
    }

    private static void MapSimulator(RouteGroupBuilder g)
    {
        g.MapGet("/", (MarketSimulator sim) => new SimulatorState(sim.IsRunning))
            .WithSummary("Simulator state")
            .WithDescription("Whether the market simulator (stand-in for the client's systems) is producing live changes.");
        g.MapPost("/pause", (MarketSimulator sim) => { sim.Pause(); return new SimulatorState(sim.IsRunning); })
            .WithSummary("Pause the simulator")
            .WithDescription("Stops live changes; the dashboard keeps showing the last figures.");
        g.MapPost("/resume", (MarketSimulator sim) => { sim.Resume(); return new SimulatorState(sim.IsRunning); })
            .WithSummary("Resume the simulator")
            .WithDescription("Restarts live changes.");
    }

    private static string Csv(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    /// <summary>Whether the market simulator is producing live changes.</summary>
    public sealed record SimulatorState(bool Running);

    /// <summary>Query-string form of <see cref="PortfolioFilter"/>; every parameter is optional.</summary>
    public sealed record FilterQuery(
        [property: Description("Only this asset class.")] AssetClass? AssetClass,
        [property: Description("Only this country (ISO code, e.g. NL).")] string? Country,
        [property: Description("Only this vendor (see /api/reference).")] int? VendorId,
        [property: Description("Only this financial product.")] ProductType? ProductType,
        [property: Description("Only contracts whose exposure exceeds the market value of their collateral.")] bool? UnderwaterOnly)
    {
        public PortfolioFilter ToFilter() =>
            new PortfolioFilter(AssetClass, Country, VendorId, ProductType, UnderwaterOnly ?? false).Normalize();
    }
}
