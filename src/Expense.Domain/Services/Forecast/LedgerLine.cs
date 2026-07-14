namespace Expense.Domain.Services.Forecast;

/// <summary>
/// A single dated forecast-ledger row. Amount is signed: positive for income/credit,
/// negative for expense/debit - never persisted, always generated on demand.
/// </summary>
public class LedgerLine
{
    public required DateOnly Date { get; set; }
    public required string Description { get; set; }
    public required decimal Amount { get; set; }
    public int AccountId { get; set; }
}
