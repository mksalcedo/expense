using Expense.Domain.Entities;
using Expense.Domain.Services.Forecast;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.Forecast;

public class PaymentDeferralServiceTests : DatabaseTestBase
{
    private readonly PaymentDeferralService _sut = new();

    private async Task<Account> CreateAccountAsync()
    {
        var account = new Account { Name = "Amex", Type = AccountType.ActiveSpending };
        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task CreateAsync_PersistsANewDeferral()
    {
        var account = await CreateAccountAsync();

        var deferral = await _sut.CreateAsync(
            Context, account.Id, new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 22), "waiting on paycheck");

        var reloaded = await Context.PaymentDeferrals.SingleAsync(d => d.Id == deferral.Id);
        Assert.Equal(account.Id, reloaded.AccountId);
        Assert.Equal(new DateOnly(2026, 8, 20), reloaded.OriginalDate);
        Assert.Equal(new DateOnly(2026, 8, 22), reloaded.DeferredToDate);
        Assert.Equal("waiting on paycheck", reloaded.Note);
    }

    [Fact]
    public async Task RemoveAsync_DeletesTheDeferral()
    {
        var account = await CreateAccountAsync();
        var deferral = await _sut.CreateAsync(Context, account.Id, new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 22), null);

        await _sut.RemoveAsync(Context, deferral.Id);

        Assert.Empty(await Context.PaymentDeferrals.ToListAsync());
    }
}
