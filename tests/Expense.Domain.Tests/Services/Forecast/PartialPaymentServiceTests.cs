using Expense.Domain.Entities;
using Expense.Domain.Services.Forecast;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.Forecast;

public class PartialPaymentServiceTests : DatabaseTestBase
{
    private readonly PartialPaymentService _sut = new();

    private async Task<Account> CreateAccountAsync()
    {
        var account = new Account { Name = "Wells Fargo Checking", Type = AccountType.Checking };
        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task CreateAsync_ForAnExpense_RecordsTheRealCashMovementAsAnExpenseOneTimeEvent()
    {
        var account = await CreateAccountAsync();

        var partialPayment = await _sut.CreateAsync(Context, account.Id, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 14), 1000m, Direction.Expense, "Amex Payment");

        var oneTimeEvent = await Context.OneTimeEvents.SingleAsync(e => e.Id == partialPayment.OneTimeEventId);
        Assert.Equal(Direction.Expense, oneTimeEvent.Direction);
        Assert.Equal(1000m, oneTimeEvent.Amount);
    }

    // Real bug this guards (found live 2026-08-04, user-identified): the synthetic "real cash
    // movement" record was always hardcoded Direction.Expense, regardless of what was actually
    // being recorded - using this for a real partial *income* payment (Piano) would have
    // silently injected a fake negative expense line into the forecast, real money never left.
    [Fact]
    public async Task CreateAsync_ForIncome_RecordsTheRealCashMovementAsAnIncomeOneTimeEvent()
    {
        var account = await CreateAccountAsync();

        var partialPayment = await _sut.CreateAsync(Context, account.Id, new DateOnly(2026, 8, 5), new DateOnly(2026, 7, 22), 429m, Direction.Income, "Piano");

        var oneTimeEvent = await Context.OneTimeEvents.SingleAsync(e => e.Id == partialPayment.OneTimeEventId);
        Assert.Equal(Direction.Income, oneTimeEvent.Direction);
        Assert.Equal(429m, oneTimeEvent.Amount);
    }

    // Real bug this guards (found live 2026-09-05): the synthetic OneTimeEvent was always
    // named after the ACCOUNT ("{account.Name} Payment (partial)") - correct for a debt/Amex
    // payment (the account genuinely IS what's being paid), but nonsensical for a Direct
    // category like Piano, which is paid into the plain checking account itself: recording a
    // real $95 Piano payment produced "Wells Fargo Checking Payment (partial)", not "Piano
    // (partial)". The name must come from the forecast line's own description instead.
    [Fact]
    public async Task CreateAsync_NamesTheSyntheticEvent_AfterTheLinesOwnDescription_NotTheAccount()
    {
        var account = await CreateAccountAsync();

        var partialPayment = await _sut.CreateAsync(Context, account.Id, new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 1), 95m, Direction.Income, "Piano");

        var oneTimeEvent = await Context.OneTimeEvents.SingleAsync(e => e.Id == partialPayment.OneTimeEventId);
        Assert.Equal("Piano (partial)", oneTimeEvent.Name);
    }

    [Fact]
    public async Task CreateAsync_PersistsThePartialPaymentRow()
    {
        var account = await CreateAccountAsync();

        var partialPayment = await _sut.CreateAsync(Context, account.Id, new DateOnly(2026, 8, 5), new DateOnly(2026, 7, 22), 429m, Direction.Income, "Piano");

        var reloaded = await Context.PartialPayments.SingleAsync(p => p.Id == partialPayment.Id);
        Assert.Equal(account.Id, reloaded.AccountId);
        Assert.Equal(new DateOnly(2026, 8, 5), reloaded.OriginalDate);
        Assert.Equal(new DateOnly(2026, 7, 22), reloaded.PaidDate);
        Assert.Equal(429m, reloaded.Amount);
    }

    [Fact]
    public async Task RemoveAsync_DeletesBothTheParialPaymentAndItsOneTimeEvent()
    {
        var account = await CreateAccountAsync();
        var partialPayment = await _sut.CreateAsync(Context, account.Id, new DateOnly(2026, 8, 5), new DateOnly(2026, 7, 22), 429m, Direction.Income, "Piano");

        await _sut.RemoveAsync(Context, partialPayment.Id);

        Assert.Empty(await Context.PartialPayments.ToListAsync());
        Assert.Empty(await Context.OneTimeEvents.ToListAsync());
    }
}
