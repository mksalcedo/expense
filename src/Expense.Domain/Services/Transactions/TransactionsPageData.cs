using Expense.Domain.Entities;

namespace Expense.Domain.Services.Transactions;

public class TransactionsPageData
{
    public required List<TransactionRow> Transactions { get; set; }
    public required List<Category> Categories { get; set; }
    public required int TotalCount { get; set; }
}
