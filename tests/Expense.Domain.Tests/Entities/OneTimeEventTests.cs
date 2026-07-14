using Expense.Domain.Entities;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Entities;

public class OneTimeEventTests : DatabaseTestBase
{
    [Fact]
    public async Task Event_SavedAndReloaded_RoundTripsCorrectly()
    {
        var checking = new Account { Name = "Wells Fargo Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var evt = new OneTimeEvent
        {
            Name = "Property Tax",
            Amount = 2300m,
            Direction = Direction.Expense,
            Date = new DateOnly(2026, 11, 1),
            AccountId = checking.Id
        };
        Context.OneTimeEvents.Add(evt);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.OneTimeEvents.SingleAsync(e => e.Id == evt.Id);

        Assert.Equal("Property Tax", reloaded.Name);
        Assert.Equal(2300m, reloaded.Amount);
        Assert.Equal(Direction.Expense, reloaded.Direction);
        Assert.Equal(new DateOnly(2026, 11, 1), reloaded.Date);
    }
}
