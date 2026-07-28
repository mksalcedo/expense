using Expense.Domain.Services.Backup;

namespace Expense.Domain.Tests.Services.Backup;

public class BackupRetentionCalculatorTests
{
    [Fact]
    public void SelectFilesToDelete_ReturnsFiles_OlderThanTheRetentionWindow()
    {
        var files = new[] { "expense-2026-06-01.sql", "expense-2026-07-20.sql" };
        var today = new DateOnly(2026, 7, 28);

        var toDelete = BackupRetentionCalculator.SelectFilesToDelete(files, today, retentionDays: 30);

        Assert.Equal(["expense-2026-06-01.sql"], toDelete);
    }

    [Fact]
    public void SelectFilesToDelete_KeepsFiles_ExactlyAtTheRetentionBoundary()
    {
        var files = new[] { "expense-2026-06-28.sql" };
        var today = new DateOnly(2026, 7, 28);

        var toDelete = BackupRetentionCalculator.SelectFilesToDelete(files, today, retentionDays: 30);

        Assert.Empty(toDelete);
    }

    [Fact]
    public void SelectFilesToDelete_IgnoresFiles_NotMatchingTheBackupNamingConvention()
    {
        var files = new[] { "readme.txt", "expense-2026-01-01.sql.bak", "not-a-backup.sql" };
        var today = new DateOnly(2026, 7, 28);

        var toDelete = BackupRetentionCalculator.SelectFilesToDelete(files, today, retentionDays: 30);

        Assert.Empty(toDelete);
    }

    [Fact]
    public void SelectFilesToDelete_ReturnsEmpty_WhenNothingIsOldEnough()
    {
        var files = new[] { "expense-2026-07-27.sql", "expense-2026-07-28.sql" };
        var today = new DateOnly(2026, 7, 28);

        var toDelete = BackupRetentionCalculator.SelectFilesToDelete(files, today, retentionDays: 30);

        Assert.Empty(toDelete);
    }

    [Fact]
    public void SelectFilesToDelete_ParsesDateFromTimestampedFileNames()
    {
        // Current naming convention includes a time-of-day suffix so multiple same-day
        // runs (a manual click plus the scheduled 1am run, or repeated manual runs) each
        // get their own file instead of silently overwriting one another.
        var files = new[] { "expense-2026-06-01-013000.sql", "expense-2026-07-20-140530.sql" };
        var today = new DateOnly(2026, 7, 28);

        var toDelete = BackupRetentionCalculator.SelectFilesToDelete(files, today, retentionDays: 30);

        Assert.Equal(["expense-2026-06-01-013000.sql"], toDelete);
    }

    [Fact]
    public void SelectFilesToDelete_HandlesAMixOfOldDateOnlyAndNewTimestampedFileNames()
    {
        // Older real backups on disk were written before the timestamp suffix was added -
        // retention must keep pruning them correctly, not just newly-written files.
        var files = new[] { "expense-2026-06-01.sql", "expense-2026-07-20-140530.sql" };
        var today = new DateOnly(2026, 7, 28);

        var toDelete = BackupRetentionCalculator.SelectFilesToDelete(files, today, retentionDays: 30);

        Assert.Equal(["expense-2026-06-01.sql"], toDelete);
    }

    [Fact]
    public void SelectFilesToDelete_MultipleSameDayTimestampedFiles_AreEachEvaluatedIndependently()
    {
        var files = new[] { "expense-2026-07-28-013000.sql", "expense-2026-07-28-142617.sql" };
        var today = new DateOnly(2026, 7, 28);

        var toDelete = BackupRetentionCalculator.SelectFilesToDelete(files, today, retentionDays: 30);

        Assert.Empty(toDelete);
    }
}
