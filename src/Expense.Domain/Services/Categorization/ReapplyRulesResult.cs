namespace Expense.Domain.Services.Categorization;

/// <summary>How many previously-stuck pending rows got categorized by re-checking current merchant_rules/products.</summary>
public class ReapplyRulesResult
{
    public int TransactionsUpdated { get; set; }
    public int ItemsUpdated { get; set; }
}
