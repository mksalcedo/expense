using Expense.Domain.Services.Dashboard;
using Expense.Domain.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Expense.Domain.Services.Scheduling;

/// <summary>
/// Runs SimpleFin then Amazon Gmail sync automatically at the configured times of day
/// (see AppSettings.ScheduledSyncTimesLocal), via the exact same ISyncStatusProvider
/// methods the manual Sync Now buttons call, so scheduled and manual runs behave
/// identically and share the same ImportRun tracking (and, since SyncStatusProvider itself
/// captures a ForecastSnapshot after each successful sync - see
/// docs/forecast-accuracy-plan.md - the same snapshot behavior too). A failed run is
/// surfaced entirely through the persisted ImportRun (read by the NavMenu's failure
/// indicator and the Dashboard banner) - no separate notification path. Like
/// SyncStatusProvider, this is composition-root glue and is deliberately not unit-tested -
/// there's no reasonable way to unit test a real background timer loop;
/// ScheduledSyncTimeCalculator underneath it is what's actually tested.
/// </summary>
public class SyncScheduler(IServiceScopeFactory scopeFactory, IOptions<AppSettings> options, ILogger<SyncScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dailyTimes = options.Value.ScheduledSyncTimesLocal.Select(TimeOnly.Parse).ToList();
                var next = ScheduledSyncTimeCalculator.GetNextRunTime(DateTimeOffset.Now, dailyTimes);
                var delay = next - DateTimeOffset.Now;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, stoppingToken);
                }

                await RunScheduledSyncAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad iteration must never permanently kill the whole scheduled-sync
                // loop for the rest of the app's lifetime.
                logger.LogError(ex, "Unexpected error in the sync scheduler loop - will retry at the next scheduled time.");
            }
        }
    }

    private async Task RunScheduledSyncAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var syncStatus = scope.ServiceProvider.GetRequiredService<ISyncStatusProvider>();

        // RunSimpleFinSyncAsync/RunAmazonGmailSyncAsync already catch broadly and record a
        // failed ImportRun themselves, so no per-source try/catch is needed here - only a
        // truly unexpected failure (e.g. a database connectivity issue) would ever reach
        // the outer loop's own catch above.
        await syncStatus.RunSimpleFinSyncAsync(cancellationToken);
        await syncStatus.RunAmazonGmailSyncAsync(cancellationToken: cancellationToken);
    }
}
