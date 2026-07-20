namespace Expense.Domain.Entities;

/// <summary>
/// One email a sync couldn't parse (e.g. an unrecognized Amazon order template) - unlike
/// a pending-categorization item, this never became a real row anywhere else, so it needs
/// its own durable, dismissable record or it's only ever visible as an aggregate count on
/// ImportRun.Summary for the run that happened to hit it.
/// </summary>
public class SyncIssue
{
    public int Id { get; set; }
    public ImportSource Source { get; set; }
    public required string MessageId { get; set; }
    public required string Subject { get; set; }
    public required string Reason { get; set; }

    /// <summary>Same meaning as BankTransaction.Dismissed - "review later", not "ignore forever".</summary>
    public bool Dismissed { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
