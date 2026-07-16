namespace Expense.Domain.Services.Transactions;

public class TransactionRow
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
}
