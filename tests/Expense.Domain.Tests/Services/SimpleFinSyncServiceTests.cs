using System.Net;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Ingestion;
using Expense.Domain.Services.Ingestion.ManualCharges;
using Expense.Domain.Services.Ingestion.SimpleFin;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services;

public class SimpleFinSyncServiceTests : DatabaseTestBase
{
    private const string OneAccountResponse = """
    {
      "errors": [],
      "accounts": [
        {
          "id": "ACT-amex-test",
          "org": { "name": "American Express" },
          "name": "Classic Gold Card (1000)",
          "balance": "-4000.00",
          "balance-date": 1783980415,
          "transactions": []
        }
      ]
    }
    """;

    private static SimpleFinSyncService CreateSut(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new FakeHttpMessageHandler(responseBody, statusCode);
        return new SimpleFinSyncService(new HttpClient(handler), new DedupService(), new CategorizationService(), new ManualChargeMatchingService());
    }

    [Fact]
    public async Task RunAsync_OnSuccess_RecordsASuccessfulRunWithASummary()
    {
        // Debt accounts only ever get a balance snapshot (see SimpleFinImportService) -
        // ActiveSpending accounts like a real Amex don't, so this uses a debt account to
        // exercise a non-zero BalanceSnapshotsAdded count in the summary text.
        var discover = new Account { Name = "Discover", Type = AccountType.Debt };
        Context.Accounts.Add(discover);
        await Context.SaveChangesAsync();
        var accountMap = new Dictionary<string, int> { ["ACT-amex-test"] = discover.Id };

        var sut = CreateSut(OneAccountResponse);

        var run = await sut.RunAsync(Context, "https://u:p@beta-bridge.simplefin.org/simplefin/access/abc", accountMap, DateTimeOffset.UtcNow.AddDays(-45));

        Assert.True(run.Success);
        Assert.Equal(ImportSource.SimpleFin, run.Source);
        Assert.Null(run.ErrorMessage);
        Assert.NotNull(run.Summary);
        Assert.Contains("balance snapshots added: 1", run.Summary);

        var reloaded = await Context.ImportRuns.SingleAsync(r => r.Id == run.Id);
        Assert.True(reloaded.Success);
    }

    [Fact]
    public async Task RunAsync_OnSuccess_AlsoSweepsUpOtherStillPendingRowsAgainstCurrentRules()
    {
        var discover = new Account { Name = "Discover", Type = AccountType.Debt };
        Context.Accounts.Add(discover);
        var truist = new Category { Name = "Truist" };
        Context.Categories.Add(truist);
        await Context.SaveChangesAsync();
        Context.MerchantRules.Add(new MerchantRule { MerchantPattern = "TRUIST MORTG OLB MTGPMT", CategoryId = truist.Id });

        // Simulates a row a bug (since fixed) previously left stuck: it matches the
        // existing rule now, but wasn't touched by this particular import at all.
        var stuckTransaction = new BankTransaction
        {
            AccountId = discover.Id, TransactionDate = new DateOnly(2026, 6, 8),
            Description = "TRUIST MORTG     OLB MTGPMT 260604 3001469588      MARK SALCEDO",
            Amount = -2681.22m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(stuckTransaction);
        await Context.SaveChangesAsync();

        var accountMap = new Dictionary<string, int> { ["ACT-amex-test"] = discover.Id };
        var sut = CreateSut(OneAccountResponse);

        var run = await sut.RunAsync(Context, "https://u:p@beta-bridge.simplefin.org/simplefin/access/abc", accountMap, DateTimeOffset.UtcNow.AddDays(-45));

        Assert.True(run.Success);
        Assert.Equal(truist.Id, stuckTransaction.CategoryId);
        Assert.Contains("re-categorized 1 previously pending", run.Summary);
    }

    [Fact]
    public async Task RunAsync_OnSuccess_RemovesAManuallyEnteredPlaceholder_WhenTheRealTransactionPosts()
    {
        const string responseWithARealTransaction = """
        {
          "errors": [],
          "accounts": [
            {
              "id": "ACT-amex-test",
              "org": { "name": "American Express" },
              "name": "Classic Gold Card (1000)",
              "balance": "-4000.00",
              "balance-date": 1783980415,
              "transactions": [
                { "id": "TRN-morgan", "posted": 1784678400, "amount": "-131.65", "description": "MORGAN COMPOUNDING PHARMACY" }
              ]
            }
          ]
        }
        """;

        var amex = new Account { Name = "Amex", Type = AccountType.ActiveSpending };
        Context.Accounts.Add(amex);
        await Context.SaveChangesAsync();
        var placeholder = new BankTransaction
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 20), PostedDate = null,
            Description = "MORGAN COMPOUDING", Amount = -131.65m, ImportSource = "ManualScreenshot", CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(placeholder);
        await Context.SaveChangesAsync();

        var accountMap = new Dictionary<string, int> { ["ACT-amex-test"] = amex.Id };
        var sut = CreateSut(responseWithARealTransaction);

        var run = await sut.RunAsync(Context, "https://u:p@beta-bridge.simplefin.org/simplefin/access/abc", accountMap, DateTimeOffset.UtcNow.AddDays(-45));

        Assert.True(run.Success);
        Assert.Contains("removed 1 manually-entered charge(s) now confirmed posted", run.Summary);
        Assert.False(await Context.BankTransactions.AnyAsync(t => t.Id == placeholder.Id));
    }

    [Fact]
    public async Task RunAsync_OnHttpFailure_RecordsAFailedRunWithAnErrorMessage_InsteadOfThrowing()
    {
        var sut = CreateSut("Internal Server Error", HttpStatusCode.InternalServerError);

        var run = await sut.RunAsync(Context, "https://u:p@beta-bridge.simplefin.org/simplefin/access/abc", new Dictionary<string, int>(), DateTimeOffset.UtcNow.AddDays(-45));

        Assert.False(run.Success);
        Assert.NotNull(run.ErrorMessage);
        Assert.Null(run.Summary);
    }
}
