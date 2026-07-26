using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Ingestion;
using Expense.Domain.Services.Ingestion.Plaid;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.Ingestion.Plaid;

public class PlaidTransactionImportServiceTests : DatabaseTestBase
{
    // Real shape captured directly from `plaid-cli transactions list --json` - two lines,
    // a diagnostic progress line followed by the real payload.
    private const string SampleCliOutput = """
    {"diagnostic":{"code":"FETCHING_TRANSACTIONS","end_date":"2026-07-25","level":"info","message":"Fetching transactions...","start_date":"2026-07-15"}}
    {"accounts":[{"account_id":"plaid-checking-1","name":"EVERYDAY CHECKING ...4103","mask":"4103","balances":{"available":5092.71,"current":5136.49}}],"total_transactions":2,"transactions":[{"transaction_id":"txn-1","account_id":"plaid-checking-1","amount":33.87,"date":"2026-07-25","name":"Chipotle Mexican Grill","merchant_name":"Chipotle Mexican Grill","pending":true},{"transaction_id":"txn-2","account_id":"plaid-checking-1","amount":-4492.86,"date":"2026-07-24","name":"OASISBATCH PAYROLL 260724 G1923022160 MARK SALCEDO","merchant_name":null,"pending":false}]}
    """;

    private static PlaidTransactionImportService CreateSut() => new(new DedupService(), new CategorizationService());

    private async Task<Account> CreateCheckingAccountAsync()
    {
        var account = new Account { Name = "Wells Fargo Checking", Type = AccountType.Checking };
        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();
        return account;
    }

    private static Dictionary<string, int> BuildAccountMap(Account account) => new() { ["plaid-checking-1"] = account.Id };

    [Fact]
    public async Task ImportAsync_ParsesThePayloadLine_IgnoringTheDiagnosticLine()
    {
        var account = await CreateCheckingAccountAsync();

        var summary = await CreateSut().ImportAsync(Context, SampleCliOutput, BuildAccountMap(account), new DateOnly(2026, 7, 25));

        Assert.Equal(2, summary.TransactionsAdded);
    }

    [Fact]
    public async Task ImportAsync_FlipsTheSignConvention_PositiveMeansMoneyOut()
    {
        var account = await CreateCheckingAccountAsync();

        await CreateSut().ImportAsync(Context, SampleCliOutput, BuildAccountMap(account), new DateOnly(2026, 7, 25));

        var chipotle = await Context.BankTransactions.SingleAsync(t => t.Description == "Chipotle Mexican Grill");
        Assert.Equal(-33.87m, chipotle.Amount);

        var payroll = await Context.BankTransactions.SingleAsync(t => t.Description.Contains("PAYROLL"));
        Assert.Equal(4492.86m, payroll.Amount);
    }

    [Fact]
    public async Task ImportAsync_PendingTransaction_HasNoPostedDate_ButStillHasATransactionDate()
    {
        var account = await CreateCheckingAccountAsync();

        await CreateSut().ImportAsync(Context, SampleCliOutput, BuildAccountMap(account), new DateOnly(2026, 7, 25));

        var chipotle = await Context.BankTransactions.SingleAsync(t => t.Description == "Chipotle Mexican Grill");
        Assert.Null(chipotle.PostedDate);
        Assert.Equal(new DateOnly(2026, 7, 25), chipotle.TransactionDate);
    }

    [Fact]
    public async Task ImportAsync_PostedTransaction_HasPostedDateSet()
    {
        var account = await CreateCheckingAccountAsync();

        await CreateSut().ImportAsync(Context, SampleCliOutput, BuildAccountMap(account), new DateOnly(2026, 7, 25));

        var payroll = await Context.BankTransactions.SingleAsync(t => t.Description.Contains("PAYROLL"));
        Assert.Equal(new DateOnly(2026, 7, 24), payroll.PostedDate);
    }

    [Fact]
    public async Task ImportAsync_SetsImportSourceToPlaid_AndPopulatesMerchantFromMerchantName()
    {
        var account = await CreateCheckingAccountAsync();

        await CreateSut().ImportAsync(Context, SampleCliOutput, BuildAccountMap(account), new DateOnly(2026, 7, 25));

        var chipotle = await Context.BankTransactions.SingleAsync(t => t.Description == "Chipotle Mexican Grill");
        Assert.Equal("Plaid", chipotle.ImportSource);
        Assert.Equal("Chipotle Mexican Grill", chipotle.Merchant);

        var payroll = await Context.BankTransactions.SingleAsync(t => t.Description.Contains("PAYROLL"));
        Assert.Null(payroll.Merchant);
    }

    [Fact]
    public async Task ImportAsync_ReimportingTheSamePayload_CreatesNoDuplicates()
    {
        var account = await CreateCheckingAccountAsync();
        var accountMap = BuildAccountMap(account);
        var sut = CreateSut();

        await sut.ImportAsync(Context, SampleCliOutput, accountMap, new DateOnly(2026, 7, 25));
        var summary = await sut.ImportAsync(Context, SampleCliOutput, accountMap, new DateOnly(2026, 7, 25));

        Assert.Equal(0, summary.TransactionsAdded);
        Assert.Equal(2, summary.DuplicatesSkipped);
        Assert.Equal(2, await Context.BankTransactions.CountAsync());
    }

    [Fact]
    public async Task ImportAsync_ATransactionAlreadyImportedFromAnotherSource_IsSkipped_EvenWithADifferentDescriptionAndId()
    {
        // Real scenario this guards: SimpleFin already imported this same real payroll
        // deposit under its own raw bank description and its own ExternalId - Plaid's
        // cleaned-up description/different transaction_id must not create a second row.
        var account = await CreateCheckingAccountAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 24), PostedDate = new DateOnly(2026, 7, 24),
            Description = "OASISBATCH PAYROLL 260724 G1923022160 MARK SALCEDO", Amount = 4492.86m,
            ImportSource = "SimpleFin", ExternalId = "simplefin-own-id-999", CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var summary = await CreateSut().ImportAsync(Context, SampleCliOutput, BuildAccountMap(account), new DateOnly(2026, 7, 25));

        Assert.Equal(1, summary.TransactionsAdded); // only Chipotle is genuinely new
        Assert.Equal(1, summary.DuplicatesSkipped); // the payroll deposit is recognized as already present
        Assert.Equal(1, await Context.BankTransactions.CountAsync(t => t.Description.Contains("PAYROLL")));
    }

    [Fact]
    public async Task ImportAsync_UnmappedAccount_IsSkippedAndReported_NotACrash()
    {
        var summary = await CreateSut().ImportAsync(Context, SampleCliOutput, new Dictionary<string, int>(), new DateOnly(2026, 7, 25));

        Assert.Equal(0, summary.TransactionsAdded);
        Assert.Contains("plaid-checking-1", summary.UnmappedAccounts);
    }

    [Fact]
    public async Task ImportAsync_NewTransaction_GetsCategorizedViaExistingMerchantRules()
    {
        var account = await CreateCheckingAccountAsync();
        var restaurants = new Category { Name = "Restaurants" };
        Context.Categories.Add(restaurants);
        await Context.SaveChangesAsync();
        Context.MerchantRules.Add(new MerchantRule { MerchantPattern = "%CHIPOTLE%", CategoryId = restaurants.Id });
        await Context.SaveChangesAsync();

        await CreateSut().ImportAsync(Context, SampleCliOutput, BuildAccountMap(account), new DateOnly(2026, 7, 25));

        var chipotle = await Context.BankTransactions.SingleAsync(t => t.Description == "Chipotle Mexican Grill");
        Assert.Equal(restaurants.Id, chipotle.CategoryId);
    }

    [Fact]
    public async Task ImportAsync_ChekingAccount_CreatesACheckingBalanceSnapshot_FromTheAvailableBalance()
    {
        // "available" confirmed directly against real data as the figure comparable to
        // what SimpleFin calls "balance" for this same account - not "current".
        var account = await CreateCheckingAccountAsync();

        var summary = await CreateSut().ImportAsync(Context, SampleCliOutput, BuildAccountMap(account), new DateOnly(2026, 7, 25));

        var snapshot = await Context.CheckingBalanceSnapshots.SingleAsync();
        Assert.Equal(new DateOnly(2026, 7, 25), snapshot.AsOfDate);
        Assert.Equal(5092.71m, snapshot.Balance);
        Assert.Equal(1, summary.BalanceSnapshotsAdded);
    }

    [Fact]
    public async Task ImportAsync_NonCheckingAccount_DoesNotCreateACheckingBalanceSnapshot()
    {
        var account = new Account { Name = "Amex", Type = AccountType.ActiveSpending };
        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();

        var summary = await CreateSut().ImportAsync(Context, SampleCliOutput, BuildAccountMap(account), new DateOnly(2026, 7, 25));

        Assert.Empty(await Context.CheckingBalanceSnapshots.ToListAsync());
        Assert.Equal(0, summary.BalanceSnapshotsAdded);
    }
}
