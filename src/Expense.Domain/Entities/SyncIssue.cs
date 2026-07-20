namespace Expense.Domain.Entities;

/// <summary>
/// One email a sync couldn't parse (e.g. an unrecognized Amazon order template) - unlike
/// a pending-categorization item, this never became a real row anywhere else. ReceivedDate
/// and Body are captured directly from the email so resolving it never requires going back
/// to Gmail - the point is to fix the missing data, not just acknowledge it's missing.
/// </summary>
public class SyncIssue
{
    public int Id { get; set; }
    public ImportSource Source { get; set; }
    public required string MessageId { get; set; }
    public required string Subject { get; set; }
    public required string Reason { get; set; }
    public DateOnly ReceivedDate { get; set; }

    /// <summary>Null when the message had no plain-text body at all - that's sometimes the reason it failed to parse in the first place.</summary>
    public string? Body { get; set; }

    public SyncIssueResolution Resolution { get; set; }

    /// <summary>Set only when Resolution is Resolved - the item the user manually entered to fill the gap.</summary>
    public int? ResolvedAmazonOrderItemId { get; set; }
    public AmazonOrderItem? ResolvedAmazonOrderItem { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
