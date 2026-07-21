namespace Expense.Domain.Services.Ingestion.ManualCharges;

/// <summary>
/// One row in the screenshot review table - editable before commit. Amount is already
/// converted to this app's signed convention (negative = charge), unlike ExtractedChargeRow's
/// always-positive-magnitude-plus-IsCredit shape.
/// </summary>
public class ManualChargeReviewRow
{
    public required DateOnly Date { get; set; }
    public required string Description { get; set; }
    public required decimal Amount { get; set; }

    /// <summary>True when this row matches something already in the system - excluded from Add All by default, but never hidden.</summary>
    public required bool IsDuplicate { get; set; }

    /// <summary>Names the specific existing record it matched, so the match can be sanity-checked rather than trusted blindly. Null when IsDuplicate is false.</summary>
    public string? DuplicateReason { get; set; }
}
