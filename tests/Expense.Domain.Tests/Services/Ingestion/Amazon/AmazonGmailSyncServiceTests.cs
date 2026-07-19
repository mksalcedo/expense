using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Ingestion.Amazon;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.Ingestion.Amazon;

public class AmazonGmailSyncServiceTests : DatabaseTestBase
{
    private const string SingleItemOrderEmail = """
        Order #
        113-5254486-7378657

        * Qunol Ultra CoQ10 100mg, 3x Better Absorption
          Quantity: 1
          29.97 USD

        Grand Total:
        31.77 USD
        """;

    private const string RefundEmail = """
        Hello, We're writing to let you know we processed your refund of $23.31 for your Order 112-1510135-3538618 from JFP Western Inc..

        This refund is for the following item(s):     Item: MOS Cardstock Paper - 11" x 14"     Quantity: 1     ASIN: B0DKB7SPSR     Reason for refund: Account adjustment     Here's the breakdown of your refund for this item:         Item Refund: $21.99         Item Tax Refund: $1.32
        """;

    private class FakeGmailMessageSource(
        IReadOnlyList<GmailMessage> orderMessages, IReadOnlyList<GmailMessage> refundMessages) : IGmailMessageSource
    {
        public List<string> Queries { get; } = [];

        public Task<IReadOnlyList<GmailMessage>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(query.Contains("auto-confirm@amazon.com") ? orderMessages : refundMessages);
        }
    }

    private static AmazonGmailSyncService CreateSut(
        IReadOnlyList<GmailMessage>? orderMessages = null, IReadOnlyList<GmailMessage>? refundMessages = null) =>
        new(
            new FakeGmailMessageSource(orderMessages ?? [], refundMessages ?? []),
            new AmazonImportService(new AmazonOrderEmailParser(), new AmazonRefundEmailParser()),
            new CategorizationService());

    private static (AmazonGmailSyncService Sut, FakeGmailMessageSource Fake) CreateSutWithFake(
        IReadOnlyList<GmailMessage>? orderMessages = null, IReadOnlyList<GmailMessage>? refundMessages = null)
    {
        var fake = new FakeGmailMessageSource(orderMessages ?? [], refundMessages ?? []);
        var sut = new AmazonGmailSyncService(fake, new AmazonImportService(new AmazonOrderEmailParser(), new AmazonRefundEmailParser()), new CategorizationService());
        return (sut, fake);
    }

    [Fact]
    public async Task RunAsync_ImportsOrderEmails_AndRecordsASuccessfulRunWithASummary()
    {
        var sut = CreateSut(orderMessages: [new GmailMessage("msg-1", "Your order", SingleItemOrderEmail, new DateOnly(2026, 7, 14))]);

        var result = await sut.RunAsync(Context);

        Assert.True(result.Run.Success);
        Assert.Equal(ImportSource.AmazonGmail, result.Run.Source);
        Assert.Null(result.Run.ErrorMessage);
        Assert.Equal(1, result.ItemsAdded);
        Assert.Contains("Order items added: 1", result.Run.Summary);
        Assert.Equal(1, await Context.AmazonOrderItems.CountAsync());
    }

    [Fact]
    public async Task RunAsync_ImportsRefundEmails_AsTheirOwnNegativeEntry()
    {
        var sut = CreateSut(refundMessages: [new GmailMessage("msg-2", "Your refund", RefundEmail, new DateOnly(2026, 7, 15))]);

        var result = await sut.RunAsync(Context);

        Assert.True(result.Run.Success);
        Assert.Equal(1, result.RefundsApplied);
        Assert.Contains("refunds applied: 1", result.Run.Summary);
        var reloaded = await Context.AmazonOrderItems.SingleAsync();
        Assert.Equal(-23.31m, reloaded.Price);
        Assert.Null(reloaded.RefundAmount); // the refund is its own row now, not an adjustment to another row
    }

    [Fact]
    public async Task RunAsync_WhenAMessageHasNoPlainTextBody_RecordsItAsAParseFailure_ButStillSucceedsOverall()
    {
        var sut = CreateSut(orderMessages: [new GmailMessage("msg-3", "HTML-only order", null, new DateOnly(2026, 7, 14))]);

        var result = await sut.RunAsync(Context);

        Assert.True(result.Run.Success);
        Assert.Single(result.ParseFailures);
        Assert.Contains("could not extract a plain-text body", result.ParseFailures[0]);
        Assert.Contains("1 email(s) failed to parse", result.Run.Summary);
    }

    [Fact]
    public async Task RunAsync_OnSuccess_AlsoSweepsUpOtherStillPendingItemsAgainstCurrentProducts()
    {
        var supplements = new Category { Name = "Supplements" };
        Context.Categories.Add(supplements);
        await Context.SaveChangesAsync();
        Context.Products.Add(new Product { ProductPattern = "%QUNOL%", CategoryId = supplements.Id });

        // A row a prior bug (or a product created since) previously left stuck - untouched
        // by this particular sync's own messages, but it should still get swept up.
        var stuckItem = new AmazonOrderItem
        {
            OrderId = "999", OrderDate = new DateOnly(2026, 6, 1), ItemTitle = "Qunol Ultra CoQ10 200mg",
            Price = 45m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow
        };
        Context.AmazonOrderItems.Add(stuckItem);
        await Context.SaveChangesAsync();

        var sut = CreateSut();

        var result = await sut.RunAsync(Context);

        Assert.True(result.Run.Success);
        Assert.Equal(supplements.Id, stuckItem.CategoryId);
        Assert.Contains("re-categorized 1 previously pending item(s)", result.Run.Summary);
    }

    [Fact]
    public async Task RunAsync_WhenNoPriorSuccessfulRunExists_SearchesTheFallback400DayWindow()
    {
        var (sut, fake) = CreateSutWithFake();

        await sut.RunAsync(Context);

        Assert.All(fake.Queries, q => Assert.Contains("newer_than:400d", q));
    }

    [Fact]
    public async Task RunAsync_WhenAPriorSuccessfulRunExists_SearchesFromFourDaysBeforeThatRunsDate_InsteadOfTheFallbackWindow()
    {
        var lastRunAt = new DateTimeOffset(2026, 7, 16, 9, 0, 0, TimeSpan.Zero);
        Context.ImportRuns.Add(new ImportRun { Source = ImportSource.AmazonGmail, RanAt = lastRunAt, Success = true });
        await Context.SaveChangesAsync();

        var (sut, fake) = CreateSutWithFake();

        await sut.RunAsync(Context);

        Assert.All(fake.Queries, q => Assert.Contains("after:2026/07/12", q));
        Assert.All(fake.Queries, q => Assert.DoesNotContain("newer_than:400d", q));
    }

    [Fact]
    public async Task RunAsync_WhenOnlyAFailedPriorRunExists_StillSearchesTheFallback400DayWindow()
    {
        Context.ImportRuns.Add(new ImportRun { Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = false });
        await Context.SaveChangesAsync();

        var (sut, fake) = CreateSutWithFake();

        await sut.RunAsync(Context);

        Assert.All(fake.Queries, q => Assert.Contains("newer_than:400d", q));
    }
}
