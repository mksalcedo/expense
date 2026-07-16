namespace Expense.Domain.Entities;

/// <summary>One completed attempt to sync a data source (SimpleFin or Amazon Gmail), so the Dashboard can show when each last ran.</summary>
public class ImportRun
{
    public int Id { get; set; }
    public ImportSource Source { get; set; }
    public DateTimeOffset RanAt { get; set; }
    public bool Success { get; set; }
    public string? Summary { get; set; }
    public string? ErrorMessage { get; set; }
}
