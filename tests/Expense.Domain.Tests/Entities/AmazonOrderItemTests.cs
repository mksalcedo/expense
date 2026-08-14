using Expense.Domain.Entities;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Entities;

public class AmazonOrderItemTests : DatabaseTestBase
{
    [Fact]
    public async Task Item_SavedAndReloaded_RoundTripsCorrectly()
    {
        var item = new AmazonOrderItem
        {
            OrderId = "113-4492181-5586630",
            OrderDate = new DateOnly(2026, 7, 12),
            ItemTitle = "Pure Encapsulations Vitamin D3 125 mcg",
            Price = 21.00m,
            Quantity = 1,
            TaxAllocated = 1.26m,
            CreatedAt = DateTimeOffset.UtcNow
        };
        Context.AmazonOrderItems.Add(item);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.AmazonOrderItems.SingleAsync(i => i.Id == item.Id);

        Assert.Equal("113-4492181-5586630", reloaded.OrderId);
        Assert.Equal(21.00m, reloaded.Price);
        Assert.Equal(1.26m, reloaded.TaxAllocated);
    }

    [Fact]
    public async Task UnknownProduct_IsPendingCategorization()
    {
        var item = new AmazonOrderItem
        {
            OrderId = "113-9999999-1111111",
            OrderDate = new DateOnly(2026, 7, 14),
            ItemTitle = "Some Brand New Supplement Nobody Has Bought Before",
            Price = 24.99m,
            Quantity = 1,
            TaxAllocated = 0m,
            ProductId = null,
            CreatedAt = DateTimeOffset.UtcNow
        };
        Context.AmazonOrderItems.Add(item);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var pending = await reloadContext.AmazonOrderItems
            .Where(i => i.ProductId == null)
            .ToListAsync();

        Assert.Contains(pending, i => i.Id == item.Id);
    }

    [Fact]
    public async Task TaxProration_SumsBackToOrderTotal()
    {
        // Mirrors the real $46.43 order example from design-summary.md:
        // Magnesium $24.99 + Fish Oil (n/a here) vs the two-item Vitamin D3/Cardio-Plus
        // order: items $21.00 + $22.80 = $43.80, tax $2.63, grand total $46.43.
        var item1 = new AmazonOrderItem
        {
            OrderId = "113-4492181-5586630", OrderDate = new DateOnly(2026, 7, 12),
            ItemTitle = "Pure Encapsulations Vitamin D3", Price = 21.00m, Quantity = 1,
            TaxAllocated = 21.00m / 43.80m * 2.63m, CreatedAt = DateTimeOffset.UtcNow
        };
        var item2 = new AmazonOrderItem
        {
            OrderId = "113-4492181-5586630", OrderDate = new DateOnly(2026, 7, 12),
            ItemTitle = "Standard Process Cardio-Plus", Price = 22.80m, Quantity = 1,
            TaxAllocated = 22.80m / 43.80m * 2.63m, CreatedAt = DateTimeOffset.UtcNow
        };
        Context.AmazonOrderItems.AddRange(item1, item2);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var items = await reloadContext.AmazonOrderItems
            .Where(i => i.OrderId == "113-4492181-5586630")
            .ToListAsync();

        var total = items.Sum(i => i.Price + i.TaxAllocated);
        Assert.Equal(46.43m, Math.Round(total, 2));
    }

    [Fact]
    public async Task NeedsReviewItem_SavedAndReloaded_RoundTripsItsContextFields()
    {
        var item = new AmazonOrderItem
        {
            OrderId = "113-4355508-6173055", OrderDate = new DateOnly(2026, 7, 22),
            ItemTitle = "(Item details unavailable in email - check Amazon order page)", Price = 51.40m, Quantity = 1,
            TaxAllocated = 0m, NeedsReview = true, CreatedAt = DateTimeOffset.UtcNow,
            SourceMessageId = "19f8c33d6c1f4ca3",
            RawEmailBody = "Order #\n113-4355508-6173055\n\nGrand Total:\n51.4 USD",
            NeedsReviewReason = "No item detail in confirmation email",
            OrderDetailsUrl = "https://www.amazon.com/gp/css/order-details?orderId=113-4355508-6173055"
        };
        Context.AmazonOrderItems.Add(item);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.AmazonOrderItems.SingleAsync(i => i.Id == item.Id);

        Assert.Equal("19f8c33d6c1f4ca3", reloaded.SourceMessageId);
        Assert.Equal("Order #\n113-4355508-6173055\n\nGrand Total:\n51.4 USD", reloaded.RawEmailBody);
        Assert.Equal("No item detail in confirmation email", reloaded.NeedsReviewReason);
        Assert.Equal("https://www.amazon.com/gp/css/order-details?orderId=113-4355508-6173055", reloaded.OrderDetailsUrl);
    }

    [Fact]
    public async Task NormalItemizedItem_LeavesNewContextFieldsNull()
    {
        var item = new AmazonOrderItem
        {
            OrderId = "113-5254486-7378657", OrderDate = new DateOnly(2026, 7, 14),
            ItemTitle = "Qunol Ultra CoQ10", Price = 29.97m, Quantity = 1,
            TaxAllocated = 1.80m, CreatedAt = DateTimeOffset.UtcNow
        };
        Context.AmazonOrderItems.Add(item);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.AmazonOrderItems.SingleAsync(i => i.Id == item.Id);

        Assert.Null(reloaded.SourceMessageId);
        Assert.Null(reloaded.RawEmailBody);
        Assert.Null(reloaded.NeedsReviewReason);
        Assert.Null(reloaded.OrderDetailsUrl);
    }

    // Real bug this guards (found live 2026-08-14): two processes both running the
    // scheduled Amazon sync at once let two NeedsReview placeholders for the same order
    // through - the application-level dedup check has no way to see another connection's
    // uncommitted insert. A database-level constraint closes the class of bug regardless
    // of what causes the overlap.
    [Fact]
    public async Task TwoNeedsReviewPlaceholders_ForTheSameOrder_ViolatesAUniqueConstraint()
    {
        Context.AmazonOrderItems.Add(new AmazonOrderItem
        {
            OrderId = "113-1846569-0253060", OrderDate = new DateOnly(2026, 8, 13),
            ItemTitle = "(Item details unavailable in email - check Amazon order page)", Price = 99.85m,
            Quantity = 1, TaxAllocated = 0m, NeedsReview = true, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        Context.AmazonOrderItems.Add(new AmazonOrderItem
        {
            OrderId = "113-1846569-0253060", OrderDate = new DateOnly(2026, 8, 13),
            ItemTitle = "(Item details unavailable in email - check Amazon order page)", Price = 99.85m,
            Quantity = 1, TaxAllocated = 0m, NeedsReview = true, CreatedAt = DateTimeOffset.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task MultipleRealItems_OnTheSameOrder_AreNotBlockedByTheNeedsReviewConstraint()
    {
        Context.AmazonOrderItems.Add(new AmazonOrderItem
        {
            OrderId = "113-4492181-5586630", OrderDate = new DateOnly(2026, 7, 12),
            ItemTitle = "Pure Encapsulations Vitamin D3", Price = 21.00m, Quantity = 1,
            TaxAllocated = 1.26m, NeedsReview = false, CreatedAt = DateTimeOffset.UtcNow
        });
        Context.AmazonOrderItems.Add(new AmazonOrderItem
        {
            OrderId = "113-4492181-5586630", OrderDate = new DateOnly(2026, 7, 12),
            ItemTitle = "Standard Process Cardio-Plus", Price = 22.80m, Quantity = 1,
            TaxAllocated = 1.37m, NeedsReview = false, CreatedAt = DateTimeOffset.UtcNow
        });

        await Context.SaveChangesAsync();

        Assert.Equal(2, await Context.AmazonOrderItems.CountAsync(i => i.OrderId == "113-4492181-5586630"));
    }

    [Fact]
    public async Task RefundedItem_TracksRefundAmountOnTheSameRow()
    {
        var item = new AmazonOrderItem
        {
            OrderId = "112-1804427-3455403", OrderDate = new DateOnly(2026, 1, 20),
            ItemTitle = "CM300 Coffee Filter Basket", Price = 14.30m, Quantity = 1,
            TaxAllocated = 0m, RefundAmount = 14.30m, CreatedAt = DateTimeOffset.UtcNow
        };
        Context.AmazonOrderItems.Add(item);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.AmazonOrderItems.SingleAsync(i => i.Id == item.Id);

        Assert.Equal(14.30m, reloaded.RefundAmount);
    }
}
