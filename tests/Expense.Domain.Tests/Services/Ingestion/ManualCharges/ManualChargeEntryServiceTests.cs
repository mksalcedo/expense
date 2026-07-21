using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Ingestion.ManualCharges;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.Ingestion.ManualCharges;

public class ManualChargeEntryServiceTests : DatabaseTestBase
{
    private class FakeAnthropicVisionClient(string responseText) : IAnthropicVisionClient
    {
        public Task<string> SendImagePromptAsync(byte[] imageBytes, string mediaType, string prompt, CancellationToken cancellationToken = default) =>
            Task.FromResult(responseText);
    }

    private static ManualChargeEntryService CreateSut(string parsedJson) => new(
        new AmexScreenshotParsingService(new FakeAnthropicVisionClient(parsedJson)),
        new ManualChargeMatchingService(),
        new CategorizationService());

    private async Task<Account> CreateAmexAsync() =>
        (await Context.Accounts.AddAsync(new Account { Name = "Amex", Type = AccountType.ActiveSpending })).Entity;

    [Fact]
    public async Task ReviewScreenshotAsync_ConvertsChargesToNegativeAmounts()
    {
        var amex = await CreateAmexAsync();
        await Context.SaveChangesAsync();
        const string parsed = """[{"date": "2026-07-20", "description": "MORGAN COMPOUDING", "amount": 131.65, "isCredit": false}]""";
        var sut = CreateSut(parsed);

        var rows = await sut.ReviewScreenshotAsync(Context, amex.Id, [1, 2, 3], "image/png", new DateOnly(2026, 7, 21));

        var row = Assert.Single(rows);
        Assert.Equal(-131.65m, row.Amount);
        Assert.False(row.IsDuplicate);
        Assert.Null(row.DuplicateReason);
    }

    [Fact]
    public async Task ReviewScreenshotAsync_ConvertsCreditsToPositiveAmounts()
    {
        var amex = await CreateAmexAsync();
        await Context.SaveChangesAsync();
        const string parsed = """[{"date": "2026-07-20", "description": "ONLINE PAYMENT - THANK YOU", "amount": 1000.00, "isCredit": true}]""";
        var sut = CreateSut(parsed);

        var rows = await sut.ReviewScreenshotAsync(Context, amex.Id, [1], "image/png", new DateOnly(2026, 7, 21));

        var row = Assert.Single(rows);
        Assert.Equal(1000.00m, row.Amount);
    }

    [Fact]
    public async Task ReviewScreenshotAsync_FlagsARowThatMatchesAnExistingTransaction()
    {
        var amex = await CreateAmexAsync();
        await Context.SaveChangesAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 18), PostedDate = new DateOnly(2026, 7, 18),
            Description = "INGLES MARKETS #474 NORCROSS GA", Amount = -171.95m, ImportSource = "SimpleFin", CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();
        const string parsed = """[{"date": "2026-07-18", "description": "INGLES MARKETS", "amount": 171.95, "isCredit": false}]""";
        var sut = CreateSut(parsed);

        var rows = await sut.ReviewScreenshotAsync(Context, amex.Id, [1], "image/png", new DateOnly(2026, 7, 21));

        var row = Assert.Single(rows);
        Assert.True(row.IsDuplicate);
        Assert.Contains("INGLES MARKETS #474 NORCROSS GA", row.DuplicateReason);
        Assert.Contains("posted 07/18/2026", row.DuplicateReason);
    }

    [Fact]
    public async Task AddChargesAsync_CreatesRealBankTransactions_WithPostedDateNullAndManualScreenshotSource()
    {
        var amex = await CreateAmexAsync();
        await Context.SaveChangesAsync();
        var sut = CreateSut("[]");
        var rows = new List<ManualChargeReviewRow>
        {
            new() { Date = new DateOnly(2026, 7, 20), Description = "MORGAN COMPOUDING", Amount = -131.65m, IsDuplicate = false }
        };

        var addedCount = await sut.AddChargesAsync(Context, amex.Id, rows);

        Assert.Equal(1, addedCount);
        var transaction = await Context.BankTransactions.SingleAsync(t => t.AccountId == amex.Id);
        Assert.Equal(new DateOnly(2026, 7, 20), transaction.TransactionDate);
        Assert.Null(transaction.PostedDate);
        Assert.Equal(-131.65m, transaction.Amount);
        Assert.Equal("ManualScreenshot", transaction.ImportSource);
    }

    [Fact]
    public async Task AddChargesAsync_RunsNormalMerchantRuleCategorization()
    {
        var amex = await CreateAmexAsync();
        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();
        Context.MerchantRules.Add(new MerchantRule { MerchantPattern = "%INGLES%", CategoryId = groceries.Id });
        await Context.SaveChangesAsync();

        var sut = CreateSut("[]");
        var rows = new List<ManualChargeReviewRow>
        {
            new() { Date = new DateOnly(2026, 7, 20), Description = "INGLES MARKETS #474", Amount = -50m, IsDuplicate = false }
        };

        await sut.AddChargesAsync(Context, amex.Id, rows);

        var transaction = await Context.BankTransactions.SingleAsync(t => t.AccountId == amex.Id);
        Assert.Equal(groceries.Id, transaction.CategoryId);
    }
}
