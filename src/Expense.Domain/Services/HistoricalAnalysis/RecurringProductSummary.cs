namespace Expense.Domain.Services.HistoricalAnalysis;

public class RecurringProductSummary
{
    public required int ProductId { get; set; }
    public required string ProductPattern { get; set; }
    public required string CategoryName { get; set; }
    public required int Purchases { get; set; }
    public required decimal AveragePrice { get; set; }
    public required decimal TotalSpent { get; set; }
    public required DateOnly LastPurchased { get; set; }
}
