using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetDashboard.Infrastructure.History;

public sealed class HistoryOptions
{
    /// <summary>Days of synthetic history to generate when the history table is empty.</summary>
    public int BackfillDays { get; set; } = 730;
    public int CaptureIntervalMinutes { get; set; } = 5;
}

/// <summary>Keeps today's history rows in step with the live portfolio.</summary>
public sealed class HistoryRecorder(IServiceScopeFactory scopeFactory, IOptions<HistoryOptions> options, ILogger<HistoryRecorder> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.Value.CaptureIntervalMinutes)));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PortfolioHistoryService>().CaptureTodayAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "History capture failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
