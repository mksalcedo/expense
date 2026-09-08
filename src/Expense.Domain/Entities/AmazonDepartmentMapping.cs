namespace Expense.Domain.Entities;

/// <summary>
/// Maps one Amazon "department" string (the browse-node name shown in a detail-less order
/// email, e.g. "Nutrition &amp; Wellness", "Grocery") to one of the user's categories. Lets
/// AmazonImportService auto-categorize an item-list-free order confirmation from the
/// department alone (see AmazonOrderCategoryHintParser), instead of every such order having
/// to be looked up by hand. User-owned data, seeded conservatively - only departments the
/// user has confirmed map unambiguously belong here; an unmapped department just defers the
/// order to the Review Queue exactly as before.
/// </summary>
public class AmazonDepartmentMapping
{
    public int Id { get; set; }

    /// <summary>Amazon's department string, stored as seen. Matched case-insensitively; unique on lower(department_name).</summary>
    public required string DepartmentName { get; set; }

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}
