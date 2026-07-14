using Expense.Domain.Entities;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Entities;

public class FundingRuleTests : DatabaseTestBase
{
    [Fact]
    public async Task FundingRule_SavedAndReloaded_RoundTripsWithItsCategory()
    {
        var category = new Category { Name = "Groceries", IsBudgeted = true };
        Context.Categories.Add(category);
        await Context.SaveChangesAsync();

        var rule = new FundingRule { CategoryId = category.Id, Strategy = FundingStrategies.PayInFullAmex };
        Context.FundingRules.Add(rule);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.FundingRules
            .Include(r => r.Category)
            .SingleAsync(r => r.Id == rule.Id);

        Assert.Equal(FundingStrategies.PayInFullAmex, reloaded.Strategy);
        Assert.Equal("Groceries", reloaded.Category.Name);
    }

    [Fact]
    public async Task Query_PayInFullAmexCategories_NeverHardcodesNamesInTheQuery()
    {
        var groceries = new Category { Name = "Groceries", IsBudgeted = true };
        var restaurants = new Category { Name = "Restaurants", IsBudgeted = true };
        var amexPayment = new Category { Name = "Amex Payment", IsBudgeted = true };
        Context.Categories.AddRange(groceries, restaurants, amexPayment);
        await Context.SaveChangesAsync();

        Context.FundingRules.AddRange(
            new FundingRule { CategoryId = groceries.Id, Strategy = FundingStrategies.PayInFullAmex },
            new FundingRule { CategoryId = restaurants.Id, Strategy = FundingStrategies.PayInFullAmex },
            new FundingRule { CategoryId = amexPayment.Id, Strategy = FundingStrategies.None }
        );
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var amexFundedCategoryIds = await reloadContext.FundingRules
            .Where(r => r.Strategy == FundingStrategies.PayInFullAmex)
            .Select(r => r.CategoryId)
            .ToListAsync();

        Assert.Equal(2, amexFundedCategoryIds.Count);
        Assert.Contains(groceries.Id, amexFundedCategoryIds);
        Assert.Contains(restaurants.Id, amexFundedCategoryIds);
        Assert.DoesNotContain(amexPayment.Id, amexFundedCategoryIds);
    }
}
