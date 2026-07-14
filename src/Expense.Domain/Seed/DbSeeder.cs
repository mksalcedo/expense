using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Seed;

/// <summary>
/// Idempotent - safe to run every time the web app or an importer starts up.
/// Real min_payment/extra_payment/payment_due_day/statement_close_day values
/// (beyond Amex's confirmed $1,100 extra principal) are deliberately left NULL
/// rather than guessed, to be corrected via the Accounts UI once it exists -
/// a wrong-but-plausible-looking placeholder number would be worse than a
/// visibly-blank one in a tool the user will actually rely on.
/// </summary>
public class DbSeeder
{
    private readonly AccountManagementService _accounts = new();

    public async Task SeedAsync(ExpenseDbContext context)
    {
        if (await context.Categories.AnyAsync()) return;

        var groceries = new Category { Name = "Groceries", IsBudgeted = true };
        var restaurants = new Category { Name = "Restaurants", IsBudgeted = true };
        var supplements = new Category { Name = "Supplements", IsBudgeted = true };
        var gas = new Category { Name = "Gas", IsBudgeted = true };
        var offBudget = new Category { Name = "Off-Budget/Misc", IsBudgeted = false };
        context.Categories.AddRange(groceries, restaurants, supplements, gas, offBudget);
        await context.SaveChangesAsync();

        context.FundingRules.AddRange(
            new FundingRule { CategoryId = groceries.Id, Strategy = FundingStrategies.PayInFullAmex },
            new FundingRule { CategoryId = restaurants.Id, Strategy = FundingStrategies.PayInFullAmex },
            new FundingRule { CategoryId = supplements.Id, Strategy = FundingStrategies.PayInFullAmex },
            new FundingRule { CategoryId = gas.Id, Strategy = FundingStrategies.PayInFullAmex }
        );
        await context.SaveChangesAsync();

        context.Products.Add(new Product { ProductPattern = "%GIFT CARD%", CategoryId = offBudget.Id });
        await context.SaveChangesAsync();

        context.Accounts.Add(new Account { Name = "Wells Fargo Checking", Type = AccountType.Checking });
        await context.SaveChangesAsync();

        await _accounts.CreateAccountAsync(context, "Amex", AccountType.ActiveSpending,
            extraPayment: 1100m, suggestedMerchantPattern: "%AMERICAN EXPRESS%");

        string[] debtAccountNames =
        [
            "Discover", "Chase Sapphire Reserve", "Chase Amazon Prime Visa", "Chase Credit Card",
            "Wells Fargo Cash Back Visa", "Wells Fargo Personal LOC", "Apple Card", "SoFi", "Venmo Credit Card"
        ];
        foreach (var name in debtAccountNames)
        {
            await _accounts.CreateAccountAsync(context, name, AccountType.Debt);
        }
    }
}
