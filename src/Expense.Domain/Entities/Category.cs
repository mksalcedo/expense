namespace Expense.Domain.Entities;

public class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }

    // Not hard-deleted on removal - deactivated, to preserve historical transactions/reports
    public bool IsActive { get; set; } = true;
}
