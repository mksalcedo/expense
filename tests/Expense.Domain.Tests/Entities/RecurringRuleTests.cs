using Expense.Domain.Entities;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Entities;

public class RecurringRuleTests : DatabaseTestBase
{
    [Fact]
    public async Task BiweeklyPaycheck_SavedAndReloaded_RoundTripsCorrectly()
    {
        var checking = new Account { Name = "Wells Fargo Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var rule = new RecurringRule
        {
            Name = "EFX Paycheck",
            Direction = Direction.Income,
            Amount = 4588.87m,
            Frequency = Frequency.Biweekly,
            Anchor = new DateOnly(2026, 7, 10),
            AccountId = checking.Id,
            Active = true,
            StartDate = new DateOnly(2026, 1, 1)
        };
        Context.RecurringRules.Add(rule);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.RecurringRules.SingleAsync(r => r.Id == rule.Id);

        Assert.Equal(Direction.Income, reloaded.Direction);
        Assert.Equal(Frequency.Biweekly, reloaded.Frequency);
        Assert.Equal(new DateOnly(2026, 7, 10), reloaded.Anchor);
        Assert.Null(reloaded.EndDate);
    }

    [Fact]
    public async Task MonthlyFixedBill_UsesAnchorAsDayOfMonth()
    {
        var checking = new Account { Name = "Wells Fargo Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var rule = new RecurringRule
        {
            Name = "Truist Mortgage",
            Direction = Direction.Expense,
            Amount = 2681.22m,
            Frequency = Frequency.Monthly,
            Anchor = new DateOnly(2026, 1, 4), // 4th of the month
            AccountId = checking.Id,
            StartDate = new DateOnly(2026, 1, 1)
        };
        Context.RecurringRules.Add(rule);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.RecurringRules.SingleAsync(r => r.Id == rule.Id);

        Assert.Equal(4, reloaded.Anchor.Day);
        Assert.Equal(2681.22m, reloaded.Amount);
    }

    [Fact]
    public async Task InactiveRule_IsExcludedFromActiveOnlyQuery()
    {
        var checking = new Account { Name = "Wells Fargo Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        Context.RecurringRules.AddRange(
            new RecurringRule { Name = "Active Bill", Direction = Direction.Expense, Amount = 100m, Frequency = Frequency.Monthly, Anchor = new DateOnly(2026, 1, 1), AccountId = checking.Id, Active = true, StartDate = new DateOnly(2026, 1, 1) },
            new RecurringRule { Name = "Cancelled Bill", Direction = Direction.Expense, Amount = 50m, Frequency = Frequency.Monthly, Anchor = new DateOnly(2026, 1, 1), AccountId = checking.Id, Active = false, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1) }
        );
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var activeRules = await reloadContext.RecurringRules.Where(r => r.Active).ToListAsync();

        Assert.Single(activeRules);
        Assert.Equal("Active Bill", activeRules[0].Name);
    }
}
