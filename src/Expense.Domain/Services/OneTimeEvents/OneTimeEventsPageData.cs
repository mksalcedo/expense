using Expense.Domain.Services.Categories;

namespace Expense.Domain.Services.OneTimeEvents;

public class OneTimeEventsPageData
{
    public required List<OneTimeEventRow> Events { get; set; }
    public required List<AccountOption> Accounts { get; set; }
}
