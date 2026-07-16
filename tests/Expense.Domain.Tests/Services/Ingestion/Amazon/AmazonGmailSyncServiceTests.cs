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
        public Task<IReadOnlyList<GmailMessage>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult(query.Contains("auto-confirm@amazon.com") ? orderMessages : refundMessages);
    }

    private static AmazonGmailSyncService CreateSut(
        IReadOnlyList<GmailMessage>? orderMessages = null, IReadOnlyList<GmailMessage>? refundMessages = null) =>
        new(
            new FakeGmailMessageSource(orderMessages ?? [], refundMessages ?? []),
            new AmazonImportService(new AmazonOrderEmailParser(), new AmazonRefundEmailParser()),
            new CategorizationService());

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
    public async Task RunAsync_ImportsRefundEmails_AndAppliesThemToTheMatchingOrder()
    {
        var order = new AmazonOrderItem
        {
            OrderId = "112-1510135-3538618", ItemTitle = "MOS Cardstock Paper - 11\" x 14\"",
            Quantity = 1, Price = 21.99m, OrderDate = new DateOnly(2026, 7, 1), CreatedAt = DateTimeOffset.UtcNow
        };
        Context.AmazonOrderItems.Add(order);
        await Context.SaveChangesAsync();

        var sut = CreateSut(refundMessages: [new GmailMessage("msg-2", "Your refund", RefundEmail, new DateOnly(2026, 7, 15))]);

        var result = await sut.RunAsync(Context);

        Assert.True(result.Run.Success);
        Assert.Equal(1, result.RefundsApplied);
        Assert.Contains("refunds applied: 1", result.Run.Summary);
        var reloaded = await Context.AmazonOrderItems.SingleAsync();
        Assert.Equal(23.31m, reloaded.RefundAmount);
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
}
