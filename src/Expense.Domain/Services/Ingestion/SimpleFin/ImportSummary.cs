using Expense.Domain.Entities;

namespace Expense.Domain.Services.Ingestion.SimpleFin;

public class ImportSummary
{
    public int TransactionsAdded { get; set; }
    public int DuplicatesSkipped { get; set; }
    public int BalanceSnapshotsAdded { get; set; }
    public List<string> UnmappedAccounts { get; } = [];

    /// <summary>Every real transaction actually added this run - lets the caller check them
    /// against any open manually-entered placeholders, see ManualChargeMatchingService.</summary>
    public List<BankTransaction> NewTransactions { get; } = [];
}
