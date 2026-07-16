namespace Expense.Domain.Services.HistoricalAnalysis;

public class CategoryAverageSummary
{
    public required int CategoryId { get; set; }
    public required string CategoryName { get; set; }
    public required decimal AverageActual { get; set; }

    /// <summary>The current weekly budget, not whatever was in effect during any of the averaged weeks - a rolling average is a "how am I trending against my plan right now" indicator.</summary>
    public decimal? CurrentBudget { get; set; }
}
