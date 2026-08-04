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

    // Optional - lets a one-time event (e.g. a top-up alongside an existing recurring bill in
    // the same category) participate in reconciliation against real transactions the same way
    // a recurring occurrence does. Null is the common case (most one-time events don't map to
    // an existing budget category at all).
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
}
