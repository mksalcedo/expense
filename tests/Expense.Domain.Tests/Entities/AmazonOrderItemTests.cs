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
