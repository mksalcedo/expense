namespace Expense.Domain.Entities;

public class Account
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public AccountType Type { get; set; }

    // Debt-type only
    public decimal? MinPayment { get; set; }
    public decimal? ExtraPayment { get; set; }

    // Debt and ActiveSpending (e.g. Amex) - purely informational reference data, not read by the forecast.
    public decimal? Apr { get; set; }

    // Every debt account (day of month the payment happens)
    public int? PaymentDueDay { get; set; }

    // Debt-type only. Null means the payment schedule has always been active (the prior,
    // only behavior) - set this when adding an account whose real first payment is in the
    // future (e.g. a new consolidation loan), so the forecast doesn't synthesize a phantom
    // payment for a cycle that was never actually due. Without this, every debt account's
    // payment schedule was implicitly active "since the beginning of time," which is wrong
    // the moment a new loan's first payment is weeks away, not immediate (found while
    // planning a real debt-consolidation loan, 2026-08-07).
    public DateOnly? PaymentStartDate { get; set; }

    // Amex only (day of month the statement closes, for cycle-qualification logic)
    public int? StatementCloseDay { get; set; }

    // Not hard-deleted on removal - deactivated, to preserve historical transactions/reports
    public bool IsActive { get; set; } = true;
}
