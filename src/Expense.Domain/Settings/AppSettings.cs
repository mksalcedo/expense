namespace Expense.Domain.Settings;

/// <summary>Bound via IOptions&lt;AppSettings&gt; - never a hardcoded constant, per the user's explicit requirement to test with shorter horizons during development.</summary>
public class AppSettings
{
    public int ForecastHorizonMonths { get; set; } = 12;
    public List<string> ScheduledSyncTimesLocal { get; set; } = ["06:00", "15:00"];
    public List<string> ScheduledBackupTimesLocal { get; set; } = ["01:00"];
    public int DatabaseBackupRetentionDays { get; set; } = 30;
    public string DatabaseBackupDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "dev", "expense", "db_backups");
    public string DatabaseContainerName { get; set; } = "expense_db";

    /// <summary>Plaid is now the primary scheduled source (see SyncScheduler) - SimpleFin
    /// is kept disabled rather than removed, in case it's ever needed again. When false,
    /// the scheduler skips it and its section disappears from the Import Data page, but
    /// its own code/tests/history are untouched.</summary>
    public bool SimpleFinEnabled { get; set; } = true;
}
