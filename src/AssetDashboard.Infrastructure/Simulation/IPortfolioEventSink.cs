using AssetDashboard.Infrastructure.Dashboard;

namespace AssetDashboard.Infrastructure.Simulation;

/// <summary>
/// Receives portfolio changes. The API implements this with SignalR; infrastructure code
/// stays unaware of the transport.
/// </summary>
public interface IPortfolioEventSink
{
    ValueTask PublishAsync(PortfolioEvent portfolioEvent, CancellationToken ct = default);
}
