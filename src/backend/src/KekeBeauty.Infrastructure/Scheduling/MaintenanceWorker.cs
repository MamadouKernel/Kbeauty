using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KekeBeauty.Infrastructure.Scheduling;

/// <summary>
/// A background job that runs <see cref="TickAsync"/> on a fixed interval. A failing tick is logged and retried on the
/// next one; <c>{configSection}:MaintenanceEnabled=false</c> switches the job off (a second instance, tests).
/// </summary>
public abstract class MaintenanceWorker(TimeProvider time, IConfiguration configuration, ILogger logger, string configSection, TimeSpan interval) : BackgroundService
{
    protected abstract Task TickAsync(CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue($"{configSection}:MaintenanceEnabled", true))
        {
            return;
        }

        using var timer = new PeriodicTimer(interval, time);
        try
        {
            do
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "{Job} failed; it will retry on the next tick", GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
