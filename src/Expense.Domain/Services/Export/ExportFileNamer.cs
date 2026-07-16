namespace Expense.Domain.Services.Export;

/// <summary>
/// Tracks how many times a forecast has been exported on a given day (for the life of the
/// running process) so repeated exports get "-rev-2", "-rev-3", etc. instead of silently
/// overwriting/colliding in the browser's downloads folder.
/// </summary>
public class ExportFileNamer
{
    private readonly Dictionary<DateOnly, int> _exportCounts = new();

    public string GetNextFileName(DateOnly date)
    {
        var count = _exportCounts.GetValueOrDefault(date) + 1;
        _exportCounts[date] = count;

        var baseName = $"Forecast-{date:yyyy-MM-dd}";
        return count == 1 ? $"{baseName}.xlsx" : $"{baseName}-rev-{count}.xlsx";
    }
}
