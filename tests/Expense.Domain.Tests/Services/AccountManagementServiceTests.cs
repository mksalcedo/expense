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
}
