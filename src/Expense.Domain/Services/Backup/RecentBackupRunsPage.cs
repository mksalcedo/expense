using Expense.Domain.Entities;

namespace Expense.Domain.Services.Backup;

/// <summary>One page of GetRecentRunsAsync results, plus the total count across every backup run (not just this page) so the UI can compute page count.</summary>
public class RecentBackupRunsPage
{
    public required List<BackupRun> Runs { get; set; }
    public required int TotalCount { get; set; }
}
