using Expense.Domain.Entities;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Entities;

public class AccountTests : DatabaseTestBase
{
    [Fact]
    public async Task DebtAccount_SavedAndReloaded_RoundTripsCorrectly()
    {
        var account = new Account
        {
            Name = "Discover",
            Type = AccountType.Debt,
            MinPayment = 173m,
            ExtraPayment = 0m,
            PaymentDueDay = 25
        };
        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.Accounts.SingleAsync(a => a.Id == account.Id);

        Assert.Equal("Discover", reloaded.Name);
        Assert.Equal(AccountType.Debt, reloaded.Type);
        Assert.Equal(173m, reloaded.MinPayment);
        Assert.Equal(0m, reloaded.ExtraPayment);
        Assert.Equal(25, reloaded.PaymentDueDay);
        Assert.Null(reloaded.StatementCloseDay);
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task AmexAccount_HasStatementCloseDay_RoundTripsCorrectly()
    {
        var account = new Account
        {
            Name = "Amex",
            Type = AccountType.ActiveSpending,
            ExtraPayment = 1100m,
            PaymentDueDay = 18,
            StatementCloseDay = 24
        };
        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.Accounts.SingleAsync(a => a.Id == account.Id);

        Assert.Equal(24, reloaded.StatementCloseDay);
        Assert.Equal(18, reloaded.PaymentDueDay);
        Assert.Equal(1100m, reloaded.ExtraPayment);
    }

    [Fact]
    public async Task Account_Deactivated_IsNotHardDeleted()
    {
        var account = new Account { Name = "SoFi", Type = AccountType.Debt };
        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();

        account.IsActive = false;
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.Accounts.SingleAsync(a => a.Id == account.Id);
        Assert.False(reloaded.IsActive);
    }
}
