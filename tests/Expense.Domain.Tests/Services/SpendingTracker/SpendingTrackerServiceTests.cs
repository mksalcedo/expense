using Expense.Domain.Entities;
using Expense.Domain.Services.Budgets;
using Expense.Domain.Services.SpendingTracker;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.SpendingTracker;

public class SpendingTrackerServiceTests : DatabaseTestBase
{
    private readonly SpendingTrackerService _sut = new(new BudgetProrationService());

    // 2026-07-15 is a Wednesday; its Sunday-start week runs 2026-07-12 (Sun) - 2026-07-18 (Sat).
    private static readonly DateOnly AsOfDate = new(2026, 7, 15);

    private async Task<Category> CreateGroceriesAsync(decimal amount = 450m, Frequency frequency = Frequency.Weekly)
    {
        var category = new Category { Name = "Groceries" };
        Context.Categories.Add(category);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = category.Id, Strategy = FundingStrategies.PayInFullAmex });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = category.Id, Amount = amount, Frequency = frequency, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await Context.SaveChangesAsync();
        return category;
    }

    private async Task<Account> CreateAccountAsync()
    {
        var account = new Account { Name = "Amex", Type = AccountType.ActiveSpending };
        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task GetCurrentWeekAsync_ComputesBudgetActualAndRemaining()
    {
        var groceries = await CreateGroceriesAsync(450m, Frequency.Weekly);
        var amex = await CreateAccountAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 14), PostedDate = new DateOnly(2026, 7, 14),
            Description = "INGLES", Amount = -120m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);

        var summary = Assert.Single(result.Categories);
        Assert.Equal(new DateOnly(2026, 7, 12), result.PeriodStart);
        Assert.Equal(new DateOnly(2026, 7, 18), result.PeriodEnd);
        Assert.Equal(450m, summary.Budget);
        Assert.Equal(120m, summary.Actual);
        Assert.Equal(330m, summary.Remaining);
    }

    [Fact]
    public async Task GetCurrentMonthAsync_ProratesAWeeklyBudgetToMonthly()
    {
        await CreateGroceriesAsync(450m, Frequency.Weekly);

        var result = await _sut.GetCurrentMonthAsync(Context, AsOfDate);

        var summary = Assert.Single(result.Categories);
        var expectedMonthly = new BudgetProrationService().Convert(450m, Frequency.Weekly, Frequency.Monthly);
        Assert.Equal(expectedMonthly, summary.Budget);
        Assert.Equal(new DateOnly(2026, 7, 1), result.PeriodStart);
        Assert.Equal(new DateOnly(2026, 7, 31), result.PeriodEnd);
    }

    [Fact]
    public async Task TransactionsOutsideTheCurrentWeek_AreExcluded_NoCarryover()
    {
        var groceries = await CreateGroceriesAsync();
        var amex = await CreateAccountAsync();
        Context.BankTransactions.AddRange(
            new BankTransaction // last week - Saturday July 11
            {
                AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 11), PostedDate = new DateOnly(2026, 7, 11),
                Description = "LAST WEEK", Amount = -999m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
            },
            new BankTransaction // next week - Sunday July 19
            {
                AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 19), PostedDate = new DateOnly(2026, 7, 19),
                Description = "NEXT WEEK", Amount = -999m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
            });
        await Context.SaveChangesAsync();

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);

        var summary = Assert.Single(result.Categories);
        Assert.Equal(0m, summary.Actual);
    }

    [Fact]
    public async Task UncategorizedBankTransactions_ShowUpAsPending_NotInAnyCategory()
    {
        var groceries = await CreateGroceriesAsync();
        var amex = await CreateAccountAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 14), PostedDate = new DateOnly(2026, 7, 14),
            Description = "UNKNOWN MERCHANT", Amount = -75m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);

        var summary = Assert.Single(result.Categories);
        Assert.Equal(0m, summary.Actual);
        Assert.Equal(75m, result.PendingAmount);
    }

    [Fact]
    public async Task UncategorizedIncomeTransactions_AreExcludedFromPending_ThisIsSpendOnly()
    {
        // An uncategorized paycheck deposit (or any positive-amount transaction) isn't
        // "spend that hasn't been sorted yet" - it's income, and doesn't belong here at all.
        await CreateGroceriesAsync();
        var checking = new Account { Name = "Wells Fargo Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 14), PostedDate = new DateOnly(2026, 7, 14),
            Description = "EFX PAYROLL DEPOSIT", Amount = 4588.87m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);

        Assert.Equal(0m, result.PendingAmount);
    }

    [Fact]
    public async Task AmazonMerchantBankRow_IsExcludedFromCategoryTotals_OnlyTheItemLevelDataCounts()
    {
        var groceries = await CreateGroceriesAsync();
        var amex = await CreateAccountAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 14), PostedDate = new DateOnly(2026, 7, 14),
            Description = "AMAZON MARKETPLACE", Amount = -60m, ImportSource = "Test", IsAmazonMerchant = true, CreatedAt = DateTimeOffset.UtcNow
        });
        Context.AmazonOrderItems.Add(new AmazonOrderItem
        {
            OrderId = "ORDER1", OrderDate = new DateOnly(2026, 7, 14), ItemTitle = "Vitamins", Price = 55m, Quantity = 1,
            TaxAllocated = 5m, CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);

        var summary = Assert.Single(result.Categories);
        Assert.Equal(60m, summary.Actual); // 55 + 5 tax, NOT also the -60 bank row
        Assert.Equal(0m, result.PendingAmount); // the Amazon-merchant bank row isn't pending either
    }

    [Fact]
    public async Task AmazonItemsCountByOrderDate_NotPostedDate()
    {
        var groceries = await CreateGroceriesAsync();
        Context.AmazonOrderItems.Add(new AmazonOrderItem
        {
            OrderId = "ORDER1", OrderDate = new DateOnly(2026, 6, 30), ItemTitle = "Vitamins", Price = 55m, Quantity = 1,
            TaxAllocated = 0m, CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);

        var summary = Assert.Single(result.Categories);
        Assert.Equal(0m, summary.Actual); // order was placed the prior week - outside this window
    }

    [Fact]
    public async Task UncategorizedAmazonItem_ShowsUpAsPending()
    {
        await CreateGroceriesAsync();
        Context.AmazonOrderItems.Add(new AmazonOrderItem
        {
            OrderId = "ORDER1", OrderDate = new DateOnly(2026, 7, 14), ItemTitle = "New gadget", Price = 40m, Quantity = 1,
            TaxAllocated = 0m, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);

        Assert.Equal(40m, result.PendingAmount);
    }

    [Fact]
    public async Task RefundReducesActualSpend()
    {
        var groceries = await CreateGroceriesAsync();
        var amex = await CreateAccountAsync();
        Context.BankTransactions.AddRange(
            new BankTransaction
            {
                AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 13), PostedDate = new DateOnly(2026, 7, 13),
                Description = "INGLES", Amount = -100m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
            },
            new BankTransaction // refund - positive amount
            {
                AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 14), PostedDate = new DateOnly(2026, 7, 14),
                Description = "INGLES REFUND", Amount = 20m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
            });
        await Context.SaveChangesAsync();

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);

        var summary = Assert.Single(result.Categories);
        Assert.Equal(80m, summary.Actual);
    }

    [Fact]
    public async Task DeactivatedCategory_IsExcluded()
    {
        var groceries = await CreateGroceriesAsync();
        groceries.IsActive = false;
        await Context.SaveChangesAsync();

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);

        Assert.Empty(result.Categories);
    }

    [Fact]
    public async Task NonPayInFullAmexCategories_AreExcluded()
    {
        var truist = new Category { Name = "Truist" };
        Context.Categories.Add(truist);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = truist.Id, Strategy = FundingStrategies.Direct });
        await Context.SaveChangesAsync();

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);

        Assert.Empty(result.Categories);
    }
}
