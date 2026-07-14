using Expense.Domain.Entities;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Entities;

public class BudgetPeriodTests : DatabaseTestBase
{
    [Fact]
    public async Task BudgetPeriod_SavedAndReloaded_RoundTripsCorrectly()
    {
        var groceries = new Category { Name = "Groceries", IsBudgeted = true };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();

        var period = new BudgetPeriod
        {
            CategoryId = groceries.Id,
            Amount = 450m,
            Frequency = Frequency.Weekly,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            EffectiveThrough = null
        };
        Context.BudgetPeriods.Add(period);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.BudgetPeriods.SingleAsync(p => p.Id == period.Id);

        Assert.Equal(450m, reloaded.Amount);
        Assert.Equal(Frequency.Weekly, reloaded.Frequency);
        Assert.Null(reloaded.EffectiveThrough);
    }

    [Fact]
    public async Task DifferentCategories_CanUseDifferentFrequencies()
    {
        var groceries = new Category { Name = "Groceries", IsBudgeted = true };
        var gas = new Category { Name = "Gas", IsBudgeted = true };
        Context.Categories.AddRange(groceries, gas);
        await Context.SaveChangesAsync();

        Context.BudgetPeriods.AddRange(
            new BudgetPeriod { CategoryId = groceries.Id, Amount = 450m, Frequency = Frequency.Weekly, EffectiveFrom = new DateOnly(2026, 1, 1) },
            new BudgetPeriod { CategoryId = gas.Id, Amount = 60m, Frequency = Frequency.Monthly, EffectiveFrom = new DateOnly(2026, 1, 1) }
        );
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var groceriesBudget = await reloadContext.BudgetPeriods.SingleAsync(p => p.CategoryId == groceries.Id);
        var gasBudget = await reloadContext.BudgetPeriods.SingleAsync(p => p.CategoryId == gas.Id);

        Assert.Equal(Frequency.Weekly, groceriesBudget.Frequency);
        Assert.Equal(Frequency.Monthly, gasBudget.Frequency);
    }

    [Fact]
    public async Task HistoricalReport_FindsTheBudgetAmountInEffectAtASpecificDate()
    {
        var supplements = new Category { Name = "Supplements", IsBudgeted = true };
        Context.Categories.Add(supplements);
        await Context.SaveChangesAsync();

        // Matches the real progression from the old spreadsheet: $115 -> $130 -> $142/week
        Context.BudgetPeriods.AddRange(
            new BudgetPeriod { CategoryId = supplements.Id, Amount = 115m, Frequency = Frequency.Weekly, EffectiveFrom = new DateOnly(2024, 1, 1), EffectiveThrough = new DateOnly(2024, 6, 30) },
            new BudgetPeriod { CategoryId = supplements.Id, Amount = 130m, Frequency = Frequency.Weekly, EffectiveFrom = new DateOnly(2024, 7, 1), EffectiveThrough = new DateOnly(2024, 9, 30) },
            new BudgetPeriod { CategoryId = supplements.Id, Amount = 142m, Frequency = Frequency.Weekly, EffectiveFrom = new DateOnly(2024, 10, 1), EffectiveThrough = null }
        );
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var asOfAugust = new DateOnly(2024, 8, 15);
        var inEffect = await reloadContext.BudgetPeriods
            .Where(p => p.CategoryId == supplements.Id)
            .Where(p => p.EffectiveFrom <= asOfAugust && (p.EffectiveThrough == null || p.EffectiveThrough >= asOfAugust))
            .SingleAsync();

        Assert.Equal(130m, inEffect.Amount);
    }
}
