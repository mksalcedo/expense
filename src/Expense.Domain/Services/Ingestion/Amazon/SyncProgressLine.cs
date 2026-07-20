namespace Expense.Domain.Services.Ingestion.Amazon;

/// <summary>One line (possibly multi-line text) of live progress from a running sync - see AmazonGmailSyncService.RunAsync.</summary>
public record SyncProgressLine(string Text, bool IsError = false);
