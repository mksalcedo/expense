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

    private const string SimplifiedNoItemDetailEmail = """
        Amazon.com Order Confirmation
        www.amazon.com/ref=TE_simp_tex_h
        _______________________________________________________________________________________

        Hello Mark,

        Thank you for shopping with us.

        We'll send a confirmation when your item ships.

        View or manage your orders in Your Orders:
        https://www.amazon.com/gp/css/order-details?orderId=113-1132648-3403446&ref_=TE_simp_od

        Details
        Order #113-1132648-3403446

            Arriving:
            Thursday, Jul 17, 5 p.m. - 10 p.m.

            Ship to:
            Mark
            NORCROSS, GA

            Order Total: $22.00

        ======================================================================================
        We hope to see you again soon.

        Amazon.com
        """;

    private const string RefundEmail = """
        Hello, We're writing to let you know we processed your refund of $23.31 for your Order 112-1510135-3538618 from JFP Western Inc..

        This refund is for the following item(s):     Item: MOS Cardstock Paper - 11" x 14"     Quantity: 1     ASIN: B0DKB7SPSR     Reason for refund: Account adjustment     Here's the breakdown of your refund for this item:         Item Refund: $21.99         Item Tax Refund: $1.32
        """;

    [Fact]
    public async Task ImportOrder_NewOrder_AddsItemsAndAppliesProductMatch()
    {
        var supplements = new Category { Name = "Supplements" };
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
    public async Task ImportOrder_ReportsAnItemOutcome_ForEachItemAdded()
    {
        var summary = await _sut.ImportOrderAsync(Context, SingleItemEmail, new DateOnly(2026, 7, 14));

        var outcome = Assert.Single(summary.ItemOutcomes);
        Assert.Contains("Qunol Ultra CoQ10", outcome.ItemTitle);
        Assert.Equal(29.97m, outcome.Price);
        Assert.Equal(1, outcome.Quantity);
        Assert.False(outcome.WasDuplicate);
        Assert.False(outcome.NeedsReview);
    }

    [Fact]
    public async Task ImportOrder_ReportsAnItemOutcome_ForADuplicateToo()
    {
        await _sut.ImportOrderAsync(Context, SingleItemEmail, new DateOnly(2026, 7, 14));

        var summary = await _sut.ImportOrderAsync(Context, SingleItemEmail, new DateOnly(2026, 7, 14));

        var outcome = Assert.Single(summary.ItemOutcomes);
        Assert.Contains("Qunol Ultra CoQ10", outcome.ItemTitle);
        Assert.True(outcome.WasDuplicate);
    }

    [Fact]
    public async Task ImportOrder_ReportsAnItemOutcome_FlaggedNeedsReview_ForAPlaceholderItem()
    {
        var summary = await _sut.ImportOrderAsync(Context, SimplifiedNoItemDetailEmail, new DateOnly(2026, 7, 14));

        var outcome = Assert.Single(summary.ItemOutcomes);
        Assert.False(outcome.WasDuplicate);
        Assert.True(outcome.NeedsReview);
    }

    [Fact]
    public async Task ImportOrder_ReportsAnItemOutcome_FlaggedNeedsReview_ForAPlaceholderDuplicateToo()
    {
        await _sut.ImportOrderAsync(Context, SimplifiedNoItemDetailEmail, new DateOnly(2026, 7, 14));

        var summary = await _sut.ImportOrderAsync(Context, SimplifiedNoItemDetailEmail, new DateOnly(2026, 7, 14));

        var outcome = Assert.Single(summary.ItemOutcomes);
        Assert.True(outcome.WasDuplicate);
        Assert.True(outcome.NeedsReview);
    }

    [Fact]
    public async Task ImportOrder_PlaceholderItem_StillDedupesAfterTheUserEditsItsTitle()
    {
        // Real bug: a placeholder ("(Item details unavailable...)") item is meant to be
        // corrected by hand once the user checks the real order page - but the old dedup key
        // was (OrderId, ItemTitle), so editing the title made a later re-scan of the same
        // email fail to recognize it and insert a second, duplicate placeholder row.
        await _sut.ImportOrderAsync(Context, SimplifiedNoItemDetailEmail, new DateOnly(2026, 7, 14));
        var supplements = new Category { Name = "Supplements" };
        Context.Categories.Add(supplements);
        await Context.SaveChangesAsync();

        var item = await Context.AmazonOrderItems.SingleAsync(i => i.OrderId == "113-1132648-3403446");
        item.ItemTitle = "Real Product Name I Looked Up";
        item.CategoryId = supplements.Id;
        item.NeedsReview = false;
        await Context.SaveChangesAsync();

        var summary = await _sut.ImportOrderAsync(Context, SimplifiedNoItemDetailEmail, new DateOnly(2026, 7, 14));

        Assert.Equal(0, summary.ItemsAdded);
        Assert.Equal(1, summary.DuplicatesSkipped);
        var itemsForOrder = await Context.AmazonOrderItems.Where(i => i.OrderId == "113-1132648-3403446").ToListAsync();
        var onlyItem = Assert.Single(itemsForOrder); // still just one row, not two
        Assert.Equal("Real Product Name I Looked Up", onlyItem.ItemTitle); // the user's fix wasn't touched
        Assert.Equal(supplements.Id, onlyItem.CategoryId);
    }

    [Fact]
    public async Task ImportRefund_ProductMatch_CreatesItsOwnNegativeCategorizedEntry()
    {
        var officeSupplies = new Category { Name = "Office Supplies" };
        Context.Categories.Add(officeSupplies);
        await Context.SaveChangesAsync();
        Context.Products.Add(new Product { ProductPattern = "%CARDSTOCK%", CategoryId = officeSupplies.Id });
        await Context.SaveChangesAsync();

        var summary = await _sut.ImportRefundAsync(Context, RefundEmail, new DateOnly(2026, 7, 19));

        Assert.Equal(1, summary.RefundsApplied);
        var item = await Context.AmazonOrderItems.SingleAsync(i => i.OrderId == "112-1510135-3538618");
        Assert.Equal(-23.31m, item.Price);
        Assert.Equal(officeSupplies.Id, item.CategoryId);
        Assert.NotNull(item.ProductId);
    }

    [Fact]
    public async Task ImportRefund_ReportsAnItemOutcome()
    {
        var summary = await _sut.ImportRefundAsync(Context, RefundEmail, new DateOnly(2026, 7, 19));

        var outcome = Assert.Single(summary.ItemOutcomes);
        Assert.Contains("Cardstock", outcome.ItemTitle);
        Assert.Equal(-23.31m, outcome.Price);
        Assert.False(outcome.WasDuplicate);
    }

    [Fact]
    public async Task ImportRefund_NoMatchingProduct_LeavesItPendingCategorization_LikeAnyOtherItem()
    {
        var summary = await _sut.ImportRefundAsync(Context, RefundEmail, new DateOnly(2026, 7, 19));

        Assert.Equal(1, summary.RefundsApplied);
        var item = await Context.AmazonOrderItems.SingleAsync(i => i.OrderId == "112-1510135-3538618");
        Assert.Equal(-23.31m, item.Price);
        Assert.Null(item.ProductId);
        Assert.Null(item.CategoryId);
    }

    [Fact]
    public async Task ImportRefund_DoesNotRequireOrTouchAMatchingOriginalPurchase()
    {
        var original = new AmazonOrderItem
        {
            OrderId = "112-1510135-3538618",
            OrderDate = new DateOnly(2026, 6, 1),
            ItemTitle = "MOS Cardstock Paper - 11\" x 14\"",
            Price = 21.99m,
            Quantity = 1,
            TaxAllocated = 1.32m,
            CreatedAt = DateTimeOffset.UtcNow
        };
        Context.AmazonOrderItems.Add(original);
        await Context.SaveChangesAsync();

        await _sut.ImportRefundAsync(Context, RefundEmail, new DateOnly(2026, 7, 19));

        var reloadedOriginal = await Context.AmazonOrderItems.SingleAsync(i => i.Id == original.Id);
        Assert.Equal(21.99m, reloadedOriginal.Price); // untouched
        Assert.Null(reloadedOriginal.RefundAmount); // untouched - the refund is its own row now
        var refundRow = await Context.AmazonOrderItems.SingleAsync(i => i.Id != original.Id && i.OrderId == "112-1510135-3538618");
        Assert.Equal(-23.31m, refundRow.Price);
    }

    [Fact]
    public async Task ImportRefund_AlreadyImported_SkipsAsDuplicate()
    {
        await _sut.ImportRefundAsync(Context, RefundEmail, new DateOnly(2026, 7, 19));

        var summary = await _sut.ImportRefundAsync(Context, RefundEmail, new DateOnly(2026, 7, 19));

        Assert.Equal(0, summary.RefundsApplied);
        Assert.Equal(1, summary.RefundDuplicatesSkipped);
        var count = await Context.AmazonOrderItems.CountAsync(i => i.OrderId == "112-1510135-3538618");
        Assert.Equal(1, count); // still just one row, not two
    }

    [Fact]
    public async Task AddManualItem_CreatesTheItem_WithProductMatchApplied()
    {
        var supplements = new Category { Name = "Supplements" };
        Context.Categories.Add(supplements);
        await Context.SaveChangesAsync();
        Context.Products.Add(new Product { ProductPattern = "%QUNOL%", CategoryId = supplements.Id });
        await Context.SaveChangesAsync();

        var item = await _sut.AddManualItemAsync(Context, "113-MANUAL", new DateOnly(2026, 7, 18), "Qunol Ultra CoQ10 100mg", 29.97m, 1);

        var reloaded = await Context.AmazonOrderItems.SingleAsync(i => i.Id == item.Id);
        Assert.Equal("113-MANUAL", reloaded.OrderId);
        Assert.Equal(new DateOnly(2026, 7, 18), reloaded.OrderDate);
        Assert.Equal("Qunol Ultra CoQ10 100mg", reloaded.ItemTitle);
        Assert.Equal(29.97m, reloaded.Price);
        Assert.Equal(1, reloaded.Quantity);
        Assert.Equal(supplements.Id, reloaded.CategoryId);
        Assert.NotNull(reloaded.ProductId);
    }

    [Fact]
    public async Task AddManualItem_NoMatchingProduct_LeavesPendingCategorization()
    {
        var item = await _sut.AddManualItemAsync(Context, "113-MANUAL", new DateOnly(2026, 7, 18), "Some Unknown Thing", 10m, 2);

        var reloaded = await Context.AmazonOrderItems.SingleAsync(i => i.Id == item.Id);
        Assert.Null(reloaded.ProductId);
        Assert.Null(reloaded.CategoryId);
        Assert.Equal(2, reloaded.Quantity);
    }
}
