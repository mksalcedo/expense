using System.Text.RegularExpressions;

namespace Expense.Domain.Services.Backup;

/// <summary>
/// Pure calculation of which backup files are old enough to prune, given the
/// "expense-YYYY-MM-DD-HHmmss.sql" naming convention DatabaseBackupService writes (the
/// time suffix lets more than one run in the same day - a manual click plus the scheduled
/// 1am run, for example - each keep their own file instead of overwriting one another) -
/// kept separate from its real file I/O so it can be unit tested without touching disk.
/// Also recognizes the older date-only "expense-YYYY-MM-DD.sql" convention still present
/// on disk from before the time suffix was added. Files that don't match either
/// convention are left alone rather than treated as candidates for deletion, so anything
/// dropped into the backup directory by hand is safe.
/// </summary>
public static class BackupRetentionCalculator
{
    private static readonly Regex FileNamePattern = new(@"^expense-(\d{4}-\d{2}-\d{2})(-\d{6})?\.sql$", RegexOptions.Compiled);

    public static List<string> SelectFilesToDelete(IEnumerable<string> fileNames, DateOnly today, int retentionDays)
    {
        var cutoff = today.AddDays(-retentionDays);
        var toDelete = new List<string>();

        foreach (var fileName in fileNames)
        {
            var match = FileNamePattern.Match(fileName);
            if (match.Success && DateOnly.TryParse(match.Groups[1].Value, out var fileDate) && fileDate < cutoff)
            {
                toDelete.Add(fileName);
            }
        }

        return toDelete;
    }
}
