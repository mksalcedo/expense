namespace Expense.Domain.Services.Categories;

public class CategoryRow
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public required bool IsBudgeted { get; set; }
    public required bool IsActive { get; set; }
    public required string FundingStrategy { get; set; }
}
