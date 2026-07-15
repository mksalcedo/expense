using Expense.Domain.Entities;
using Expense.Domain.Seed;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Seed;

public class DbSeederTests : DatabaseTestBase
{
    private readonly DbSeeder _sut = new();

    [Fact]
    public async Task SeedAsync_CreatesTheFiveStartingCategories()
    {
        await _sut.SeedAsync(Context);

        var categories = await Context.Categories
            .Where(c => c.Name == "Groceries" || c.Name == "Restaurants" || c.Name == "Supplements"
                        || c.Name == "Gas" || c.Name == "Off-Budget/Misc")
            .ToListAsync();

        Assert.Equal(5, categories.Count);
    }

    [Fact]
    public async Task SeedAsync_SetsPayInFullAmex_OnExactlyTheFourSpendingCategories()
    {
        await _sut.SeedAsync(Context);

        var payInFullCategoryNames = await Context.FundingRules
            .Where(r => r.Strategy == FundingStrategies.PayInFullAmex)
            .Select(r => r.Category.Name)
            .ToListAsync();

        Assert.Equal(4, payInFullCategoryNames.Count);
        Assert.Contains("Groceries", payInFullCategoryNames);
        Assert.Contains("Restaurants", payInFullCategoryNames);
        Assert.Contains("Supplements", payInFullCategoryNames);
        Assert.Contains("Gas", payInFullCategoryNames);
    }

    [Fact]
    public async Task SeedAsync_CreatesElevenAccounts_IncludingCheckingAndAmex()
    {
        await _sut.SeedAsync(Context);

        var accounts = await Context.Accounts.ToListAsync();

        Assert.Equal(11, accounts.Count);
        Assert.Contains(accounts, a => a.Name == "Wells Fargo Checking" && a.Type == AccountType.Checking);
        Assert.Contains(accounts, a => a.Name == "Amex" && a.Type == AccountType.ActiveSpending && a.ExtraPayment == 1100m);

        var debtAccountNames = new[]
        {
            "Discover", "Chase Sapphire Reserve", "Chase Amazon Prime Visa", "Chase Credit Card",
            "Wells Fargo Cash Back Visa", "Wells Fargo Personal LOC", "Apple Card", "SoFi", "Venmo Credit Card"
        };
        foreach (var name in debtAccountNames)
        {
            Assert.Contains(accounts, a => a.Name == name && a.Type == AccountType.Debt);
        }
    }

    [Fact]
    public async Task SeedAsync_CreatesAmexMerchantRule_RoutingToAmexPaymentCategory()
    {
        await _sut.SeedAsync(Context);

        var amexPaymentCategory = await Context.Categories.SingleAsync(c => c.Name == "Amex Payment");
        var rule = await Context.MerchantRules.SingleAsync(r => r.CategoryId == amexPaymentCategory.Id);

        Assert.Contains("AMERICAN EXPRESS", rule.MerchantPattern.ToUpperInvariant());
    }

    [Fact]
    public async Task SeedAsync_CreatesGiftCardProductRule_RoutingToOffBudgetMisc()
    {
        await _sut.SeedAsync(Context);

        var offBudget = await Context.Categories.SingleAsync(c => c.Name == "Off-Budget/Misc");
        var product = await Context.Products.SingleAsync(p => p.ProductPattern == "%GIFT CARD%");

        Assert.Equal(offBudget.Id, product.CategoryId);
    }

    [Fact]
    public async Task SeedAsync_RunTwice_IsIdempotent()
    {
        await _sut.SeedAsync(Context);
        await _sut.SeedAsync(Context);

        var categoryCount = await Context.Categories.CountAsync();
        var accountCount = await Context.Accounts.CountAsync();

        Assert.Equal(15, categoryCount); // 5 starting + 10 debt/Amex payment categories
        Assert.Equal(11, accountCount);
    }
}
