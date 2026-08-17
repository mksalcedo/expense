using Expense.Domain.Entities;
using Expense.Domain.Services.Forecast;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.Forecast;

public class PaymentAmountAdjustmentServiceTests : DatabaseTestBase
{
    private readonly PaymentAmountAdjustmentService _sut = new();

    private async Task<Account> CreateAccountAsync()
    {
        var account = new Account { Name = "Amex", Type = AccountType.ActiveSpending };
        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task CreateAsync_PersistsANewAdjustment()
    {
        var account = await CreateAccountAsync();
        var category = new Category { Name = "Gas (utility)" };
        Context.Categories.Add(category);
        await Context.SaveChangesAsync();

        var adjustment = await _sut.CreateAsync(Context, account.Id, category.Id, new DateOnly(2026, 8, 28), -70.31m);

        var reloaded = await Context.PaymentAmountAdjustments.SingleAsync(a => a.Id == adjustment.Id);
        Assert.Equal(account.Id, reloaded.AccountId);
        Assert.Equal(category.Id, reloaded.CategoryId);
        Assert.Equal(new DateOnly(2026, 8, 28), reloaded.OriginalDate);
        Assert.Equal(-70.31m, reloaded.Amount);
    }

    [Fact]
    public async Task RemoveAsync_DeletesTheAdjustment()
    {
        var account = await CreateAccountAsync();
        var adjustment = await _sut.CreateAsync(Context, account.Id, null, new DateOnly(2026, 8, 28), -70.31m);

        await _sut.RemoveAsync(Context, adjustment.Id);

        Assert.Empty(await Context.PaymentAmountAdjustments.ToListAsync());
    }
}
