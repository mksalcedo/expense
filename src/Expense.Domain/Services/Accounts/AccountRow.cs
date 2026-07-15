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
}
