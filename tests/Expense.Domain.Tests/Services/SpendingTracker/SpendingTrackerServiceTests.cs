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
        // CarryoverAnchorDate defaults to CURRENT_DATE (the real wall-clock date) via the DB
        // column default - fixed here to line up with AsOfDate/EffectiveFrom instead, so tests
        // stay deterministic regardless of what today's real date happens to be.
        var category = new Category { Name = "Groceries", CarryoverAnchorDate = new DateOnly(2026, 1, 1) };
        Context.Categories.Add(category);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = category.Id, Strategy = FundingStrategies.TrackedBudget });
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

    private async Task<Category> CreateCategoryAsync(string name, decimal amount, Frequency frequency, DateOnly anchor, decimal? capMultiplier = 1.0m)
    {
        var category = new Category { Name = name, CarryoverAnchorDate = anchor, CarryoverCapMultiplier = capMultiplier };
        Context.Categories.Add(category);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = category.Id, Strategy = FundingStrategies.TrackedBudget });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = category.Id, Amount = amount, Frequency = frequency, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await Context.SaveChangesAsync();
        return category;
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

    // Real gap this guards (found during the Dashboard week/month navigation work, 2026-08-03):
    // this service only ever queried the currently-open budget period (EffectiveThrough IS
    // NULL) - fine for "current week/month" (today always falls inside the open period by
    // construction) but wrong the moment a past period becomes browsable, since a since-changed
    // budget would silently misreport what was actually budgeted back then. HistoricalAnalysis
    // already gets this right (looks up the period whose EffectiveFrom/Through actually covers
    // the period being asked about) - same fix here.
    [Fact]
    public async Task AskingForAPastPeriod_UsesTheBudgetThatWasActuallyInEffectThen_NotTodays()
    {
        var category = new Category { Name = "Groceries", CarryoverAnchorDate = new DateOnly(2026, 1, 1) };
        Context.Categories.Add(category);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = category.Id, Strategy = FundingStrategies.TrackedBudget });
        Context.BudgetPeriods.AddRange(
            new BudgetPeriod
            {
                CategoryId = category.Id, Amount = 400m, Frequency = Frequency.Weekly,
                EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveThrough = new DateOnly(2026, 6, 30)
            },
            new BudgetPeriod
            {
                CategoryId = category.Id, Amount = 450m, Frequency = Frequency.Weekly,
                EffectiveFrom = new DateOnly(2026, 7, 1)
            });
        await Context.SaveChangesAsync();

        // A week entirely within the old $400 budget's effective range.
        var result = await _sut.GetCurrentWeekAsync(Context, new DateOnly(2026, 3, 4));

        var summary = Assert.Single(result.Categories);
        Assert.Equal(400m, summary.Budget);
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
    public async Task PendingSelfReportedCharge_CountsTowardActualSpend_UsingTransactionDate()
    {
        // Self-reported (screenshot-derived) charges have no PostedDate yet - must still
        // count as real spending this week, using TransactionDate as the effective date,
        // for consistency with how the Forecast page already treats these (see AmexCycleCalculator).
        var groceries = await CreateGroceriesAsync(450m, Frequency.Weekly);
        var amex = await CreateAccountAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 14), PostedDate = null,
            Description = "INGLES", Amount = -120m, ImportSource = "ManualScreenshot", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);

        var summary = Assert.Single(result.Categories);
        Assert.Equal(120m, summary.Actual);
    }

    [Fact]
    public async Task UncategorizedPendingSelfReportedCharge_ShowsUpAsPending()
    {
        var groceries = await CreateGroceriesAsync();
        var amex = await CreateAccountAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 14), PostedDate = null,
            Description = "MORGAN COMPOUDING", Amount = -50m, ImportSource = "ManualScreenshot", CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);

        var summary = Assert.Single(result.Categories);
        Assert.Equal(0m, summary.Actual);
        Assert.Equal(50m, result.PendingAmount);
    }

    [Fact]
    public async Task PendingPlaidCharge_CountsTowardActualSpend_UsingTransactionDate()
    {
        // Real bug this guards: a Plaid-imported transaction still pending at the source
        // (no PostedDate yet) was invisible here even when correctly categorized - the
        // "count while pending" exception only ever covered ManualScreenshot charges,
        // never extended to Plaid when Plaid was added as a real import source.
        var groceries = await CreateGroceriesAsync(450m, Frequency.Weekly);
        var amex = await CreateAccountAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 14), PostedDate = null,
            Description = "Publix", Amount = -107.90m, ImportSource = "Plaid", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);

        var summary = Assert.Single(result.Categories);
        Assert.Equal(107.90m, summary.Actual);
    }

    [Fact]
    public async Task UncategorizedPendingPlaidCharge_ShowsUpAsPending()
    {
        var groceries = await CreateGroceriesAsync();
        var amex = await CreateAccountAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 14), PostedDate = null,
            Description = "Publix", Amount = -107.90m, ImportSource = "Plaid", CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);

        var summary = Assert.Single(result.Categories);
        Assert.Equal(0m, summary.Actual);
        Assert.Equal(107.90m, result.PendingAmount);
    }

    [Fact]
    public async Task PendingSelfReportedCharge_OutsideTheCurrentWeek_IsExcluded()
    {
        var groceries = await CreateGroceriesAsync();
        var amex = await CreateAccountAsync();
        Context.BankTransactions.Add(new BankTransaction // last week - Saturday July 11
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 11), PostedDate = null,
            Description = "INGLES", Amount = -120m, ImportSource = "ManualScreenshot", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);

        var summary = Assert.Single(result.Categories);
        Assert.Equal(0m, summary.Actual);
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
    public async Task NonTrackedBudgetCategories_AreExcluded()
    {
        var truist = new Category { Name = "Truist" };
        Context.Categories.Add(truist);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = truist.Id, Strategy = FundingStrategies.Direct });
        await Context.SaveChangesAsync();

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);

        Assert.Empty(result.Categories);
    }

    // Carryover: which view is "native" for a category is decided by its own budgeted
    // Frequency - Weekly categories carry over on the week view, everything else (Monthly
    // here) on the month view. The other view always stays a plain this-period-only number.
    [Fact]
    public async Task WeeklyCategory_OnTheWeekView_IsCarryoverTracked()
    {
        await CreateCategoryAsync("Groceries", 450m, Frequency.Weekly, anchor: new DateOnly(2026, 7, 5));

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);

        var summary = Assert.Single(result.Categories);
        Assert.True(summary.IsCarryoverTracked);
        Assert.NotNull(summary.RollingBalance);
    }

    [Fact]
    public async Task WeeklyCategory_OnTheMonthView_IsNotCarryoverTracked()
    {
        await CreateCategoryAsync("Groceries", 450m, Frequency.Weekly, anchor: new DateOnly(2026, 7, 5));

        var result = await _sut.GetCurrentMonthAsync(Context, AsOfDate);

        var summary = Assert.Single(result.Categories);
        Assert.False(summary.IsCarryoverTracked);
        Assert.Null(summary.RollingBalance);
        Assert.Equal(0m, summary.CarriedIn);
    }

    [Fact]
    public async Task MonthlyCategory_OnTheMonthView_IsCarryoverTracked()
    {
        await CreateCategoryAsync("Clothing", 100m, Frequency.Monthly, anchor: new DateOnly(2026, 7, 1));

        var result = await _sut.GetCurrentMonthAsync(Context, AsOfDate);

        var summary = Assert.Single(result.Categories);
        Assert.True(summary.IsCarryoverTracked);
    }

    [Fact]
    public async Task MonthlyCategory_OnTheWeekView_IsNotCarryoverTracked()
    {
        await CreateCategoryAsync("Clothing", 100m, Frequency.Monthly, anchor: new DateOnly(2026, 7, 1));

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);

        var summary = Assert.Single(result.Categories);
        Assert.False(summary.IsCarryoverTracked);
        Assert.Null(summary.RollingBalance);
    }

    [Fact]
    public async Task RollingBalance_OnTheCurrentWeek_IncludesTheSurplusCarriedInFromThePriorWeek()
    {
        var groceries = await CreateCategoryAsync("Groceries", 450m, Frequency.Weekly, anchor: new DateOnly(2026, 7, 5));
        var amex = await CreateAccountAsync();
        Context.BankTransactions.AddRange(
            new BankTransaction // prior week (7/5-7/11): spent 400, +50 surplus
            {
                AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 8), PostedDate = new DateOnly(2026, 7, 8),
                Description = "Prior week", Amount = -400m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
            },
            new BankTransaction // current week (7/12-7/18): spent 470, -20 this period alone
            {
                AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 14), PostedDate = new DateOnly(2026, 7, 14),
                Description = "Current week", Amount = -470m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
            });
        await Context.SaveChangesAsync();

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);

        var summary = Assert.Single(result.Categories);
        Assert.Equal(450m, summary.Budget);
        Assert.Equal(470m, summary.Actual);
        Assert.Equal(-20m, summary.Remaining); // this period alone, unaffected by carryover
        Assert.Equal(50m, summary.CarriedIn);
        Assert.Equal(30m, summary.RollingBalance);
    }

    [Fact]
    public async Task RollingBalance_IsCappedAtTheCategorysConfiguredMultiple_OfThatPeriodsOwnBudget()
    {
        // No transactions in either the prior or current week - both periods run the full
        // +450 surplus, which would total +900 uncapped; the 1.0x default cap should hold it
        // at 450 instead.
        await CreateCategoryAsync("Groceries", 450m, Frequency.Weekly, anchor: new DateOnly(2026, 7, 5), capMultiplier: 1.0m);

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);

        var summary = Assert.Single(result.Categories);
        Assert.Equal(450m, summary.CarriedIn);
        Assert.Equal(450m, summary.RollingBalance);
        Assert.Equal(450m, summary.CarryoverCap);
    }

    [Fact]
    public async Task NullCapMultiplier_AllowsCarryoverPastOnePeriodsBudget()
    {
        // Anchored to January with no spending at all - by July (7 months inclusive), an
        // uncapped Clothing budget should have accumulated all 7 months' surplus.
        await CreateCategoryAsync("Clothing", 100m, Frequency.Monthly, anchor: new DateOnly(2026, 1, 1), capMultiplier: null);

        var result = await _sut.GetCurrentMonthAsync(Context, AsOfDate);

        var summary = Assert.Single(result.Categories);
        Assert.Equal(700m, summary.RollingBalance);
        Assert.Null(summary.CarryoverCap);
    }

    [Fact]
    public async Task ResetCarryoverAsync_StartingThisPeriod_ZeroesTheCarriedInBalanceImmediately()
    {
        var groceries = await CreateCategoryAsync("Groceries", 450m, Frequency.Weekly, anchor: new DateOnly(2026, 7, 5));

        await _sut.ResetCarryoverAsync(Context, groceries.Id, startingNextPeriod: false, AsOfDate);

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);
        var summary = Assert.Single(result.Categories);
        Assert.Equal(0m, summary.CarriedIn);
        Assert.Equal(450m, summary.RollingBalance); // just this period's own delta now
    }

    [Fact]
    public async Task ResetCarryoverAsync_StartingNextPeriod_LeavesTheCurrentlyInProgressPeriodUntouched()
    {
        var groceries = await CreateCategoryAsync("Groceries", 450m, Frequency.Weekly, anchor: new DateOnly(2026, 7, 5));

        await _sut.ResetCarryoverAsync(Context, groceries.Id, startingNextPeriod: true, AsOfDate);

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);
        var summary = Assert.Single(result.Categories);
        Assert.Equal(450m, summary.CarriedIn); // reset hasn't taken effect yet - still this week
    }

    [Fact]
    public async Task ResetCarryoverAsync_StartingNextPeriod_TakesEffectOnceThatPeriodActuallyBegins()
    {
        var groceries = await CreateCategoryAsync("Groceries", 450m, Frequency.Weekly, anchor: new DateOnly(2026, 7, 5));

        await _sut.ResetCarryoverAsync(Context, groceries.Id, startingNextPeriod: true, AsOfDate); // queues a reset for the week of 7/19

        var result = await _sut.GetCurrentWeekAsync(Context, new DateOnly(2026, 7, 22)); // within the week of 7/19-7/25
        var summary = Assert.Single(result.Categories);
        Assert.Equal(0m, summary.CarriedIn); // the queued reset has now taken effect
    }

    // The Spending Tracker's drill-down: individual bank transactions and Amazon items
    // making up a category's Actual figure for a period.
    [Fact]
    public async Task GetCategoryTransactionsAsync_ReturnsBankAndAmazonLines_SortedByDate()
    {
        var groceries = await CreateGroceriesAsync();
        var amex = await CreateAccountAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 14), PostedDate = new DateOnly(2026, 7, 14),
            Description = "INGLES", Amount = -120m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        Context.AmazonOrderItems.Add(new AmazonOrderItem
        {
            OrderId = "ORDER1", OrderDate = new DateOnly(2026, 7, 13), ItemTitle = "Vitamins", Price = 20m, Quantity = 1,
            TaxAllocated = 1.60m, CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var lines = await _sut.GetCategoryTransactionsAsync(Context, groceries.Id, new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 18));

        Assert.Equal(2, lines.Count);
        Assert.Equal(new DateOnly(2026, 7, 13), lines[0].Date);
        Assert.Equal("Vitamins", lines[0].Description);
        Assert.Equal(21.60m, lines[0].Amount);
        Assert.Equal(new DateOnly(2026, 7, 14), lines[1].Date);
        Assert.Equal("INGLES", lines[1].Description);
        Assert.Equal(120m, lines[1].Amount);
    }

    [Fact]
    public async Task GetCategoryTransactionsAsync_ExcludesTransactionsOutsideThePeriod()
    {
        var groceries = await CreateGroceriesAsync();
        var amex = await CreateAccountAsync();
        Context.BankTransactions.Add(new BankTransaction // outside the requested period
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 11), PostedDate = new DateOnly(2026, 7, 11),
            Description = "LAST WEEK", Amount = -999m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var lines = await _sut.GetCategoryTransactionsAsync(Context, groceries.Id, new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 18));

        Assert.Empty(lines);
    }

    [Fact]
    public async Task GetCategoryTransactionsAsync_RefundShowsAsANegativeLine()
    {
        var groceries = await CreateGroceriesAsync();
        var amex = await CreateAccountAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 14), PostedDate = new DateOnly(2026, 7, 14),
            Description = "INGLES REFUND", Amount = 20m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var lines = await _sut.GetCategoryTransactionsAsync(Context, groceries.Id, new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 18));

        var line = Assert.Single(lines);
        Assert.Equal(-20m, line.Amount);
    }

    // Regression guard: the drill-down list must always sum to exactly the same Actual
    // figure the summary row shows, or the two would visibly disagree with each other.
    [Fact]
    public async Task GetCategoryTransactionsAsync_SumOfLines_ReconcilesWithTheSummarysActualFigure()
    {
        var groceries = await CreateGroceriesAsync();
        var amex = await CreateAccountAsync();
        Context.BankTransactions.AddRange(
            new BankTransaction
            {
                AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 13), PostedDate = new DateOnly(2026, 7, 13),
                Description = "INGLES", Amount = -100m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
            },
            new BankTransaction
            {
                AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 14), PostedDate = new DateOnly(2026, 7, 14),
                Description = "INGLES REFUND", Amount = 20m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
            });
        Context.AmazonOrderItems.Add(new AmazonOrderItem
        {
            OrderId = "ORDER1", OrderDate = new DateOnly(2026, 7, 15), ItemTitle = "Vitamins", Price = 55m, Quantity = 1,
            TaxAllocated = 5m, CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GetCurrentWeekAsync(Context, AsOfDate);
        var lines = await _sut.GetCategoryTransactionsAsync(Context, groceries.Id, result.PeriodStart, result.PeriodEnd);

        var summary = Assert.Single(result.Categories);
        Assert.Equal(summary.Actual, lines.Sum(l => l.Amount));
    }
}
