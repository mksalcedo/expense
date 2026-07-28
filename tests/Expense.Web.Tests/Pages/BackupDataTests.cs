using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Backup;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class BackupDataTests : BunitContext
{
    private class FakeDatabaseBackupService(BackupRun? lastRun = null) : IDatabaseBackupService
    {
        public int RunCount { get; private set; }
        public BackupRun NextRunResult { get; set; } = new() { RanAt = DateTimeOffset.UtcNow, Success = true, FilePath = "/home/user/dev/expense/db_backups/expense-2026-07-28.sql", FileSizeBytes = 245_000, Duration = TimeSpan.FromSeconds(1.4) };
        public List<BackupRun> RecentRuns { get; set; } = [];

        public Task<BackupRun> RunAsync(CancellationToken cancellationToken = default)
        {
            RunCount++;
            return Task.FromResult(NextRunResult);
        }

        public Task<BackupRun?> GetLastRunAsync(CancellationToken cancellationToken = default) => Task.FromResult(lastRun);

        public Task<RecentBackupRunsPage> GetRecentRunsAsync(int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var pageOfRuns = RecentRuns.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(new RecentBackupRunsPage { Runs = pageOfRuns, TotalCount = RecentRuns.Count });
        }
    }

    private FakeDatabaseBackupService RegisterFakes(BackupRun? lastRun = null, List<BackupRun>? recentRuns = null)
    {
        var service = new FakeDatabaseBackupService(lastRun) { RecentRuns = recentRuns ?? [] };
        Services.AddSingleton<IDatabaseBackupService>(service);
        return service;
    }

    [Fact]
    public void WhenNeverBackedUp_ShowsNever()
    {
        RegisterFakes();

        var cut = Render<BackupData>();

        Assert.Contains("Last backup: never", cut.Find("#backup-status").TextContent);
    }

    [Fact]
    public void WhenLastBackupSucceeded_ShowsSizeAndDuration()
    {
        var lastRun = new BackupRun
        {
            RanAt = new DateTimeOffset(2026, 7, 28, 1, 0, 0, TimeSpan.Zero),
            Success = true,
            FilePath = "/home/user/dev/expense/db_backups/expense-2026-07-28.sql",
            FileSizeBytes = 245_000,
            Duration = TimeSpan.FromSeconds(1.4)
        };
        RegisterFakes(lastRun: lastRun);

        var cut = Render<BackupData>();

        var status = cut.Find("#backup-status").TextContent;
        Assert.Contains("239.3 KB", status);
        Assert.Contains("1.4s", status);
    }

    [Fact]
    public void WhenLastBackupFailed_ShowsTheErrorMessage()
    {
        var lastRun = new BackupRun { RanAt = DateTimeOffset.UtcNow, Success = false, ErrorMessage = "pg_dump exited with code 1: connection refused" };
        RegisterFakes(lastRun: lastRun);

        var cut = Render<BackupData>();

        Assert.Contains("FAILED: pg_dump exited with code 1: connection refused", cut.Find("#backup-status").TextContent);
    }

    [Fact]
    public void RunBackupButton_TriggersARun_AndRefreshesStatus()
    {
        var service = RegisterFakes();

        var cut = Render<BackupData>();
        cut.Find("#run-backup-btn").Click();

        Assert.Equal(1, service.RunCount);
        Assert.Contains("239.3 KB", cut.Find("#backup-status").TextContent);
    }

    [Fact]
    public void HistoryTable_IncludesSeconds_SoTwoRunsInTheSameMinuteAreDistinguishable()
    {
        var runs = new List<BackupRun>
        {
            new() { Id = 2, RanAt = new DateTimeOffset(2026, 7, 28, 14, 30, 36, TimeSpan.Zero), Success = true, FilePath = "/x/expense-2026-07-28-143036.sql", FileSizeBytes = 1000, Duration = TimeSpan.FromSeconds(1) },
            new() { Id = 1, RanAt = new DateTimeOffset(2026, 7, 28, 14, 30, 33, TimeSpan.Zero), Success = true, FilePath = "/x/expense-2026-07-28-143033.sql", FileSizeBytes = 1000, Duration = TimeSpan.FromSeconds(1) }
        };
        RegisterFakes(recentRuns: runs);

        var cut = Render<BackupData>();

        var rows = cut.FindAll("tbody tr");
        Assert.NotEqual(rows[0].TextContent, rows[1].TextContent);
        Assert.Contains(":36", rows[0].TextContent);
        Assert.Contains(":33", rows[1].TextContent);
    }

    [Fact]
    public void HistoryTable_ShowsRecentRuns_WithStatusAndDetails()
    {
        var runs = new List<BackupRun>
        {
            new() { Id = 2, RanAt = new DateTimeOffset(2026, 7, 28, 1, 0, 0, TimeSpan.Zero), Success = true, FilePath = "/x/expense-2026-07-28.sql", FileSizeBytes = 245_000, Duration = TimeSpan.FromSeconds(1.4) },
            new() { Id = 1, RanAt = new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero), Success = false, ErrorMessage = "disk full" }
        };
        RegisterFakes(recentRuns: runs);

        var cut = Render<BackupData>();

        var rows = cut.FindAll("tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Contains("OK", rows[0].TextContent);
        Assert.Contains("/x/expense-2026-07-28.sql", rows[0].TextContent);
        Assert.Contains("FAILED", rows[1].TextContent);
        Assert.Contains("disk full", rows[1].TextContent);
    }

    [Fact]
    public void HistoryTable_Paginates_WhenMoreThanOnePageOfRuns()
    {
        var runs = Enumerable.Range(1, 15)
            .Select(i => new BackupRun { Id = i, RanAt = DateTimeOffset.UtcNow.AddDays(-i), Success = true, FilePath = $"/x/expense-{i}.sql", FileSizeBytes = 1000, Duration = TimeSpan.FromSeconds(1) })
            .ToList();
        RegisterFakes(recentRuns: runs);

        var cut = Render<BackupData>();

        Assert.Equal(10, cut.FindAll("tbody tr").Count);
        Assert.Equal("Page 1 of 2", cut.Find("#backup-runs-page-indicator").TextContent);

        cut.Find("#backup-runs-next-page-btn").Click();

        Assert.Equal(5, cut.FindAll("tbody tr").Count);
        Assert.Equal("Page 2 of 2", cut.Find("#backup-runs-page-indicator").TextContent);
    }
}
