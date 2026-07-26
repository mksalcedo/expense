namespace Expense.Domain.Services.Forecast;

/// <summary>One transaction's ReconciledOccurrenceDate assignment, before/after - used both to report a dry run and to log what a real run actually did.</summary>
public class ReconciliationChange
{
    public required int TransactionId { get; set; }
    public required string Description { get; set; }
    public DateOnly? OldValue { get; set; }
    public DateOnly? NewValue { get; set; }
}
