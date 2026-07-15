using Expense.Domain.Entities;

namespace Expense.Domain.Services.Categories;

public class CategoryRow
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public required bool IsActive { get; set; }
    public required string FundingStrategy { get; set; }

    // Current BudgetPeriod, if any - only meaningful for FundingStrategy == Direct or PayInFullAmex.
    public decimal? BudgetAmount { get; set; }
    public Frequency? BudgetFrequency { get; set; }
    public Direction? BudgetDirection { get; set; }
    public DateOnly? BudgetAnchor { get; set; }
    public int? BudgetAccountId { get; set; }
}
