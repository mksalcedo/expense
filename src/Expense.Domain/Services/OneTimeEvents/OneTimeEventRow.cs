using Expense.Domain.Entities;

namespace Expense.Domain.Services.OneTimeEvents;

public class OneTimeEventRow
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public required decimal Amount { get; set; }
    public required Direction Direction { get; set; }
    public required DateOnly Date { get; set; }
    public required int AccountId { get; set; }
    public required string AccountName { get; set; }
}
