using Expense.Domain.Entities;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Entities;

public class MerchantRuleTests : DatabaseTestBase
{
    [Fact]
    public async Task MerchantRule_SavedAndReloaded_RoundTripsWithItsCategory()
    {
        var category = new Category { Name = "Groceries" };
        Context.Categories.Add(category);
        await Context.SaveChangesAsync();

        var rule = new MerchantRule { MerchantPattern = "%KROGER%", CategoryId = category.Id };
        Context.MerchantRules.Add(rule);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.MerchantRules
            .Include(r => r.Category)
            .SingleAsync(r => r.Id == rule.Id);

        Assert.Equal("%KROGER%", reloaded.MerchantPattern);
        Assert.Equal("Groceries", reloaded.Category.Name);
        Assert.Null(reloaded.Direction);
    }

    [Fact]
    public async Task MerchantRule_Direction_RoundTrips()
    {
        var category = new Category { Name = "Piano" };
        Context.Categories.Add(category);
        await Context.SaveChangesAsync();

        Context.MerchantRules.AddRange(
            new MerchantRule { MerchantPattern = "VENMO", CategoryId = category.Id, Direction = Direction.Income },
            new MerchantRule { MerchantPattern = "VENMO", CategoryId = category.Id, Direction = Direction.Expense });
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.MerchantRules
            .Where(r => r.MerchantPattern == "VENMO")
            .OrderBy(r => r.Id)
            .ToListAsync();

        Assert.Equal(Direction.Income, reloaded[0].Direction);
        Assert.Equal(Direction.Expense, reloaded[1].Direction);
    }
}
