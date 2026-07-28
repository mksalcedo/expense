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

    // Real shape of `plaid-cli transactions list --all --json` once more than one item is
    // linked - captured directly from real output, not guessed. No top-level "accounts"/
    // "transactions" at all - each linked item nests its own, inside a top-level "items"
    // array. Trimmed to one transaction per item for readability.
    private const string SampleAllItemsCliOutput = """
    {"diagnostic":{"code":"FETCHING_TRANSACTIONS","end_date":"2026-07-27","level":"info","message":"Fetching transactions...","start_date":"2026-07-20"}}
    {"items":[{"item":{"item_id":"item-checking","institution_id":"ins_127991"},"accounts":[{"account_id":"plaid-checking-1","balances":{"available":4959.63,"current":5136.49}}],"transactions":[{"transaction_id":"txn-checking-1","account_id":"plaid-checking-1","amount":58.83,"date":"2026-07-26","name":"Teds Montana","merchant_name":"Teds Montana","pending":false}],"total_transactions":1},{"item":{"item_id":"item-amex","institution_id":"ins_10"},"accounts":[{"account_id":"plaid-amex-1","balances":{"available":0,"current":45635.21}}],"transactions":[{"transaction_id":"txn-amex-1","account_id":"plaid-amex-1","amount":55.63,"date":"2026-07-25","name":"INGLES MARKETS #474","merchant_name":"Ingles Markets","pending":false}],"total_transactions":1}]}
    """;

    private async Task<Account> CreateAmexAccountAsync()
    {
        var account = new Account { Name = "Amex", Type = AccountType.ActiveSpending };
        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task ImportAsync_ParsesThePayloadLine_IgnoringTheDiagnosticLine()
    {
        var account = await CreateCheckingAccountAsync();

        var summary = await CreateSut().ImportAsync(Context, SampleCliOutput, BuildAccountMap(account), new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(2, summary.TransactionsAdded);
    }

    [Fact]
    public async Task ImportAsync_FlipsTheSignConvention_PositiveMeansMoneyOut()
    {
        var account = await CreateCheckingAccountAsync();

        await CreateSut().ImportAsync(Context, SampleCliOutput, BuildAccountMap(account), new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));

        var chipotle = await Context.BankTransactions.SingleAsync(t => t.Description == "Chipotle Mexican Grill");
        Assert.Equal(-33.87m, chipotle.Amount);

        var payroll = await Context.BankTransactions.SingleAsync(t => t.Description.Contains("PAYROLL"));
        Assert.Equal(4492.86m, payroll.Amount);
    }

    [Fact]
    public async Task ImportAsync_PendingTransaction_HasNoPostedDate_ButStillHasATransactionDate()
    {
        var account = await CreateCheckingAccountAsync();

        await CreateSut().ImportAsync(Context, SampleCliOutput, BuildAccountMap(account), new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));

        var chipotle = await Context.BankTransactions.SingleAsync(t => t.Description == "Chipotle Mexican Grill");
        Assert.Null(chipotle.PostedDate);
        Assert.Equal(new DateOnly(2026, 7, 25), chipotle.TransactionDate);
    }

    [Fact]
    public async Task ImportAsync_PostedTransaction_HasPostedDateSet()
    {
        var account = await CreateCheckingAccountAsync();

        await CreateSut().ImportAsync(Context, SampleCliOutput, BuildAccountMap(account), new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));

        var payroll = await Context.BankTransactions.SingleAsync(t => t.Description.Contains("PAYROLL"));
        Assert.Equal(new DateOnly(2026, 7, 24), payroll.PostedDate);
    }

    [Fact]
    public async Task ImportAsync_SetsImportSourceToPlaid_AndPopulatesMerchantFromMerchantName()
    {
        var account = await CreateCheckingAccountAsync();

        await CreateSut().ImportAsync(Context, SampleCliOutput, BuildAccountMap(account), new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));

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

        await sut.ImportAsync(Context, SampleCliOutput, accountMap, new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));
        var summary = await sut.ImportAsync(Context, SampleCliOutput, accountMap, new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));

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

        var summary = await CreateSut().ImportAsync(Context, SampleCliOutput, BuildAccountMap(account), new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(1, summary.TransactionsAdded); // only Chipotle is genuinely new
        Assert.Equal(1, summary.DuplicatesSkipped); // the payroll deposit is recognized as already present
        Assert.Equal(1, await Context.BankTransactions.CountAsync(t => t.Description.Contains("PAYROLL")));
    }

    // Real scenario this guards (found live 2026-07-28): Plaid doesn't update a pending
    // transaction in place when it posts - it issues a brand new transaction_id and links
    // back to the original one via pending_transaction_id. Neither existing dedup check
    // catches this: ExternalId legitimately differs, and the cross-source posted-date+amount
    // check requires the existing row to already have a real PostedDate, which the pending
    // row doesn't have yet.
    private const string PendingChatGptCliOutput = """
    {"diagnostic":{"code":"FETCHING_TRANSACTIONS","end_date":"2026-07-27","level":"info","message":"Fetching transactions...","start_date":"2026-07-20"}}
    {"accounts":[{"account_id":"plaid-checking-1","balances":{"available":5092.71,"current":5136.49}}],"total_transactions":1,"transactions":[{"transaction_id":"txn-chatgpt-pending","account_id":"plaid-checking-1","amount":20.00,"date":"2026-07-27","name":"CHATGPT","merchant_name":"OpenAI","pending":true}]}
    """;

    private const string PostedChatGptCliOutput = """
    {"diagnostic":{"code":"FETCHING_TRANSACTIONS","end_date":"2026-07-28","level":"info","message":"Fetching transactions...","start_date":"2026-07-20"}}
    {"accounts":[{"account_id":"plaid-checking-1","balances":{"available":5072.71,"current":5116.49}}],"total_transactions":1,"transactions":[{"transaction_id":"txn-chatgpt-posted","account_id":"plaid-checking-1","amount":20.00,"date":"2026-07-27","name":"OPENAI *CHATGPT SUBSSAN FRANCISCO","merchant_name":"OpenAI","pending":false,"pending_transaction_id":"txn-chatgpt-pending"}]}
    """;

    [Fact]
    public async Task ImportAsync_PostedTransactionReferencingAPendingId_UpdatesTheExistingRow_InsteadOfInsertingADuplicate()
    {
        var account = await CreateCheckingAccountAsync();
        var accountMap = BuildAccountMap(account);
        var sut = CreateSut();
        await sut.ImportAsync(Context, PendingChatGptCliOutput, accountMap, new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));

        var summary = await sut.ImportAsync(Context, PostedChatGptCliOutput, accountMap, new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(1, await Context.BankTransactions.CountAsync());
        Assert.Equal(0, summary.TransactionsAdded);
        Assert.Equal(0, summary.DuplicatesSkipped);
        Assert.Equal(1, summary.PendingTransactionsUpdated);

        var merged = await Context.BankTransactions.SingleAsync();
        Assert.Equal(new DateOnly(2026, 7, 27), merged.PostedDate);
        Assert.Equal("OPENAI *CHATGPT SUBSSAN FRANCISCO", merged.Description);
        Assert.Equal("txn-chatgpt-posted", merged.ExternalId);
    }

    [Fact]
    public async Task ImportAsync_MergingAPostedTransactionIntoItsPendingRow_PreservesTheExistingCategoryId()
    {
        var account = await CreateCheckingAccountAsync();
        var accountMap = BuildAccountMap(account);
        var category = new Category { Name = "Subscriptions" };
        Context.Categories.Add(category);
        await Context.SaveChangesAsync();
        var sut = CreateSut();
        await sut.ImportAsync(Context, PendingChatGptCliOutput, accountMap, new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));
        var pending = await Context.BankTransactions.SingleAsync();
        pending.CategoryId = category.Id;
        await Context.SaveChangesAsync();

        await sut.ImportAsync(Context, PostedChatGptCliOutput, accountMap, new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));

        var merged = await Context.BankTransactions.SingleAsync();
        Assert.Equal(category.Id, merged.CategoryId);
    }

    [Fact]
    public async Task ImportAsync_PostedTransactionReferencingAPendingId_ButNoMatchingRowExists_ImportsNormally()
    {
        // The pending version may never have been imported (e.g. outside a previous
        // import's date window) - pending_transaction_id pointing at nothing local must
        // not crash, just fall through to a normal insert.
        var account = await CreateCheckingAccountAsync();
        var accountMap = BuildAccountMap(account);

        var summary = await CreateSut().ImportAsync(Context, PostedChatGptCliOutput, accountMap, new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(1, summary.TransactionsAdded);
        Assert.Equal(0, summary.PendingTransactionsUpdated);
        Assert.Equal(1, await Context.BankTransactions.CountAsync());
    }

    [Fact]
    public async Task ImportAsync_UnmappedAccount_IsSkippedAndReported_NotACrash()
    {
        var summary = await CreateSut().ImportAsync(Context, SampleCliOutput, new Dictionary<string, int>(), new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));

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

        await CreateSut().ImportAsync(Context, SampleCliOutput, BuildAccountMap(account), new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));

        var chipotle = await Context.BankTransactions.SingleAsync(t => t.Description == "Chipotle Mexican Grill");
        Assert.Equal(restaurants.Id, chipotle.CategoryId);
    }

    [Fact]
    public async Task ImportAsync_ChekingAccount_CreatesACheckingBalanceSnapshot_FromTheAvailableBalance()
    {
        // "available" confirmed directly against real data as the figure comparable to
        // what SimpleFin calls "balance" for this same account - not "current".
        var account = await CreateCheckingAccountAsync();

        var summary = await CreateSut().ImportAsync(Context, SampleCliOutput, BuildAccountMap(account), new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));

        var snapshot = await Context.CheckingBalanceSnapshots.SingleAsync();
        Assert.Equal(new DateOnly(2026, 7, 25), snapshot.AsOfDate);
        Assert.Equal(new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero), snapshot.AsOfTimestamp);
        Assert.Equal(5092.71m, snapshot.Balance);
        Assert.Equal(1, summary.BalanceSnapshotsAdded);
    }

    [Fact]
    public async Task ImportAsync_AllItemsShape_ImportsTransactionsFromEveryLinkedItem_NotJustTheFirst()
    {
        // Real bug this guards: `--all` output nests each item's transactions inside a
        // top-level "items" array instead of a top-level "transactions" property - a
        // parser that only ever looks for a top-level "transactions" property throws
        // instead of finding either item's data at all.
        var checking = await CreateCheckingAccountAsync();
        var amex = await CreateAmexAccountAsync();
        var accountMap = new Dictionary<string, int> { ["plaid-checking-1"] = checking.Id, ["plaid-amex-1"] = amex.Id };

        var summary = await CreateSut().ImportAsync(Context, SampleAllItemsCliOutput, accountMap, new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(2, summary.TransactionsAdded);
        Assert.NotNull(await Context.BankTransactions.SingleOrDefaultAsync(t => t.Description == "Teds Montana" && t.AccountId == checking.Id));
        Assert.NotNull(await Context.BankTransactions.SingleOrDefaultAsync(t => t.Merchant == "Ingles Markets" && t.AccountId == amex.Id));
    }

    [Fact]
    public async Task ImportAsync_AllItemsShape_OnlyChecking_GetsABalanceSnapshot_AmexDoesNot()
    {
        var checking = await CreateCheckingAccountAsync();
        var amex = await CreateAmexAccountAsync();
        var accountMap = new Dictionary<string, int> { ["plaid-checking-1"] = checking.Id, ["plaid-amex-1"] = amex.Id };

        var summary = await CreateSut().ImportAsync(Context, SampleAllItemsCliOutput, accountMap, new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(1, summary.BalanceSnapshotsAdded);
        var snapshot = await Context.CheckingBalanceSnapshots.SingleAsync();
        Assert.Equal(4959.63m, snapshot.Balance);
    }

    [Fact]
    public async Task ImportAsync_NonCheckingAccount_DoesNotCreateACheckingBalanceSnapshot()
    {
        var account = new Account { Name = "Amex", Type = AccountType.ActiveSpending };
        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();

        var summary = await CreateSut().ImportAsync(Context, SampleCliOutput, BuildAccountMap(account), new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));

        Assert.Empty(await Context.CheckingBalanceSnapshots.ToListAsync());
        Assert.Equal(0, summary.BalanceSnapshotsAdded);
    }
}
