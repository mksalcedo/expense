using System.Text.RegularExpressions;
using Expense.Domain.Entities;

namespace Expense.Domain.Services.Ingestion.Amazon;

/// <summary>
/// The shared "does this order's department(s) resolve to exactly one of my categories?"
/// rule, used both at import time (AmazonImportService, from a freshly-parsed
/// AmazonOrderCategoryHint) and when a mapping is added later (DepartmentMappingService,
/// from the hint string already stored on the row). Auto-assigns only when every department
/// named is mapped AND they all point at one category - so "Supplements" + "Vitamins" -&gt;
/// Supplements is fine, but any unmapped department, or a genuine spread across categories,
/// leaves the row for review exactly as before.
/// </summary>
public static partial class AmazonDepartmentResolver
{
    // "3 Apparel" / "Supplements" - the count prefix is optional (a single-department hint is stored bare).
    [GeneratedRegex(@"^\s*(?:\d+\s+)?(?<dept>.+?)\s*$")]
    private static partial Regex HintPiece();

    /// <summary>Reverses AmazonOrderCategoryHint.ToDisplayString() into the department names it was built from.</summary>
    public static IReadOnlyList<string> ParseDepartmentNames(string? hintDisplayString)
    {
        if (string.IsNullOrWhiteSpace(hintDisplayString)) return [];
        return hintDisplayString.Split(',')
            .Select(piece => HintPiece().Match(piece).Groups["dept"].Value.Trim())
            .Where(d => d.Length > 0)
            .ToList();
    }

    /// <summary>The single category all the named departments map to, or null when any is unmapped or they span more than one.</summary>
    public static Category? Resolve(IEnumerable<string> departmentNames, IReadOnlyList<AmazonDepartmentMapping> mappings)
    {
        var names = departmentNames.ToList();
        if (names.Count == 0) return null;

        var resolved = names
            .Select(name => mappings.FirstOrDefault(m => string.Equals(m.DepartmentName, name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (resolved.Any(m => m is null)) return null;

        var categories = resolved.Select(m => m!.Category).DistinctBy(c => c.Id).ToList();
        return categories.Count == 1 ? categories[0] : null;
    }

    /// <summary>Applies a resolved category to a placeholder row: sets the category, clears NeedsReview, records how. The title is set separately from the department hint (see PlaceholderTitle).</summary>
    public static void ApplyResolvedCategory(AmazonOrderItem item, Category category, string hintDisplayString)
    {
        item.CategoryId = category.Id;
        item.NeedsReview = false;
        item.NeedsReviewReason = $"Category set automatically from email department: {hintDisplayString}";
    }

    /// <summary>The fixed generic title an item-list-free order confirmation imports with, before any department hint is available.</summary>
    public const string GenericPlaceholderTitle = "(Item details unavailable in email - check Amazon order page)";

    /// <summary>
    /// The best title we can give a placeholder row: the Amazon department it was in
    /// ("Amazon order — Printer Supplies") when the email's HTML told us one, else the plain
    /// generic string. Purely descriptive - never touches CategoryId. Overwritten by the real
    /// item name if the order page is later scraped.
    /// </summary>
    public static string PlaceholderTitle(string? departmentHint) =>
        string.IsNullOrWhiteSpace(departmentHint) ? GenericPlaceholderTitle : $"Amazon order — {departmentHint}";

    /// <summary>True when the title is still one this code generated (generic or "Amazon order — …"), i.e. safe to refresh - as opposed to a real title the user typed or a scrape filled in.</summary>
    public static bool IsSystemPlaceholderTitle(string? title) =>
        title == GenericPlaceholderTitle || (title?.StartsWith("Amazon order — ", StringComparison.Ordinal) ?? false);
}
