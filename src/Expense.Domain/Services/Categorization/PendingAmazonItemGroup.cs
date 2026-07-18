namespace Expense.Domain.Services.Categorization;

public class PendingAmazonItemGroup
{
    public required string SuggestedPattern { get; set; }
    public required string ItemTitle { get; set; }
    public required DateOnly SampleDate { get; set; }
    public required List<int> ItemIds { get; set; }
    public decimal TotalPrice { get; set; }

    /// <summary>True for a NeedsReview item's own singleton group - never true for a real multi-item group.</summary>
    public bool NeedsReview { get; set; }

    /// <summary>Only set for a singleton group (one real order) - a multi-item group spans more than one real order, so no single id applies.</summary>
    public string? OrderId { get; set; }
}
