using Expense.Domain.Entities;

namespace Expense.Domain.Services.Budgets;

/// <summary>One row per budgeted, active category. Amount/Frequency/EffectiveFrom are null when no budget has been set yet.</summary>
public class BudgetRow
{
    public required int CategoryId { get; set; }
    public required string CategoryName { get; set; }
    public decimal? Amount { get; set; }
    public Frequency? Frequency { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public decimal? MonthlyEquivalent { get; set; }
}
