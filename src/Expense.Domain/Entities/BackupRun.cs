namespace Expense.Domain.Entities;

/// <summary>One completed attempt to back up the database, so the app can show when backups last ran.</summary>
public class BackupRun
{
    public int Id { get; set; }
    public DateTimeOffset RanAt { get; set; }
    public bool Success { get; set; }
    public string? FilePath { get; set; }
    public long? FileSizeBytes { get; set; }
    public TimeSpan? Duration { get; set; }
    public string? ErrorMessage { get; set; }
}
