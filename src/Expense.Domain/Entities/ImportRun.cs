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
    public List<ImportRunProgressLine> ProgressLines { get; set; } = [];

    /// <summary>The raw, unparsed response received for this run (currently only
    /// populated for Plaid - the exact plaid-cli stdout) - kept alongside the parsed
    /// summary/progress lines so a gap between what's shown and what was actually
    /// received can be checked without re-fetching from the source.</summary>
    public string? RawResponse { get; set; }
}
