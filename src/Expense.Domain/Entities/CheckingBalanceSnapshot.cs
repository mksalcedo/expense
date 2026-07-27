namespace Expense.Domain.Entities;

/// <summary>
/// No account reference by design - there is exactly one checking account in this
/// system (Wells Fargo Checking), and the forecast always starts from the latest row here.
/// </summary>
public class CheckingBalanceSnapshot
{
    public int Id { get; set; }
    public DateOnly AsOfDate { get; set; }

    /// <summary>
    /// Real timestamp precision, not just the calendar day - lets the forecast pick the
    /// genuinely freshest snapshot when two sources (e.g. SimpleFin and a Plaid backup
    /// import) both report a balance for the same AsOfDate but at different real times.
    /// </summary>
    public DateTimeOffset AsOfTimestamp { get; set; }

    public decimal Balance { get; set; }
}
