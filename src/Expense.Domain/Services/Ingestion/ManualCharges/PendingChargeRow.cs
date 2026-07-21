namespace Expense.Domain.Services.Ingestion.ManualCharges;

public class PendingChargeRow
{
    public int Id { get; set; }
    public required string AccountName { get; set; }
    public DateOnly Date { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset EnteredAt { get; set; }
}
