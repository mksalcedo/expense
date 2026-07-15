using Expense.Domain.Entities;
using Expense.Domain.Services.OneTimeEvents;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.OneTimeEvents;

public class OneTimeEventManagementServiceTests : DatabaseTestBase
{
    private readonly OneTimeEventManagementService _sut = new();

    private async Task<Account> CreateCheckingAccountAsync()
    {
        var account = new Account { Name = "Wells Fargo Checking", Type = AccountType.Checking };
        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task CreateEventAsync_CreatesTheEvent()
    {
        var checking = await CreateCheckingAccountAsync();

        var evt = await _sut.CreateEventAsync(Context, "Property Tax", 2300m, Direction.Expense, new DateOnly(2026, 11, 1), checking.Id);

        var reloaded = await Context.OneTimeEvents.SingleAsync(e => e.Id == evt.Id);
        Assert.Equal("Property Tax", reloaded.Name);
        Assert.Equal(2300m, reloaded.Amount);
        Assert.Equal(Direction.Expense, reloaded.Direction);
        Assert.Equal(new DateOnly(2026, 11, 1), reloaded.Date);
        Assert.Equal(checking.Id, reloaded.AccountId);
    }

    [Fact]
    public async Task UpdateEventAsync_SavesAllFieldsTogether()
    {
        var checking = await CreateCheckingAccountAsync();
        var otherAccount = new Account { Name = "Other Checking", Type = AccountType.Checking };
        Context.Accounts.Add(otherAccount);
        await Context.SaveChangesAsync();
        var evt = await _sut.CreateEventAsync(Context, "Property Tax", 2300m, Direction.Expense, new DateOnly(2026, 11, 1), checking.Id);

        await _sut.UpdateEventAsync(Context, evt.Id, "Property Tax (revised)", 2450m, Direction.Expense, new DateOnly(2026, 11, 15), otherAccount.Id);

        var reloaded = await Context.OneTimeEvents.SingleAsync(e => e.Id == evt.Id);
        Assert.Equal("Property Tax (revised)", reloaded.Name);
        Assert.Equal(2450m, reloaded.Amount);
        Assert.Equal(new DateOnly(2026, 11, 15), reloaded.Date);
        Assert.Equal(otherAccount.Id, reloaded.AccountId);
    }

    [Fact]
    public async Task DeleteEventAsync_RemovesItEntirely()
    {
        var checking = await CreateCheckingAccountAsync();
        var evt = await _sut.CreateEventAsync(Context, "HVAC repair", 850m, Direction.Expense, new DateOnly(2026, 7, 20), checking.Id);

        await _sut.DeleteEventAsync(Context, evt.Id);

        Assert.False(await Context.OneTimeEvents.AnyAsync(e => e.Id == evt.Id));
    }
}
