using Expense.Domain.Entities;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Entities;

public class MerchantRuleTests : DatabaseTestBase
{
    [Fact]
    public async Task MerchantRule_SavedAndReloaded_RoundTripsWithItsCategory()
    {
        var category = new Category { Name = "Groceries", IsBudgeted = true };
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
    }
}
