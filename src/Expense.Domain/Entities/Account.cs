namespace Expense.Domain.Entities;

public class Account
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public AccountType Type { get; set; }

    // Debt-type only
    public decimal? MinPayment { get; set; }
    public decimal? ExtraPayment { get; set; }

    // Every debt account (day of month the payment happens)
    public int? PaymentDueDay { get; set; }

    // Amex only (day of month the statement closes, for cycle-qualification logic)
    public int? StatementCloseDay { get; set; }

    // Not hard-deleted on removal - deactivated, to preserve historical transactions/reports
    public bool IsActive { get; set; } = true;
}
