using Expense.Domain.Services.Backup;
using Expense.Domain.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Expense.Domain.Services.Scheduling;

/// <summary>
/// Runs a database backup automatically at the configured time of day (see
/// AppSettings.ScheduledBackupTimesLocal, default 1am - ahead of the existing host-level
/// backup-to-server.sh cron job at 2am, so each night's dump is already on disk when that
/// job mirrors ~/dev to the network share). Uses the same ScheduledSyncTimeCalculator as
/// SyncScheduler. A failed run is recorded as a BackupRun and surfaced on the Backup Data
/// page - no separate notification path. Like SyncScheduler, this is composition-root glue
/// and is deliberately not unit-tested.
/// </summary>
public class BackupScheduler(IServiceScopeFactory scopeFactory, IOptions<AppSettings> options, ILogger<BackupScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dailyTimes = options.Value.ScheduledBackupTimesLocal.Select(TimeOnly.Parse).ToList();
                var next = ScheduledSyncTimeCalculator.GetNextRunTime(DateTimeOffset.Now, dailyTimes);
                var delay = next - DateTimeOffset.Now;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, stoppingToken);
                }

                using var scope = scopeFactory.CreateScope();
                var backupService = scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>();
                await backupService.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad iteration must never permanently kill the whole scheduled-backup
                // loop for the rest of the app's lifetime.
                logger.LogError(ex, "Unexpected error in the backup scheduler loop - will retry at the next scheduled time.");
            }
        }
    }
}
