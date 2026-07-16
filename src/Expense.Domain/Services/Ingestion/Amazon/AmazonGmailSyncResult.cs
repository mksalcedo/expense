using Expense.Domain.Entities;

namespace Expense.Domain.Services.Ingestion.Amazon;

/// <summary>The full detail of one AmazonGmailSyncService run - the console importer prints every field; the Dashboard only needs Run.Success/Summary/RanAt.</summary>
public class AmazonGmailSyncResult
{
    public required ImportRun Run { get; set; }
    public int ItemsAdded { get; set; }
    public int DuplicatesSkipped { get; set; }
    public int RefundsApplied { get; set; }
    public List<string> UnmatchedRefunds { get; } = [];
    public List<string> ParseFailures { get; } = [];
}
