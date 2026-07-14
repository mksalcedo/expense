namespace Expense.Domain.Entities;

public class DebtBalanceSnapshot
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public DateOnly AsOfDate { get; set; }
    public decimal Balance { get; set; }
}
