namespace Expense.Domain.Services.HistoricalAnalysis;

public class PeriodSpendingSummary
{
    public required DateOnly PeriodStart { get; set; }
    public required DateOnly PeriodEnd { get; set; }
    public required int CategoryId { get; set; }
    public required string CategoryName { get; set; }

    /// <summary>Null if the category had no BudgetPeriod in effect at PeriodStart.</summary>
    public decimal? Budget { get; set; }
    public required decimal Actual { get; set; }
}
