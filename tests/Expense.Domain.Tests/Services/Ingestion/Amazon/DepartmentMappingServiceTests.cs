using Expense.Domain.Entities;
using Expense.Domain.Services.Ingestion.Amazon;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.Ingestion.Amazon;

public class DepartmentMappingServiceTests : DatabaseTestBase
{
    private readonly DepartmentMappingService _sut = new();

    private async Task<(Category Supplements, Category Misc)> SeedCategoriesAsync()
    {
        var supplements = new Category { Name = "Supplements" };
        var misc = new Category { Name = "Off-Budget/Misc" };
        Context.Categories.AddRange(supplements, misc);
        await Context.SaveChangesAsync();
        return (supplements, misc);
    }

    private AmazonOrderItem Placeholder(string orderId, string? hint) => new()
    {
        OrderId = orderId,
        OrderDate = new DateOnly(2026, 8, 20),
        ItemTitle = "(Item details unavailable in email - check Amazon order page)",
        Price = 25m,
        Quantity = 1,
        NeedsReview = true,
        DepartmentHint = hint,
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task UpsertAndReapply_NewMapping_AutoCategorizesMatchingPendingPlaceholders()
    {
        var (_, misc) = await SeedCategoriesAsync();
        Context.AmazonOrderItems.AddRange(
            Placeholder("113-1111111-1111111", "Kitchen"),
            Placeholder("113-2222222-2222222", "1 Kitchen"),
            Placeholder("113-3333333-3333333", "Garden & Outdoor")); // not covered by the new mapping
        await Context.SaveChangesAsync();

        var reapplied = await _sut.UpsertAndReapplyAsync(Context, "Kitchen", misc.Id);

        Assert.Equal(2, reapplied);
        var kitchen1 = await Context.AmazonOrderItems.SingleAsync(i => i.OrderId == "113-1111111-1111111");
        Assert.Equal(misc.Id, kitchen1.CategoryId);
        Assert.False(kitchen1.NeedsReview);
        Assert.Equal("Amazon order — Off-Budget/Misc", kitchen1.ItemTitle);

        var garden = await Context.AmazonOrderItems.SingleAsync(i => i.OrderId == "113-3333333-3333333");
        Assert.Null(garden.CategoryId);
        Assert.True(garden.NeedsReview);
    }

    [Fact]
    public async Task UpsertAndReapply_MixedHintStaysInReview_UntilBothDepartmentsAreMapped()
    {
        var (supplements, misc) = await SeedCategoriesAsync();
        Context.AmazonOrderItems.Add(Placeholder("113-4444444-4444444", "1 Home, 1 Nutrition & Wellness"));
        await Context.SaveChangesAsync();

        var afterFirst = await _sut.UpsertAndReapplyAsync(Context, "Nutrition & Wellness", supplements.Id);
        Assert.Equal(0, afterFirst); // "Home" still unmapped

        var afterSecond = await _sut.UpsertAndReapplyAsync(Context, "Home", supplements.Id);
        Assert.Equal(1, afterSecond); // both now map to Supplements -> resolves

        var item = await Context.AmazonOrderItems.SingleAsync(i => i.OrderId == "113-4444444-4444444");
        Assert.Equal(supplements.Id, item.CategoryId);
    }

    [Fact]
    public async Task UpsertAndReapply_MixedHint_TwoDifferentCategories_NeverAutoResolves()
    {
        var (supplements, misc) = await SeedCategoriesAsync();
        Context.AmazonOrderItems.Add(Placeholder("113-5555555-5555555", "3 Apparel, 1 Office"));
        await Context.SaveChangesAsync();

        await _sut.UpsertAndReapplyAsync(Context, "Apparel", supplements.Id);
        var reapplied = await _sut.UpsertAndReapplyAsync(Context, "Office", misc.Id);

        Assert.Equal(0, reapplied);
        var item = await Context.AmazonOrderItems.SingleAsync(i => i.OrderId == "113-5555555-5555555");
        Assert.Null(item.CategoryId);
        Assert.True(item.NeedsReview);
    }

    [Fact]
    public async Task Upsert_ExistingDepartment_RepointsItAtTheNewCategory()
    {
        var (supplements, misc) = await SeedCategoriesAsync();

        await _sut.UpsertAndReapplyAsync(Context, "Kitchen", supplements.Id);
        await _sut.UpsertAndReapplyAsync(Context, "kitchen", misc.Id); // different case, same department

        var mappings = await _sut.GetAllAsync(Context);
        var kitchen = Assert.Single(mappings, m => string.Equals(m.DepartmentName, "Kitchen", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(misc.Id, kitchen.CategoryId);
    }

    [Fact]
    public async Task Delete_RemovesTheMapping()
    {
        var (supplements, _) = await SeedCategoriesAsync();
        await _sut.UpsertAndReapplyAsync(Context, "Kitchen", supplements.Id);
        var mapping = Assert.Single(await _sut.GetAllAsync(Context));

        await _sut.DeleteAsync(Context, mapping.Id);

        Assert.Empty(await _sut.GetAllAsync(Context));
    }
}
