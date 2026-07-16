using System.Net;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Ingestion;
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
        return new SimpleFinSyncService(new HttpClient(handler), new DedupService(), new CategorizationService());
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
    public async Task RunAsync_OnHttpFailure_RecordsAFailedRunWithAnErrorMessage_InsteadOfThrowing()
    {
        var sut = CreateSut("Internal Server Error", HttpStatusCode.InternalServerError);

        var run = await sut.RunAsync(Context, "https://u:p@beta-bridge.simplefin.org/simplefin/access/abc", new Dictionary<string, int>(), DateTimeOffset.UtcNow.AddDays(-45));

        Assert.False(run.Success);
        Assert.NotNull(run.ErrorMessage);
        Assert.Null(run.Summary);
    }
}
