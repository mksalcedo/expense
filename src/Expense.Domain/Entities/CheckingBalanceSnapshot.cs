namespace Expense.Domain.Entities;

/// <summary>
/// No account reference by design - there is exactly one checking account in this
/// system (Wells Fargo Checking), and the forecast always starts from the latest row here.
/// </summary>
public class CheckingBalanceSnapshot
{
    public int Id { get; set; }
    public DateOnly AsOfDate { get; set; }
    public decimal Balance { get; set; }
}
