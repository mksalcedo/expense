namespace Expense.Domain.Services.Categorization;

public class PendingAmazonItemGroup
{
    public required string SuggestedPattern { get; set; }
    public required string ItemTitle { get; set; }
    public required DateOnly SampleDate { get; set; }
    public required List<int> ItemIds { get; set; }
    public decimal TotalPrice { get; set; }

    /// <summary>
    /// Only meaningful for a NeedsReview group - always 0 there until a human corrects the
    /// item (see ReviewQueue.razor), since the placeholder's TotalPrice is the order's whole
    /// tax-inclusive grand total with no split yet.
    /// </summary>
    public decimal TaxAllocated { get; set; }

    /// <summary>True for a NeedsReview item's own singleton group - never true for a real multi-item group.</summary>
    public bool NeedsReview { get; set; }

    /// <summary>Only set for a singleton group (one real order) - a multi-item group spans more than one real order, so no single id applies.</summary>
    public string? OrderId { get; set; }

    // Only ever set for a NeedsReview group - null for a real multi-item group, mirroring
    // AmazonOrderItem's own fields (see docs/amazon-needs-review-plan.md).
    public string? NeedsReviewReason { get; set; }
    public string? RawEmailBody { get; set; }
    public string? OrderDetailsUrl { get; set; }

    /// <summary>The "{count} {department}" hint from the order email's HTML body (see
    /// AmazonOrderCategoryHintParser), when one was parsed but didn't resolve to a single
    /// category (unmapped or genuinely mixed). Null otherwise.</summary>
    public string? DepartmentHint { get; set; }

    /// <summary>
    /// The single department name when DepartmentHint names exactly one - the thing a
    /// "remember this department" shortcut in the Review Queue would map. Null for a mixed
    /// hint (nothing single to remember) or no hint.
    /// </summary>
    public string? SingleDepartmentName { get; set; }
}
