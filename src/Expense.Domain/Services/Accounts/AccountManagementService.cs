using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Accounts;

/// <summary>
/// Adding/editing/removing an account is one unified operation, not three separate
/// manual steps: creating an account also creates its matching "X Payment" category
/// (funding strategy 'account_payment' - its expected amount comes from this Account's
/// MinPayment/ExtraPayment, never entered on the category itself) and a suggested
/// merchant rule the user can adjust afterward. Removal deactivates rather than
/// hard-deletes, preserving historical transactions/reports.
/// </summary>
public class AccountManagementService
{
    public async Task<Account> CreateAccountAsync(
        ExpenseDbContext context,
        string name,
        AccountType type = AccountType.Debt,
        decimal? minPayment = null,
        decimal? extraPayment = null,
        int? paymentDueDay = null,
        int? statementCloseDay = null,
        string? suggestedMerchantPattern = null,
        decimal? apr = null)
    {
        var account = new Account
        {
            Name = name,
            Type = type,
            MinPayment = minPayment,
            ExtraPayment = extraPayment,
            PaymentDueDay = paymentDueDay,
            StatementCloseDay = statementCloseDay,
            Apr = apr
        };
        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        // Savings has no "payment" concept - it's a manually-tracked asset balance, not a bill
        // the forecast pays, so none of the Debt/ActiveSpending payment-category machinery applies.
        if (type != AccountType.Savings)
        {
            var category = new Category { Name = $"{name} Payment" };
            context.Categories.Add(category);
            await context.SaveChangesAsync();

            context.FundingRules.Add(new FundingRule { CategoryId = category.Id, Strategy = FundingStrategies.AccountPayment, AccountId = account.Id });

            var pattern = suggestedMerchantPattern ?? $"%{name.ToUpperInvariant()}%";
            context.MerchantRules.Add(new MerchantRule { MerchantPattern = pattern, CategoryId = category.Id });

            await context.SaveChangesAsync();
        }

        return account;
    }

    public async Task DeactivateAccountAsync(ExpenseDbContext context, int accountId)
    {
        var account = await context.Accounts.SingleAsync(a => a.Id == accountId);
        account.IsActive = false;
        await context.SaveChangesAsync();
    }

    public async Task ReactivateAccountAsync(ExpenseDbContext context, int accountId)
    {
        var account = await context.Accounts.SingleAsync(a => a.Id == accountId);
        account.IsActive = true;
        await context.SaveChangesAsync();
    }

    /// <summary>Combined save for the master-detail edit form: name and payment fields commit together. Type is fixed at creation.</summary>
    public async Task UpdateAccountAsync(
        ExpenseDbContext context, int accountId, string name,
        decimal? minPayment, decimal? extraPayment, int? paymentDueDay, int? statementCloseDay, decimal? apr = null)
    {
        var account = await context.Accounts.SingleAsync(a => a.Id == accountId);
        account.Name = name;
        account.Apr = apr;
        await SetPaymentFieldsAsync(context, account, minPayment, extraPayment, paymentDueDay, statementCloseDay);
    }

    /// <summary>
    /// Upserts by (AccountId, AsOfDate) rather than always inserting - since this data is
    /// entered by hand (no automatic sync writes to it currently), re-saving the same day is a
    /// correction (e.g. a typo), not a second real observation.
    /// </summary>
    public async Task AddOrUpdateBalanceSnapshotAsync(ExpenseDbContext context, int accountId, DateOnly asOfDate, decimal balance)
    {
        var snapshot = await context.DebtBalanceSnapshots
            .SingleOrDefaultAsync(s => s.AccountId == accountId && s.AsOfDate == asOfDate);

        if (snapshot is null)
        {
            context.DebtBalanceSnapshots.Add(new DebtBalanceSnapshot { AccountId = accountId, AsOfDate = asOfDate, Balance = balance });
        }
        else
        {
            snapshot.Balance = balance;
        }

        await context.SaveChangesAsync();
    }

    /// <summary>Payment-fields-only save, for editing an AccountPayment category's linked account inline from Categories.razor - never touches the account's name.</summary>
    public async Task UpdatePaymentFieldsAsync(
        ExpenseDbContext context, int accountId,
        decimal? minPayment, decimal? extraPayment, int? paymentDueDay, int? statementCloseDay)
    {
        var account = await context.Accounts.SingleAsync(a => a.Id == accountId);
        await SetPaymentFieldsAsync(context, account, minPayment, extraPayment, paymentDueDay, statementCloseDay);
    }

    private static async Task SetPaymentFieldsAsync(
        ExpenseDbContext context, Account account,
        decimal? minPayment, decimal? extraPayment, int? paymentDueDay, int? statementCloseDay)
    {
        account.MinPayment = minPayment;
        account.ExtraPayment = extraPayment;
        account.PaymentDueDay = paymentDueDay;
        account.StatementCloseDay = statementCloseDay;
        await context.SaveChangesAsync();
    }
}
