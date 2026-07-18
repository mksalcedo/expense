namespace Expense.Domain.Services.Transactions;

/// <summary>One page of GetTransactionsAsync results, plus the total count across every matching row (not just this page) so the UI can compute page count.</summary>
public class TransactionsPageResult
{
    public required List<TransactionRow> Items { get; set; }
    public required int TotalCount { get; set; }
}
