using Expense.Domain.Entities;
using Expense.Domain.Services.Ingestion.Amazon;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.Ingestion.Amazon;

public class AmazonImportServiceTests : DatabaseTestBase
{
    private readonly AmazonImportService _sut = new(new AmazonOrderEmailParser(), new AmazonRefundEmailParser());

    private const string SingleItemEmail = """
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

    [Fact]
    public async Task ImportOrder_NewOrder_AddsItemsAndAppliesProductMatch()
    {
        var supplements = new Category { Name = "Supplements", IsBudgeted = true };
        Context.Categories.Add(supplements);
        await Context.SaveChangesAsync();
        Context.Products.Add(new Product { ProductPattern = "%QUNOL%", CategoryId = supplements.Id });
        await Context.SaveChangesAsync();

        var summary = await _sut.ImportOrderAsync(Context, SingleItemEmail, new DateOnly(2026, 7, 14));

        Assert.Equal(1, summary.ItemsAdded);
        var item = await Context.AmazonOrderItems.SingleAsync(i => i.OrderId == "113-5254486-7378657");
        Assert.Equal(supplements.Id, item.CategoryId);
        Assert.NotNull(item.ProductId);
    }

    [Fact]
    public async Task ImportOrder_NoMatchingProduct_LeavesPendingCategorization()
    {
        var summary = await _sut.ImportOrderAsync(Context, SingleItemEmail, new DateOnly(2026, 7, 14));

        Assert.Equal(1, summary.ItemsAdded);
        var item = await Context.AmazonOrderItems.SingleAsync(i => i.OrderId == "113-5254486-7378657");
        Assert.Null(item.ProductId);
        Assert.Null(item.CategoryId);
    }

    [Fact]
    public async Task ImportOrder_AlreadyImported_SkipsAsDuplicate()
    {
        await _sut.ImportOrderAsync(Context, SingleItemEmail, new DateOnly(2026, 7, 14));

        var summary = await _sut.ImportOrderAsync(Context, SingleItemEmail, new DateOnly(2026, 7, 14));

        Assert.Equal(0, summary.ItemsAdded);
        Assert.Equal(1, summary.DuplicatesSkipped);
        var count = await Context.AmazonOrderItems.CountAsync(i => i.OrderId == "113-5254486-7378657");
        Assert.Equal(1, count); // still just one row, not two
    }

    [Fact]
    public async Task ImportRefund_MatchingOrderItem_UpdatesRefundAmount()
    {
        Context.AmazonOrderItems.Add(new AmazonOrderItem
        {
            OrderId = "112-1510135-3538618",
            OrderDate = new DateOnly(2026, 6, 1),
            ItemTitle = "MOS Cardstock Paper - 11\" x 14\"",
            Price = 21.99m,
            Quantity = 1,
            TaxAllocated = 1.32m,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var summary = await _sut.ImportRefundAsync(Context, RefundEmail);

        Assert.Equal(1, summary.RefundsApplied);
        var item = await Context.AmazonOrderItems.SingleAsync(i => i.OrderId == "112-1510135-3538618");
        Assert.Equal(23.31m, item.RefundAmount);
    }

    [Fact]
    public async Task ImportRefund_NoMatchingOrderItem_IsReportedNotErrored()
    {
        var summary = await _sut.ImportRefundAsync(Context, RefundEmail);

        Assert.Equal(0, summary.RefundsApplied);
        Assert.Contains(summary.UnmatchedRefunds, u => u.Contains("112-1510135-3538618"));
    }
}
