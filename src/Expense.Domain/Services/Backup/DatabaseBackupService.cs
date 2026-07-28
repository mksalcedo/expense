using System.Diagnostics;
using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Expense.Domain.Services.Backup;

public interface IDatabaseBackupService
{
    Task<BackupRun> RunAsync(CancellationToken cancellationToken = default);
    Task<BackupRun?> GetLastRunAsync(CancellationToken cancellationToken = default);
    Task<RecentBackupRunsPage> GetRecentRunsAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}

/// <summary>
/// Composition-root glue for backing up the database - reads the same ConnectionStrings:ExpenseDb
/// the app itself connects with (so the backup's username/database can never drift out of sync
/// with the real database), and runs pg_dump *inside* the expense_db container via `docker exec`
/// rather than the host's own pg_dump binary. This matters: the host has PostgreSQL 18 client
/// tools installed, but expense_db actually runs PostgreSQL 16 - a dump taken with the newer
/// host pg_dump embeds a "SET transaction_timeout = 0" line that a 16.x server rejects on
/// restore (confirmed for real via a restore-into-a-throwaway-database test on 2026-07-28).
/// Running pg_dump from inside the container guarantees its version always exactly matches the
/// server's, by construction, with no separate version-pinning to maintain. Prunes old dumps via
/// BackupRetentionCalculator and records the outcome as a BackupRun so a scheduled 1am run is
/// visible even if nobody's watching. Like SyncStatusProvider, this is deliberately not
/// unit-tested - there's no reasonable way to unit test a real docker/pg_dump subprocess;
/// BackupRetentionCalculator underneath it is what's tested.
/// </summary>
public class DatabaseBackupService(
    IDbContextFactory<ExpenseDbContext> contextFactory, IConfiguration configuration, IOptions<AppSettings> options) : IDatabaseBackupService
{
    public async Task<BackupRun> RunAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var connectionString = configuration.GetConnectionString("ExpenseDb");
        if (connectionString is null)
        {
            return await RecordFailureAsync("ConnectionStrings:ExpenseDb not set.", cancellationToken);
        }

        var settings = options.Value;
        var npgsql = new NpgsqlConnectionStringBuilder(connectionString);
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        string filePath;

        try
        {
            Directory.CreateDirectory(settings.DatabaseBackupDirectory);
            // Includes a time-of-day suffix so a manual run and the scheduled 1am run (or
            // repeated manual runs) landing on the same day each get their own file
            // instead of one silently overwriting the other's dump.
            filePath = Path.Combine(settings.DatabaseBackupDirectory, $"expense-{now:yyyy-MM-dd-HHmmss}.sql");
        }
        catch (Exception ex)
        {
            return await RecordFailureAsync($"Could not prepare backup directory: {ex.Message}", cancellationToken);
        }

        // No password needed - a local connection from inside the container is trusted by
        // expense_db's own pg_hba.conf, confirmed for real (docker exec ... pg_dump ... with
        // no PGPASSWORD set succeeds), which also avoids ever putting the password on a
        // command line visible via `ps`.
        var startInfo = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(settings.DatabaseContainerName);
        startInfo.ArgumentList.Add("pg_dump");
        startInfo.ArgumentList.Add("--username"); startInfo.ArgumentList.Add(npgsql.Username ?? "");
        startInfo.ArgumentList.Add("--dbname"); startInfo.ArgumentList.Add(npgsql.Database ?? "");

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            return await RecordFailureAsync($"Failed to start docker exec pg_dump: {ex.Message}", cancellationToken);
        }

        if (process is null)
        {
            return await RecordFailureAsync("Failed to start docker exec pg_dump.", cancellationToken);
        }

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            await process.StandardOutput.BaseStream.CopyToAsync(fileStream, cancellationToken);
        }
        catch (Exception ex)
        {
            TryDeletePartialFile(filePath);
            return await RecordFailureAsync($"Failed to write backup file: {ex.Message}", cancellationToken);
        }

        await process.WaitForExitAsync(cancellationToken);
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            TryDeletePartialFile(filePath);
            return await RecordFailureAsync($"docker exec pg_dump exited with code {process.ExitCode}: {stderr}", cancellationToken);
        }

        try
        {
            var fileSize = new FileInfo(filePath).Length;
            PruneOldBackups(settings.DatabaseBackupDirectory, today, settings.DatabaseBackupRetentionDays);

            var run = new BackupRun
            {
                RanAt = DateTimeOffset.UtcNow,
                Success = true,
                FilePath = filePath,
                FileSizeBytes = fileSize,
                Duration = stopwatch.Elapsed
            };
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            context.BackupRuns.Add(run);
            await context.SaveChangesAsync(cancellationToken);
            return run;
        }
        catch (Exception ex)
        {
            // pg_dump itself succeeded here - a failure at this point (pruning, saving the
            // run) must still surface as a failure so it isn't silently lost, even though
            // the dump file on disk is actually fine.
            return await RecordFailureAsync($"Backup file was written but recording it failed: {ex.Message}", cancellationToken);
        }
    }

    public async Task<BackupRun?> GetLastRunAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.BackupRuns.OrderByDescending(r => r.RanAt).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<RecentBackupRunsPage> GetRecentRunsAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.BackupRuns.AsQueryable();
        var totalCount = await query.CountAsync(cancellationToken);
        var runs = await query
            .OrderByDescending(r => r.RanAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new RecentBackupRunsPage { Runs = runs, TotalCount = totalCount };
    }

    private static void TryDeletePartialFile(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch
        {
            // Best-effort cleanup only - the failure is already being recorded and
            // reported by the caller regardless of whether this succeeds.
        }
    }

    private static void PruneOldBackups(string directory, DateOnly today, int retentionDays)
    {
        var fileNames = Directory.GetFiles(directory, "expense-*.sql").Select(Path.GetFileName).OfType<string>();
        var toDelete = BackupRetentionCalculator.SelectFilesToDelete(fileNames, today, retentionDays);
        foreach (var fileName in toDelete)
        {
            File.Delete(Path.Combine(directory, fileName));
        }
    }

    private async Task<BackupRun> RecordFailureAsync(string errorMessage, CancellationToken cancellationToken)
    {
        var run = new BackupRun { RanAt = DateTimeOffset.UtcNow, Success = false, ErrorMessage = errorMessage };
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.BackupRuns.Add(run);
        await context.SaveChangesAsync(cancellationToken);
        return run;
    }
}
