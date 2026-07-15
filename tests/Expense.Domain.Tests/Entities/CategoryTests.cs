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
    public async Task Category_InsertedViaRawSqlWithNoExplicitIsActive_DefaultsToActiveAtTheDatabaseLevel()
    {
        // Regression guard: the C# object initializer's `= true` only applies when EF
        // constructs the object in memory - it does NOT become the database column's
        // own default. A prior migration used `defaultValue: false` (the raw CLR bool
        // default) instead of true, which silently flipped every existing category to
        // inactive when applied to the real database. This test bypasses the C# object
        // initializer entirely (raw SQL, exactly what happens to a pre-existing row when
        // a new column is added) so it actually exercises the column-level default.
        await Context.Database.ExecuteSqlRawAsync(
            "INSERT INTO categories (name, is_budgeted) VALUES ('Raw Insert Test', true)");

        var reloaded = await Context.Categories.SingleAsync(c => c.Name == "Raw Insert Test");
        Assert.True(reloaded.IsActive);
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
