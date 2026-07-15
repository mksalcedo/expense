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
        string? suggestedMerchantPattern = null)
    {
        var account = new Account
        {
            Name = name,
            Type = type,
            MinPayment = minPayment,
            ExtraPayment = extraPayment,
            PaymentDueDay = paymentDueDay,
            StatementCloseDay = statementCloseDay
        };
        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        var category = new Category { Name = $"{name} Payment" };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        context.FundingRules.Add(new FundingRule { CategoryId = category.Id, Strategy = FundingStrategies.AccountPayment });

        var pattern = suggestedMerchantPattern ?? $"%{name.ToUpperInvariant()}%";
        context.MerchantRules.Add(new MerchantRule { MerchantPattern = pattern, CategoryId = category.Id });

        await context.SaveChangesAsync();

        return account;
    }

    public async Task DeactivateAccountAsync(ExpenseDbContext context, int accountId)
    {
        var account = await context.Accounts.SingleAsync(a => a.Id == accountId);
        account.IsActive = false;
        await context.SaveChangesAsync();
    }
}
