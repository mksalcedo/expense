using Expense.Domain.Entities;

namespace Expense.Domain.Services.Dashboard;

/// <summary>One page of GetRecentRunsAsync results, plus the total count across every run for that source (not just this page) so the UI can compute page count.</summary>
public class RecentRunsPage
{
    public required List<ImportRun> Runs { get; set; }
    public required int TotalCount { get; set; }
}
