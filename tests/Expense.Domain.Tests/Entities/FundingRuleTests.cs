using Expense.Domain.Entities;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Entities;

public class FundingRuleTests : DatabaseTestBase
{
    [Fact]
    public async Task FundingRule_SavedAndReloaded_RoundTripsWithItsCategory()
    {
        var category = new Category { Name = "Groceries" };
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
        var groceries = new Category { Name = "Groceries" };
        var restaurants = new Category { Name = "Restaurants" };
        var amexPayment = new Category { Name = "Amex Payment" };
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

    [Fact]
    public async Task FundingRule_ForAnAccountPaymentCategory_LinksToItsAccount()
    {
        var discover = new Account { Name = "Discover", Type = AccountType.Debt };
        Context.Accounts.Add(discover);
        await Context.SaveChangesAsync();

        var category = new Category { Name = "Discover Payment" };
        Context.Categories.Add(category);
        await Context.SaveChangesAsync();

        var rule = new FundingRule { CategoryId = category.Id, Strategy = FundingStrategies.AccountPayment, AccountId = discover.Id };
        Context.FundingRules.Add(rule);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.FundingRules.Include(r => r.Account).SingleAsync(r => r.Id == rule.Id);

        Assert.Equal("Discover", reloaded.Account!.Name);
    }

    [Fact]
    public async Task FundingRule_ForAnOrdinaryCategory_HasNoLinkedAccount()
    {
        var category = new Category { Name = "Groceries" };
        Context.Categories.Add(category);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = category.Id, Strategy = FundingStrategies.PayInFullAmex });
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.FundingRules.SingleAsync(r => r.CategoryId == category.Id);

        Assert.Null(reloaded.AccountId);
    }
}
