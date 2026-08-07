using Expense.Domain.Entities;

namespace Expense.Domain.Services.Accounts;

public class AccountRow
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public required AccountType Type { get; set; }
    public required bool IsActive { get; set; }
    public decimal? MinPayment { get; set; }
    public decimal? ExtraPayment { get; set; }
    public int? PaymentDueDay { get; set; }
    public int? StatementCloseDay { get; set; }
    public DateOnly? PaymentStartDate { get; set; }
    public decimal? Apr { get; set; }
    public decimal? LatestBalance { get; set; }
    public DateOnly? LatestBalanceAsOfDate { get; set; }
}
