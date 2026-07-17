using Expense.Domain.Entities;
using Expense.Domain.Services.Budgets;
using Expense.Domain.Services.Forecast;
using Expense.Domain.Tests.TestSupport;

namespace Expense.Domain.Tests.Services.Forecast;

public class ForecastEngineTests : DatabaseTestBase
{
    private readonly ForecastEngine _sut = new(new BudgetProrationService(), new RecurrenceExpander(), new AmexCycleCalculator());

    private async Task SeedCheckingBalanceAsync(decimal balance, DateOnly asOfDate)
    {
        Context.CheckingBalanceSnapshots.Add(new CheckingBalanceSnapshot { AsOfDate = asOfDate, Balance = balance });
        await Context.SaveChangesAsync();
    }

    [Fact]
    public async Task StartingBalance_ComesFromLatestCheckingSnapshot()
    {
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 6, 1));
        await SeedCheckingBalanceAsync(6463.02m, new DateOnly(2026, 7, 13)); // latest wins

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        Assert.Equal(6463.02m, result.StartingBalance);
    }

    [Fact]
    public async Task DirectCategory_AppearsAsALedgerLineAndUpdatesRunningBalance()
    {
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 13));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var paycheck = new Category { Name = "Paycheck" };
        Context.Categories.Add(paycheck);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = paycheck.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = paycheck.Id, Amount = 2000m, Frequency = Frequency.Biweekly, Direction = Direction.Income,
            Anchor = new DateOnly(2026, 7, 17), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 25));

        var row = Assert.Single(result.Rows);
        Assert.Equal(new DateOnly(2026, 7, 17), row.Date);
        Assert.Equal("Paycheck", row.Description);
        Assert.Equal(2000m, row.Amount);
        Assert.Equal(3000m, row.RunningBalance);
    }

    [Fact]
    public async Task DirectCategory_WithNoAnchorOrAccountYetConfigured_ProducesNoLine()
    {
        // A category can be marked Direct before its budget period has an anchor/account set
        // (e.g. mid-edit in the UI) - it must not blow up the forecast, just be excluded.
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 13));

        var category = new Category { Name = "Incomplete Direct Item" };
        Context.Categories.Add(category);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = category.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = category.Id, Amount = 500m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task OneTimeEvent_AppearsAsALedgerLine()
    {
        await SeedCheckingBalanceAsync(5000m, new DateOnly(2026, 7, 13));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        Context.OneTimeEvents.Add(new OneTimeEvent
        {
            Name = "HVAC repair", Amount = 850m, Direction = Direction.Expense, Date = new DateOnly(2026, 7, 20), AccountId = checking.Id
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows);
        Assert.Equal("HVAC repair", row.Description);
        Assert.Equal(-850m, row.Amount);
        Assert.Equal(4150m, row.RunningBalance);
    }

    [Fact]
    public async Task ConfiguredDebtAccount_ProducesMonthlyPaymentLine()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        Context.Accounts.Add(new Account
        {
            Name = "Discover", Type = AccountType.Debt, MinPayment = 50m, ExtraPayment = 100m, PaymentDueDay = 20
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows);
        Assert.Equal(new DateOnly(2026, 7, 20), row.Date);
        Assert.Equal("Discover Payment", row.Description);
        Assert.Equal(-150m, row.Amount);
    }

    [Fact]
    public async Task DebtAccountWithNoConfiguredPayment_ProducesNoLine()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        Context.Accounts.Add(new Account
        {
            Name = "SoFi", Type = AccountType.Debt, PaymentDueDay = 20 // min/extra payment left as unset placeholders
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task AmexFutureCycle_UsesBudgetedTotalFromFundedCategories()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 1, 1));
        var amex = new Account
        {
            Name = "Amex", Type = AccountType.ActiveSpending, ExtraPayment = 1100m,
            StatementCloseDay = 25, PaymentDueDay = 15
        };
        Context.Accounts.Add(amex);
        await Context.SaveChangesAsync();

        var groceries = new Category { Name = "Groceries" };
        var offBudget = new Category { Name = "Off-Budget/Misc" };
        Context.Categories.AddRange(groceries, offBudget);
        await Context.SaveChangesAsync();

        Context.FundingRules.AddRange(
            new FundingRule { CategoryId = groceries.Id, Strategy = FundingStrategies.PayInFullAmex },
            new FundingRule { CategoryId = offBudget.Id, Strategy = FundingStrategies.None });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = groceries.Id, Amount = 450m, Frequency = Frequency.Weekly, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await Context.SaveChangesAsync();

        // asOfDate (Jan 1) is far before this cycle even starts (Jun 26) - budget only, no actual data
        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 31));

        var amexRow = Assert.Single(result.Rows, r => r.Description == "Amex Payment" && r.Date == new DateOnly(2026, 7, 15));
        var expectedMonthlyGroceries = new BudgetProrationService().Convert(450m, Frequency.Weekly, Frequency.Monthly);
        Assert.Equal(-(expectedMonthlyGroceries + 1100m), amexRow.Amount);
    }

    [Fact]
    public async Task AmexClosedCycle_OverBudget_UsesActualQualifyingCharges()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 3, 1));
        var amex = new Account
        {
            Name = "Amex", Type = AccountType.ActiveSpending, ExtraPayment = 0m,
            StatementCloseDay = 25, PaymentDueDay = 15
        };
        Context.Accounts.Add(amex);
        await Context.SaveChangesAsync();

        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = groceries.Id, Strategy = FundingStrategies.PayInFullAmex });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = groceries.Id, Amount = 900m, Frequency = Frequency.Monthly, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        // cycle for the Mar 15 due date runs Jan 26 - Feb 25 - well over the $900 budget
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 2, 1), PostedDate = new DateOnly(2026, 2, 1),
            Description = "TRADER JOE S", Amount = -1250m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        var amexRow = Assert.Single(result.Rows, r => r.Description == "Amex Payment");
        Assert.Equal(-1250m, amexRow.Amount); // actual (1250) beats budget (900), plus $0 extra principal
    }

    [Fact]
    public async Task AmexClosedCycle_CountsUncategorizedCharges_NotJustCategorizedOnes()
    {
        // Real bug: the payment amount used to only count charges already sorted into a
        // PayInFullAmex category - an uncategorized charge (still sitting in the Review
        // Queue backlog) was invisible to "how much do I owe", understating the forecast.
        // The card is pay-in-full: every real charge needs to be paid regardless of whether
        // it's been categorized yet.
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 3, 1));
        var amex = new Account
        {
            Name = "Amex", Type = AccountType.ActiveSpending, ExtraPayment = 0m,
            StatementCloseDay = 25, PaymentDueDay = 15
        };
        Context.Accounts.Add(amex);
        await Context.SaveChangesAsync();

        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = groceries.Id, Strategy = FundingStrategies.PayInFullAmex });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = groceries.Id, Amount = 100m, Frequency = Frequency.Monthly, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        Context.BankTransactions.AddRange(
            new BankTransaction
            {
                AccountId = amex.Id, TransactionDate = new DateOnly(2026, 2, 1), PostedDate = new DateOnly(2026, 2, 1),
                Description = "TRADER JOE S", Amount = -200m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
            },
            new BankTransaction
            {
                AccountId = amex.Id, TransactionDate = new DateOnly(2026, 2, 5), PostedDate = new DateOnly(2026, 2, 5),
                Description = "NETFLIX.COM", Amount = -15m, ImportSource = "Test", CategoryId = null, CreatedAt = DateTimeOffset.UtcNow
            });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        var amexRow = Assert.Single(result.Rows, r => r.Description == "Amex Payment");
        Assert.Equal(-215m, amexRow.Amount); // 200 (categorized) + 15 (uncategorized) - both are real charges
    }

    [Fact]
    public async Task AmexClosedCycle_ExcludesPaymentsAndCredits_OnlyCountsActualCharges()
    {
        // A payment you already made toward the card this cycle (or a points credit) shows
        // up as a positive amount - it must never be netted against your spending, or the
        // forecast would understate what you still owe by whatever you've already paid.
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 3, 1));
        var amex = new Account
        {
            Name = "Amex", Type = AccountType.ActiveSpending, ExtraPayment = 0m,
            StatementCloseDay = 25, PaymentDueDay = 15
        };
        Context.Accounts.Add(amex);
        await Context.SaveChangesAsync();

        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = groceries.Id, Strategy = FundingStrategies.PayInFullAmex });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = groceries.Id, Amount = 100m, Frequency = Frequency.Monthly, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        Context.BankTransactions.AddRange(
            new BankTransaction
            {
                AccountId = amex.Id, TransactionDate = new DateOnly(2026, 2, 1), PostedDate = new DateOnly(2026, 2, 1),
                Description = "TRADER JOE S", Amount = -200m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
            },
            new BankTransaction
            {
                AccountId = amex.Id, TransactionDate = new DateOnly(2026, 2, 10), PostedDate = new DateOnly(2026, 2, 10),
                Description = "AUTOPAY PAYMENT - THANK YOU", Amount = 150m, ImportSource = "Test", CategoryId = null, CreatedAt = DateTimeOffset.UtcNow
            });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        var amexRow = Assert.Single(result.Rows, r => r.Description == "Amex Payment");
        Assert.Equal(-200m, amexRow.Amount); // the $150 payment must not offset the $200 charge
    }

    [Fact]
    public async Task DeferredPayment_MovesToTheNewDateAndIsFlagged()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var discover = new Account
        {
            Name = "Discover", Type = AccountType.Debt, MinPayment = 50m, ExtraPayment = 100m, PaymentDueDay = 20
        };
        Context.Accounts.Add(discover);
        await Context.SaveChangesAsync();

        Context.PaymentDeferrals.Add(new PaymentDeferral
        {
            AccountId = discover.Id, OriginalDate = new DateOnly(2026, 7, 20), DeferredToDate = new DateOnly(2026, 7, 22),
            Note = "waiting on paycheck", CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows);
        Assert.Equal(new DateOnly(2026, 7, 22), row.Date);
        Assert.Equal(new DateOnly(2026, 7, 20), row.OriginalDate);
        Assert.True(row.IsDeferred);
        Assert.NotNull(row.DeferralId);
    }

    [Fact]
    public async Task UndeferredPayment_IsNotFlagged()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        Context.Accounts.Add(new Account
        {
            Name = "Discover", Type = AccountType.Debt, MinPayment = 50m, ExtraPayment = 100m, PaymentDueDay = 20
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows);
        Assert.Equal(new DateOnly(2026, 7, 20), row.Date);
        Assert.Equal(new DateOnly(2026, 7, 20), row.OriginalDate);
        Assert.False(row.IsDeferred);
        Assert.Null(row.DeferralId);
    }

    [Fact]
    public async Task LowestProjectedBalance_ReflectsTheMinimumRunningBalance()
    {
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 13));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        Context.OneTimeEvents.AddRange(
            new OneTimeEvent { Name = "Big expense", Amount = 900m, Direction = Direction.Expense, Date = new DateOnly(2026, 7, 15), AccountId = checking.Id },
            new OneTimeEvent { Name = "Refund", Amount = 200m, Direction = Direction.Income, Date = new DateOnly(2026, 7, 20), AccountId = checking.Id });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        Assert.Equal(100m, result.LowestProjectedBalance); // 1000 - 900 = 100, then +200 = 300
    }
}
