using Expense.Domain.Entities;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Entities;

public class CategoryTests : DatabaseTestBase
{
    [Fact]
    public async Task Category_SavedAndReloaded_RoundTripsCorrectly()
    {
        var category = new Category { Name = "Groceries", IsBudgeted = true };
        Context.Categories.Add(category);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.Categories.SingleAsync(c => c.Id == category.Id);

        Assert.Equal("Groceries", reloaded.Name);
        Assert.True(reloaded.IsBudgeted);
        Assert.True(reloaded.IsActive); // defaults to active
    }

    [Fact]
    public async Task Category_NameMustBeUnique()
    {
        Context.Categories.Add(new Category { Name = "Restaurants", IsBudgeted = true });
        await Context.SaveChangesAsync();

        Context.Categories.Add(new Category { Name = "Restaurants", IsBudgeted = false });

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }
}
