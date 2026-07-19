using Expense.Domain.Entities;

namespace Expense.Domain.Services.Forecast;

public class ForecastLedgerRow
{
    public required DateOnly Date { get; set; }
    public required string Description { get; set; }
    public required decimal Amount { get; set; }
    public required decimal RunningBalance { get; set; }
    public int AccountId { get; set; }
    public DateOnly OriginalDate { get; set; }
    public bool IsDeferred { get; set; }
    public int? DeferralId { get; set; }
}

/// <summary>A still-active PaymentConfirmation, for the Forecast page's manual-exclusions undo list.</summary>
public class ConfirmedPayment
{
    public required int ConfirmationId { get; set; }
    public required int AccountId { get; set; }
    public required string AccountName { get; set; }
    public required DateOnly OriginalDate { get; set; }
    public required ConfirmationReason Reason { get; set; }
}

public class ForecastResult
{
    public required decimal StartingBalance { get; set; }
    public required List<ForecastLedgerRow> Rows { get; set; }
    public List<ConfirmedPayment> Confirmations { get; set; } = [];

    public decimal LowestProjectedBalance =>
        Rows.Count == 0 ? StartingBalance : Rows.Min(r => r.RunningBalance);

    public DateOnly? LowestProjectedBalanceDate
    {
        get
        {
            if (Rows.Count == 0) return null;
            var lowest = Rows.Min(r => r.RunningBalance);
            return Rows.First(r => r.RunningBalance == lowest).Date;
        }
    }
}
