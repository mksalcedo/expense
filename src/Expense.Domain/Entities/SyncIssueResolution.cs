namespace Expense.Domain.Entities;

/// <summary>How a SyncIssue was, or hasn't yet been, dealt with.</summary>
public enum SyncIssueResolution
{
    /// <summary>Still needs review - the default/active state.</summary>
    None,

    /// <summary>The user entered the missing order details by hand; see SyncIssue.ResolvedAmazonOrderItemId.</summary>
    Resolved,

    /// <summary>The email wasn't actually an order at all (e.g. a gift-card notification) - nothing to enter.</summary>
    NotAnOrder
}
