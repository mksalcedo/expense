namespace Expense.Domain.Entities;

public class OneTimeEvent
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public decimal Amount { get; set; }
    public Direction Direction { get; set; }
    public DateOnly Date { get; set; }

    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;
}
