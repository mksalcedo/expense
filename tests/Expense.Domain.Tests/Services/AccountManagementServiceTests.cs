using Expense.Domain.Entities;
using Expense.Domain.Services.Accounts;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services;

public class AccountManagementServiceTests : DatabaseTestBase
{
    private readonly AccountManagementService _sut = new();

    [Fact]
    public async Task CreateAccountAsync_ForADebtAccount_CreatesAccountCategoryFundingRuleAndMerchantRule_Together()
    {
        var account = await _sut.CreateAccountAsync(
            Context,
            name: "Discover",
            type: AccountType.Debt,
            minPayment: 173m);

        var category = await Context.Categories.SingleAsync(c => c.Name == "Discover Payment");
        var fundingRule = await Context.FundingRules.SingleAsync(r => r.CategoryId == category.Id);
        var merchantRule = await Context.MerchantRules.SingleAsync(r => r.CategoryId == category.Id);

        Assert.Equal(AccountType.Debt, account.Type);
        Assert.Equal(173m, account.MinPayment);
        Assert.True(category.IsActive);
        Assert.Equal(FundingStrategies.AccountPayment, fundingRule.Strategy);
        Assert.Equal(account.Id, fundingRule.AccountId);
        Assert.Contains("DISCOVER", merchantRule.MerchantPattern.ToUpperInvariant());
    }

    [Fact]
    public async Task CreateAccountAsync_ForAmex_SupportsActiveSpendingTypeAndExplicitMerchantPattern()
    {
        var account = await _sut.CreateAccountAsync(
            Context,
            name: "Amex",
            type: AccountType.ActiveSpending,
            extraPayment: 1100m,
            paymentDueDay: 18,
            statementCloseDay: 24,
            suggestedMerchantPattern: "%AMERICAN EXPRESS%");

        var category = await Context.Categories.SingleAsync(c => c.Name == "Amex Payment");
        var merchantRule = await Context.MerchantRules.SingleAsync(r => r.CategoryId == category.Id);

        Assert.Equal(AccountType.ActiveSpending, account.Type);
        Assert.Equal(24, account.StatementCloseDay);
        Assert.Equal(1100m, account.ExtraPayment);
        Assert.Equal("%AMERICAN EXPRESS%", merchantRule.MerchantPattern);
    }

    // A savings account has no "payment" concept - it's a manually-tracked asset balance shown
    // alongside the forecast, not a bill the forecast pays. Unlike Debt/ActiveSpending, creating
    // one must not spawn a "{name} Payment" category/funding rule/merchant rule, since none of
    // that machinery has any meaning here and would just clutter Categories with an unusable row.
    [Fact]
    public async Task CreateAccountAsync_ForASavingsAccount_CreatesNoCategoryFundingRuleOrMerchantRule()
    {
        var account = await _sut.CreateAccountAsync(Context, name: "Emergency Fund", type: AccountType.Savings);

        Assert.Equal(AccountType.Savings, account.Type);
        Assert.False(await Context.Categories.AnyAsync(c => c.Name == "Emergency Fund Payment"));
        Assert.Empty(await Context.FundingRules.ToListAsync());
        Assert.Empty(await Context.MerchantRules.ToListAsync());
    }

    [Fact]
    public async Task AddOrUpdateBalanceSnapshotAsync_WorksForASavingsAccount_JustLikeAnyOtherAccount()
    {
        var account = await _sut.CreateAccountAsync(Context, name: "Emergency Fund", type: AccountType.Savings);

        await _sut.AddOrUpdateBalanceSnapshotAsync(Context, account.Id, new DateOnly(2026, 8, 3), 1500m);

        var snapshot = await Context.DebtBalanceSnapshots.SingleAsync(s => s.AccountId == account.Id);
        Assert.Equal(1500m, snapshot.Balance);
    }

    [Fact]
    public async Task DeactivateAccountAsync_SoftDeletes_AndLeavesHistoricalTransactionsUntouched()
    {
        var account = await _sut.CreateAccountAsync(Context, name: "SoFi", type: AccountType.Debt);

        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 1, 1),
            Description = "SOFI PAYMENT", Amount = -1107.24m, ImportSource = "Manual",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        await _sut.DeactivateAccountAsync(Context, account.Id);

        var reloadedAccount = await Context.Accounts.SingleAsync(a => a.Id == account.Id);
        var historicalTransaction = await Context.BankTransactions.SingleAsync(t => t.AccountId == account.Id);

        Assert.False(reloadedAccount.IsActive);
        Assert.Equal(-1107.24m, historicalTransaction.Amount);
    }

    [Fact]
    public async Task ReactivateAccountAsync_UndoesADeactivation()
    {
        var account = await _sut.CreateAccountAsync(Context, name: "SoFi", type: AccountType.Debt);
        await _sut.DeactivateAccountAsync(Context, account.Id);

        await _sut.ReactivateAccountAsync(Context, account.Id);

        var reloaded = await Context.Accounts.SingleAsync(a => a.Id == account.Id);
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task UpdateAccountAsync_SavesNameAndPaymentFieldsTogether()
    {
        var account = await _sut.CreateAccountAsync(Context, name: "Discover", type: AccountType.Debt);

        await _sut.UpdateAccountAsync(Context, account.Id, "Discover It", minPayment: 173m, extraPayment: 50m,
            paymentDueDay: 3, statementCloseDay: null);

        var reloaded = await Context.Accounts.SingleAsync(a => a.Id == account.Id);
        Assert.Equal("Discover It", reloaded.Name);
        Assert.Equal(173m, reloaded.MinPayment);
        Assert.Equal(50m, reloaded.ExtraPayment);
        Assert.Equal(3, reloaded.PaymentDueDay);
        Assert.Null(reloaded.StatementCloseDay);
    }

    [Fact]
    public async Task UpdateAccountAsync_ForAmex_CanSetStatementCloseDay()
    {
        var account = await _sut.CreateAccountAsync(Context, name: "Amex", type: AccountType.ActiveSpending);

        await _sut.UpdateAccountAsync(Context, account.Id, "Amex", minPayment: null, extraPayment: 1100m,
            paymentDueDay: 18, statementCloseDay: 24);

        var reloaded = await Context.Accounts.SingleAsync(a => a.Id == account.Id);
        Assert.Equal(24, reloaded.StatementCloseDay);
        Assert.Equal(1100m, reloaded.ExtraPayment);
    }

    [Fact]
    public async Task UpdatePaymentFieldsAsync_UpdatesPaymentFieldsWithoutTouchingName()
    {
        var account = await _sut.CreateAccountAsync(Context, name: "Discover", type: AccountType.Debt, minPayment: 173m);

        await _sut.UpdatePaymentFieldsAsync(Context, account.Id, minPayment: 180m, extraPayment: 20m, paymentDueDay: 5, statementCloseDay: null);

        var reloaded = await Context.Accounts.SingleAsync(a => a.Id == account.Id);
        Assert.Equal("Discover", reloaded.Name);
        Assert.Equal(180m, reloaded.MinPayment);
        Assert.Equal(20m, reloaded.ExtraPayment);
        Assert.Equal(5, reloaded.PaymentDueDay);
    }

    [Fact]
    public async Task CreateAccountAsync_CanSetApr()
    {
        var account = await _sut.CreateAccountAsync(Context, name: "SoFi", type: AccountType.Debt, apr: 9.99m);

        Assert.Equal(9.99m, account.Apr);
    }

    [Fact]
    public async Task UpdateAccountAsync_CanUpdateApr()
    {
        var account = await _sut.CreateAccountAsync(Context, name: "SoFi", type: AccountType.Debt, apr: 9.99m);

        await _sut.UpdateAccountAsync(Context, account.Id, "SoFi", minPayment: null, extraPayment: null,
            paymentDueDay: null, statementCloseDay: null, apr: 10.49m);

        var reloaded = await Context.Accounts.SingleAsync(a => a.Id == account.Id);
        Assert.Equal(10.49m, reloaded.Apr);
    }

    [Fact]
    public async Task AddOrUpdateBalanceSnapshotAsync_InsertsANewSnapshot_WhenNoneExistsForThatDate()
    {
        var account = await _sut.CreateAccountAsync(Context, name: "SoFi", type: AccountType.Debt);

        await _sut.AddOrUpdateBalanceSnapshotAsync(Context, account.Id, new DateOnly(2026, 7, 31), 45876.50m);

        var snapshot = await Context.DebtBalanceSnapshots.SingleAsync(s => s.AccountId == account.Id);
        Assert.Equal(new DateOnly(2026, 7, 31), snapshot.AsOfDate);
        Assert.Equal(45876.50m, snapshot.Balance);
    }

    [Fact]
    public async Task AddOrUpdateBalanceSnapshotAsync_UpdatesTheExistingSnapshot_WhenOneAlreadyExistsForThatDate()
    {
        var account = await _sut.CreateAccountAsync(Context, name: "SoFi", type: AccountType.Debt);
        await _sut.AddOrUpdateBalanceSnapshotAsync(Context, account.Id, new DateOnly(2026, 7, 31), 45876.50m);

        // Correcting a same-day entry (e.g. a typo) shouldn't create a second row for the same date.
        await _sut.AddOrUpdateBalanceSnapshotAsync(Context, account.Id, new DateOnly(2026, 7, 31), 45000.00m);

        var snapshot = await Context.DebtBalanceSnapshots.SingleAsync(s => s.AccountId == account.Id);
        Assert.Equal(45000.00m, snapshot.Balance);
    }
}
