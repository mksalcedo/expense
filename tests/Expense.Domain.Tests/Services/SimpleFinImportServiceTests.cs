using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Ingestion;
using Expense.Domain.Services.Ingestion.SimpleFin;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services;

public class SimpleFinImportServiceTests : DatabaseTestBase
{
    // Keyed on the stable "id" field, not "name" - a real account's display name was
    // observed to contain garbled unicode replacement characters, so name is not a
    // safe mapping key even though it reads nicely in most cases.
    private const string TwoAccountResponse = """
    {
      "errors": [],
      "accounts": [
        {
          "id": "ACT-amex-test",
          "org": { "name": "American Express" },
          "name": "Classic Gold Card (1000)",
          "balance": "-47341.98",
          "balance-date": 1783980415,
          "transactions": [
            { "id": "amex-tx-1", "posted": 1783857600, "amount": "-18.83", "description": "TRADER JOE S #734" },
            { "id": "amex-tx-2", "posted": 1783857600, "amount": "-9.53", "description": "AMAZON MARKETPLACE" }
          ]
        },
        {
          "id": "ACT-discover-test",
          "org": { "name": "Discover" },
          "name": "Discover Credit Card",
          "balance": "0.00",
          "balance-date": 1783990326,
          "transactions": [
            { "id": "discover-tx-1", "posted": 1783512000, "amount": "8.99", "description": "CASHBACK BONUS" }
          ]
        }
      ]
    }
    """;

    private SimpleFinImportService CreateSut(string responseBody)
    {
        var handler = new FakeHttpMessageHandler(responseBody);
        var client = new SimpleFinClient(new HttpClient(handler), "https://u:p@beta-bridge.simplefin.org/simplefin/access/abc");
        return new SimpleFinImportService(client, new DedupService(), new CategorizationService());
    }

    private async Task<(Account amex, Account discover)> CreateAccountsAsync()
    {
        var amex = new Account { Name = "Amex", Type = AccountType.ActiveSpending };
        var discover = new Account { Name = "Discover", Type = AccountType.Debt };
        Context.Accounts.AddRange(amex, discover);
        await Context.SaveChangesAsync();
        return (amex, discover);
    }

    private static Dictionary<string, int> BuildAccountMap(Account amex, Account discover) => new()
    {
        ["ACT-amex-test"] = amex.Id,
        ["ACT-discover-test"] = discover.Id
    };

    [Fact]
    public async Task Import_ActiveSpendingAccount_CreatesTransactionsAndAppliesCategorization()
    {
        var (amex, discover) = await CreateAccountsAsync();
        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();
        Context.MerchantRules.Add(new MerchantRule { MerchantPattern = "%TRADER JOE%", CategoryId = groceries.Id });
        await Context.SaveChangesAsync();

        var sut = CreateSut(TwoAccountResponse);
        await sut.ImportAsync(Context, BuildAccountMap(amex, discover), DateTimeOffset.UtcNow.AddDays(-45));

        var amexTransactions = await Context.BankTransactions.Where(t => t.AccountId == amex.Id).ToListAsync();
        Assert.Equal(2, amexTransactions.Count);

        var traderJoes = amexTransactions.Single(t => t.Description.Contains("TRADER JOE"));
        Assert.Equal(groceries.Id, traderJoes.CategoryId);

        var amazon = amexTransactions.Single(t => t.Description.Contains("AMAZON"));
        Assert.True(amazon.IsAmazonMerchant);
        Assert.Null(amazon.CategoryId);
    }

    [Fact]
    public async Task Import_DebtAccount_OnlyStoresBalance_TransactionsAreDiscarded()
    {
        var (amex, discover) = await CreateAccountsAsync();
        var sut = CreateSut(TwoAccountResponse);

        await sut.ImportAsync(Context, BuildAccountMap(amex, discover), DateTimeOffset.UtcNow.AddDays(-45));

        var discoverTransactions = await Context.BankTransactions.Where(t => t.AccountId == discover.Id).ToListAsync();
        Assert.Empty(discoverTransactions);

        var snapshot = await Context.DebtBalanceSnapshots.SingleAsync(s => s.AccountId == discover.Id);
        Assert.Equal(0.00m, snapshot.Balance);
    }

    [Fact]
    public async Task Import_CheckingAccount_UpdatesCheckingBalanceSnapshots()
    {
        var checking = new Account { Name = "Wells Fargo Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        const string checkingResponse = """
        {
          "errors": [],
          "accounts": [
            { "id": "ACT-checking-test", "org": { "name": "Wells Fargo" }, "name": "EVERYDAY CHECKING", "balance": "6463.02", "balance-date": 1783980195, "transactions": [] }
          ]
        }
        """;
        var sut = CreateSut(checkingResponse);
        var map = new Dictionary<string, int> { ["ACT-checking-test"] = checking.Id };

        await sut.ImportAsync(Context, map, DateTimeOffset.UtcNow.AddDays(-45));

        var snapshot = await Context.CheckingBalanceSnapshots.SingleAsync();
        Assert.Equal(6463.02m, snapshot.Balance);
    }

    [Fact]
    public async Task Import_RunTwice_SkipsAlreadyImportedTransactions()
    {
        var (amex, discover) = await CreateAccountsAsync();
        var sut = CreateSut(TwoAccountResponse);
        var map = BuildAccountMap(amex, discover);

        await sut.ImportAsync(Context, map, DateTimeOffset.UtcNow.AddDays(-45));
        var summary = await sut.ImportAsync(Context, map, DateTimeOffset.UtcNow.AddDays(-45));

        var amexTransactionCount = await Context.BankTransactions.CountAsync(t => t.AccountId == amex.Id);
        Assert.Equal(2, amexTransactionCount); // still just 2, not 4
        Assert.Equal(0, summary.TransactionsAdded);
        Assert.Equal(2, summary.DuplicatesSkipped);
    }

    [Fact]
    public async Task Import_UnmappedSimpleFinAccount_IsReportedByIdNotErrored()
    {
        var (amex, _) = await CreateAccountsAsync();
        var sut = CreateSut(TwoAccountResponse);
        var mapMissingDiscover = new Dictionary<string, int> { ["ACT-amex-test"] = amex.Id };

        var summary = await sut.ImportAsync(Context, mapMissingDiscover, DateTimeOffset.UtcNow.AddDays(-45));

        Assert.Contains("ACT-discover-test", summary.UnmappedAccounts);
    }
}
