namespace Expense.Domain.Services.Ingestion.Amazon;

/// <summary>One Amazon "department" (browse-node name, e.g. "Nutrition &amp; Wellness") and how many items in the order belong to it.</summary>
public record AmazonDepartmentCount(string Department, int Count);

/// <summary>
/// The per-order category hint pulled from an order-confirmation email's HTML body (see
/// AmazonOrderCategoryHintParser). One entry per Order # in the message. Departments is
/// empty when the email carried no hint for that order (older templates) - callers treat
/// that identically to today's no-information case.
/// </summary>
public record AmazonOrderCategoryHint(string OrderId, IReadOnlyList<AmazonDepartmentCount> Departments)
{
    /// <summary>A compact human-readable form for storing on the row / showing in the Review Queue, e.g. "Supplements" or "3 Apparel, 1 Office".</summary>
    public string ToDisplayString() =>
        Departments.Count switch
        {
            0 => "",
            1 when Departments[0].Count == 1 => Departments[0].Department,
            _ => string.Join(", ", Departments.Select(d => $"{d.Count} {d.Department}"))
        };
}
