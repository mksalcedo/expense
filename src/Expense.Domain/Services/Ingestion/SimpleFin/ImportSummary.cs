namespace Expense.Domain.Services.Ingestion.SimpleFin;

public class ImportSummary
{
    public int TransactionsAdded { get; set; }
    public int DuplicatesSkipped { get; set; }
    public int BalanceSnapshotsAdded { get; set; }
    public List<string> UnmappedAccounts { get; } = [];
}
