using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AssetDashboard.Infrastructure;

/// <summary>
/// The application's own traces and metrics (OpenTelemetry). Registered in ServiceDefaults under
/// <see cref="Name"/>; visible in the Aspire dashboard next to ASP.NET Core, SignalR and Npgsql telemetry.
/// </summary>
public static class AssetDashboardTelemetry
{
    public const string Name = "AssetDashboard";

    public static readonly ActivitySource ActivitySource = new(Name);
    private static readonly Meter Meter = new(Name);

    /// <summary>Time to recompute one portfolio snapshot, tagged with the filter key.</summary>
    public static readonly Histogram<double> SnapshotDuration =
        Meter.CreateHistogram<double>("assetdashboard.snapshot.duration", "ms", "Time to recompute a portfolio snapshot");

    public static readonly Counter<long> SnapshotsSent =
        Meter.CreateCounter<long>("assetdashboard.snapshots.sent", "{snapshot}", "Snapshots pushed to SignalR groups");

    public static readonly Counter<long> EventsPublished =
        Meter.CreateCounter<long>("assetdashboard.events.published", "{event}", "Portfolio events pushed to clients, by type");

    public static readonly Counter<long> SimulatorSteps =
        Meter.CreateCounter<long>("assetdashboard.simulator.steps", "{step}", "Simulated portfolio changes, by step");

    public static readonly Counter<long> SourceLoads =
        Meter.CreateCounter<long>("assetdashboard.source.loads", "{load}", "Source-system loads, by source and status");

    private static Func<int> _activeFilters = () => 0;

    /// <summary>Connects the "filters being watched" gauge to the broadcaster.</summary>
    public static void ObserveActiveFilters(Func<int> count) => _activeFilters = count;

    static AssetDashboardTelemetry() =>
        Meter.CreateObservableGauge("assetdashboard.filters.active", () => _activeFilters(), "{filter}",
            "Distinct filters with at least one viewer (one snapshot each)");
}
