using Expense.Domain.Entities;
using Expense.Domain.Services.Budgets;
using Expense.Domain.Services.Forecast;
using Expense.Domain.Tests.TestSupport;

namespace Expense.Domain.Tests.Services.Forecast;

public class ForecastEngineTests : DatabaseTestBase
{
    private readonly ForecastEngine _sut = new(new BudgetProrationService(), new RecurrenceExpander(), new AmexCycleCalculator());

    private async Task SeedCheckingBalanceAsync(decimal balance, DateOnly asOfDate, DateTimeOffset? asOfTimestamp = null)
    {
        Context.CheckingBalanceSnapshots.Add(new CheckingBalanceSnapshot
        {
            AsOfDate = asOfDate,
            AsOfTimestamp = asOfTimestamp ?? asOfDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            Balance = balance
        });
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
    public async Task StartingBalance_MultipleSnapshotsOnTheSameDate_UsesTheOneWithTheLaterRealTimestamp()
    {
        // Real bug this guards: two different sources (e.g. a stale SimpleFin sync and a
        // more current Plaid backup import) can both write a snapshot for the same
        // calendar day - AsOfDate alone can't break the tie. It must be decided by which
        // one is genuinely more current (AsOfTimestamp), not by insertion order - e.g. a
        // stale SimpleFin sync that happens to run *after* a fresh Plaid pull must not win
        // just because it was inserted later.
        await SeedCheckingBalanceAsync(5136.49m, new DateOnly(2026, 7, 25), new DateTimeOffset(2026, 7, 25, 20, 57, 0, TimeSpan.Zero)); // Plaid, fresher
        await SeedCheckingBalanceAsync(4548.83m, new DateOnly(2026, 7, 25), new DateTimeOffset(2026, 7, 25, 6, 0, 0, TimeSpan.Zero)); // SimpleFin, stale, inserted later

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 26), new DateOnly(2026, 8, 10));

        Assert.Equal(5136.49m, result.StartingBalance);
    }

    [Fact]
    public async Task StartingBalance_TrueTie_SameDateAndSameTimestamp_UsesTheMostRecentlyInsertedOne()
    {
        var tiedTimestamp = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        await SeedCheckingBalanceAsync(4548.83m, new DateOnly(2026, 7, 24), tiedTimestamp);
        await SeedCheckingBalanceAsync(5092.71m, new DateOnly(2026, 7, 24), tiedTimestamp); // inserted later

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 25), new DateOnly(2026, 8, 10));

        Assert.Equal(5092.71m, result.StartingBalance);
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
        // The prior Biweekly occurrence (7/3) already came in for real - without this,
        // the widened reconciliation lookback would (correctly) still show it as an
        // unconfirmed, still-pending occurrence too.
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 3), PostedDate = new DateOnly(2026, 7, 3),
            Description = "EFX PAYROLL", Amount = 2000m, ImportSource = "Test", CategoryId = paycheck.Id,
            ReconciledOccurrenceDate = new DateOnly(2026, 7, 3), CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 25));

        var row = Assert.Single(result.Rows);
        Assert.Equal(new DateOnly(2026, 7, 17), row.Date);
        Assert.Equal("Paycheck", row.Description);
        Assert.Equal(2000m, row.Amount);
        Assert.Equal(3000m, row.RunningBalance);
        // Carried through so a captured snapshot line can later be checked against real
        // reconciled transactions when diffing - see docs/forecast-history-redesign-plan.md.
        Assert.Equal(paycheck.Id, row.CategoryId);
    }

    [Fact]
    public async Task MultipleLines_OnTheSameDay_ListsIncomeBeforeExpense()
    {
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 13));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        // Rent seeded/added before Paycheck throughout, deliberately - so its lower id
        // would naturally sort first if the only sort key were still just Date, making
        // this a genuine red-then-green test of the income-first secondary sort.
        var rent = new Category { Name = "Rent" };
        var paycheck = new Category { Name = "Paycheck" };
        Context.Categories.AddRange(rent, paycheck);
        await Context.SaveChangesAsync();

        Context.FundingRules.AddRange(
            new FundingRule { CategoryId = rent.Id, Strategy = FundingStrategies.Direct },
            new FundingRule { CategoryId = paycheck.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.AddRange(
            new BudgetPeriod
            {
                CategoryId = rent.Id, Amount = 1500m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
                Anchor = new DateOnly(2026, 7, 17), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
            },
            new BudgetPeriod
            {
                CategoryId = paycheck.Id, Amount = 2000m, Frequency = Frequency.Monthly, Direction = Direction.Income,
                Anchor = new DateOnly(2026, 7, 17), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
            });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 25));

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("Paycheck", result.Rows[0].Description);
        Assert.Equal("Rent", result.Rows[1].Description);
    }

    [Fact]
    public async Task DirectFundedBill_PostedEarly_MoreThanMaxMatchWindowDaysBeforeAsOfDate_StillReconciles()
    {
        // Real bug this guards: exclusion used to depend on a date-window search relative
        // to asOfDate, widened by only one MaxMatchWindowDays radius from today even though
        // an included occurrence could itself be up to MaxMatchWindowDays old, and its own
        // match window then reached a further MaxMatchWindowDays before that - a transaction
        // posted in that gap (here: occurrence 7/10 is exactly asOfDate-14, and the real
        // transaction posted 2 days earlier still, 7/8) was silently excluded from
        // consideration, so the line reappeared - a real double-count against a checking
        // balance that already reflected it. ReconciledOccurrenceDate is set once by
        // TransactionReconciliationService using only the transaction's own PostedDate
        // against the category's real schedule, never relative to asOfDate, so this can't
        // recur regardless of how the widening math elsewhere ever changes.
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 24));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var ssa = new Category { Name = "SSA" };
        Context.Categories.Add(ssa);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = ssa.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = ssa.Id, Amount = 652m, Frequency = Frequency.Monthly, Direction = Direction.Income,
            Anchor = new DateOnly(2026, 7, 10), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 8), PostedDate = new DateOnly(2026, 7, 8),
            Description = "SSA TREAS 310", Amount = 652m, ImportSource = "Test", CategoryId = ssa.Id,
            ReconciledOccurrenceDate = new DateOnly(2026, 7, 10), CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 24), new DateOnly(2026, 8, 10));

        Assert.DoesNotContain(result.Rows, r => r.Description == "SSA" && r.Date == new DateOnly(2026, 7, 10));
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

        var amexRow = Assert.Single(result.Rows, r => r.Description == "Amex Payment" && r.Date == new DateOnly(2026, 3, 15));
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

        var amexRow = Assert.Single(result.Rows, r => r.Description == "Amex Payment" && r.Date == new DateOnly(2026, 3, 15));
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

        var amexRow = Assert.Single(result.Rows, r => r.Description == "Amex Payment" && r.Date == new DateOnly(2026, 3, 15));
        Assert.Equal(-200m, amexRow.Amount); // the $150 payment must not offset the $200 charge
    }

    [Fact]
    public async Task OneTimeEvent_StillShows_ShortlyAfterItsOwnDateHasPassed()
    {
        // A deferred/confirmed one-time event's OriginalDate can end up in the past relative
        // to asOfDate - it must not vanish the instant that happens, the same reason
        // RecurringRule-based lines are widened backward too.
        await SeedCheckingBalanceAsync(5000m, new DateOnly(2026, 7, 13));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        Context.OneTimeEvents.Add(new OneTimeEvent
        {
            Name = "HVAC repair", Amount = 850m, Direction = Direction.Expense, Date = new DateOnly(2026, 7, 10), AccountId = checking.Id
        });
        await Context.SaveChangesAsync();

        // asOfDate (7/20) is 10 days after the event's date - within the 14-day window.
        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows);
        Assert.Equal("HVAC repair", row.Description);
    }

    // Real scenario this guards: a "top-up" one-time event alongside an existing recurring
    // bill in the same category (e.g. Water $193/mo budgeted, but the real bill came in at
    // $378.20, modeled as the recurring $193 line plus a separate $185.20 one-time top-up).
    // The SAME real payment satisfies both, but a BankTransaction can only ever record one
    // ReconciledOccurrenceDate - it reconciles to the recurring line's date, not the top-up's.
    // "This category already reconciled to a date near the top-up's own date" is treated as
    // evidence the same payment covered the top-up too, rather than requiring an exact match.
    [Fact]
    public async Task CategorizedOneTimeEvent_IsExcluded_WhenItsCategoryHasANearbyReconciledTransaction()
    {
        await SeedCheckingBalanceAsync(5000m, new DateOnly(2026, 8, 1));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();
        var water = new Category { Name = "Water" };
        Context.Categories.Add(water);
        await Context.SaveChangesAsync();

        Context.OneTimeEvents.Add(new OneTimeEvent
        {
            Name = "Water additional", Amount = 185.20m, Direction = Direction.Expense,
            Date = new DateOnly(2026, 7, 30), AccountId = checking.Id, CategoryId = water.Id
        });
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 31), PostedDate = new DateOnly(2026, 7, 31),
            Description = "GWINNETT CTY WATER", Amount = -378.20m, ImportSource = "Test", CategoryId = water.Id,
            ReconciledOccurrenceDate = new DateOnly(2026, 7, 20), CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 31));

        // Stays visible (struck through), same treatment as every other reconciliation
        // reason - only ages out of the ledger after ExcludedPaymentVisibilityDays, not
        // immediately just because it reconciled.
        var row = Assert.Single(result.Rows, r => r.Description == "Water additional");
        Assert.True(row.IsExcluded);
        Assert.Equal(ConfirmationReason.AutoReconciled, row.ExclusionReason);
    }

    [Fact]
    public async Task CategorizedOneTimeEvent_StaysProjected_WhenTheNearestReconciledTransactionIsTooFarInDate()
    {
        await SeedCheckingBalanceAsync(5000m, new DateOnly(2026, 8, 1));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();
        var water = new Category { Name = "Water" };
        Context.Categories.Add(water);
        await Context.SaveChangesAsync();

        Context.OneTimeEvents.Add(new OneTimeEvent
        {
            Name = "Water additional", Amount = 185.20m, Direction = Direction.Expense,
            Date = new DateOnly(2026, 7, 30), AccountId = checking.Id, CategoryId = water.Id
        });
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 6, 1), PostedDate = new DateOnly(2026, 6, 1),
            Description = "GWINNETT CTY WATER", Amount = -193.00m, ImportSource = "Test", CategoryId = water.Id,
            ReconciledOccurrenceDate = new DateOnly(2026, 6, 1), CreatedAt = DateTimeOffset.UtcNow // far outside the 14-day window
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 31));

        Assert.Contains(result.Rows, r => r.Description == "Water additional");
    }

    [Fact]
    public async Task CategorizedOneTimeEvent_StaysProjected_WhenTheReconciledTransactionIsADifferentCategory()
    {
        await SeedCheckingBalanceAsync(5000m, new DateOnly(2026, 8, 1));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();
        var water = new Category { Name = "Water" };
        var gas = new Category { Name = "Gas" };
        Context.Categories.AddRange(water, gas);
        await Context.SaveChangesAsync();

        Context.OneTimeEvents.Add(new OneTimeEvent
        {
            Name = "Water additional", Amount = 185.20m, Direction = Direction.Expense,
            Date = new DateOnly(2026, 7, 30), AccountId = checking.Id, CategoryId = water.Id
        });
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 31), PostedDate = new DateOnly(2026, 7, 31),
            Description = "GAS BILL", Amount = -378.20m, ImportSource = "Test", CategoryId = gas.Id,
            ReconciledOccurrenceDate = new DateOnly(2026, 7, 20), CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 31));

        Assert.Contains(result.Rows, r => r.Description == "Water additional");
    }

    [Fact]
    public async Task CategorizedOneTimeEvent_StaysProjected_WhenTheReconciledTransactionsAmountIsTooSmall()
    {
        // A real payment must actually be large enough to plausibly cover the top-up too, not
        // just any reconciled transaction nearby - same "floor, not a band" tolerance the
        // recurring-line check uses.
        await SeedCheckingBalanceAsync(5000m, new DateOnly(2026, 8, 1));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();
        var water = new Category { Name = "Water" };
        Context.Categories.Add(water);
        await Context.SaveChangesAsync();

        Context.OneTimeEvents.Add(new OneTimeEvent
        {
            Name = "Water additional", Amount = 185.20m, Direction = Direction.Expense,
            Date = new DateOnly(2026, 7, 30), AccountId = checking.Id, CategoryId = water.Id
        });
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 31), PostedDate = new DateOnly(2026, 7, 31),
            Description = "GWINNETT CTY WATER", Amount = -50.00m, ImportSource = "Test", CategoryId = water.Id,
            ReconciledOccurrenceDate = new DateOnly(2026, 7, 20), CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 31));

        Assert.Contains(result.Rows, r => r.Description == "Water additional");
    }

    [Fact]
    public async Task AmexCycle_AutoReconciles_WhenARealAmexPaymentTransactionHasPosted()
    {
        // Amex already has its own "Amex Payment" category/merchant rules like any other
        // account (see AccountManagementService.CreateAccountAsync) - a real payment gets
        // categorized into it just like a Chase/Discover payment would.
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 3, 16));
        var amex = new Account
        {
            Name = "Amex", Type = AccountType.ActiveSpending, ExtraPayment = 0m,
            StatementCloseDay = 25, PaymentDueDay = 15
        };
        Context.Accounts.Add(amex);
        await Context.SaveChangesAsync();

        var groceries = new Category { Name = "Groceries" };
        var amexPayment = new Category { Name = "Amex Payment" };
        Context.Categories.AddRange(groceries, amexPayment);
        await Context.SaveChangesAsync();

        Context.FundingRules.AddRange(
            new FundingRule { CategoryId = groceries.Id, Strategy = FundingStrategies.PayInFullAmex },
            new FundingRule { CategoryId = amexPayment.Id, Strategy = FundingStrategies.AccountPayment, AccountId = amex.Id });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = groceries.Id, Amount = 900m, Frequency = Frequency.Monthly, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        Context.BankTransactions.AddRange(
            new BankTransaction
            {
                AccountId = amex.Id, TransactionDate = new DateOnly(2026, 2, 1), PostedDate = new DateOnly(2026, 2, 1),
                Description = "TRADER JOE S", Amount = -1250m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
            },
            new BankTransaction
            {
                AccountId = amex.Id, TransactionDate = new DateOnly(2026, 3, 14), PostedDate = new DateOnly(2026, 3, 14),
                Description = "AMEX EPAYMENT ACH PMT", Amount = 1250m, ImportSource = "Test", CategoryId = amexPayment.Id,
                ReconciledOccurrenceDate = new DateOnly(2026, 3, 15), CreatedAt = DateTimeOffset.UtcNow
            });
        await Context.SaveChangesAsync();

        // asOfDate is just after the 3/15 due date - the real payment already posted 3/14.
        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 31));

        var row = Assert.Single(result.Rows, r => r.Description == "Amex Payment");
        Assert.True(row.IsExcluded);
        Assert.Equal(ConfirmationReason.AutoReconciled, row.ExclusionReason);
    }

    [Fact]
    public async Task AmexCycle_StaysProjected_WhenDueDateHasPassedButNoRealPaymentTransactionExistsYet()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 3, 20));
        var amex = new Account
        {
            Name = "Amex", Type = AccountType.ActiveSpending, ExtraPayment = 0m,
            StatementCloseDay = 25, PaymentDueDay = 15
        };
        Context.Accounts.Add(amex);
        await Context.SaveChangesAsync();

        var groceries = new Category { Name = "Groceries" };
        var amexPayment = new Category { Name = "Amex Payment" };
        Context.Categories.AddRange(groceries, amexPayment);
        await Context.SaveChangesAsync();

        Context.FundingRules.AddRange(
            new FundingRule { CategoryId = groceries.Id, Strategy = FundingStrategies.PayInFullAmex },
            new FundingRule { CategoryId = amexPayment.Id, Strategy = FundingStrategies.AccountPayment, AccountId = amex.Id });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = groceries.Id, Amount = 900m, Frequency = Frequency.Monthly, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 2, 1), PostedDate = new DateOnly(2026, 2, 1),
            Description = "TRADER JOE S", Amount = -1250m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        // asOfDate is 5 days past the 3/15 due date - still within the 14-day grace window,
        // and no real Amex Payment transaction has posted yet.
        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 3, 20), new DateOnly(2026, 3, 31));

        var amexRow = Assert.Single(result.Rows, r => r.Description == "Amex Payment" && r.Date == new DateOnly(2026, 3, 15));
        Assert.Equal(-1250m, amexRow.Amount);
    }

    [Fact]
    public async Task AmexCycle_CatchesAPendingSelfReportedOverage_BeforeItPosts()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 3, 20));
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
        Context.BankTransactions.AddRange(
            new BankTransaction
            {
                AccountId = amex.Id, TransactionDate = new DateOnly(2026, 2, 1), PostedDate = new DateOnly(2026, 2, 1),
                Description = "TRADER JOE S", Amount = -1250m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
            },
            // Seen pending on Amex's site, entered by hand - not posted yet. $1,000 alone
            // already exceeds the $900 budget, so this must be caught before it ever posts.
            new BankTransaction
            {
                AccountId = amex.Id, TransactionDate = new DateOnly(2026, 3, 18), PostedDate = null,
                Description = "MORGAN COMPOUDING", Amount = -1000m, ImportSource = "ManualScreenshot", CreatedAt = DateTimeOffset.UtcNow
            });
        await Context.SaveChangesAsync();

        // Window extended into April so the currently-open cycle (Feb 26-Mar 25, due Apr 15) -
        // the one the 3/18 pending charge actually belongs to - is included, not just the
        // already-closed cycle due 3/15.
        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 3, 20), new DateOnly(2026, 4, 30));

        var closedCycleRow = Assert.Single(result.Rows, r => r.Amount == -1250m);
        Assert.Equal(new DateOnly(2026, 3, 15), closedCycleRow.Date); // unaffected by the pending charge
        Assert.Equal("Amex Payment", closedCycleRow.Description);

        // The currently-open cycle's own line now reflects the real, higher total (pending
        // charge exceeds budget) instead of quietly showing the flat $900 budget figure until
        // it posts - that's the whole point: catch the overage early.
        var openCycleRow = Assert.Single(result.Rows, r => r.Amount == -1000m);
        Assert.Equal(new DateOnly(2026, 4, 15), openCycleRow.Date);
        Assert.Equal("Amex Payment (includes $1,000.00 pending, not yet posted)", openCycleRow.Description);
    }

    [Fact]
    public async Task AmexCycle_CatchesAPendingPlaidReportedOverage_BeforeItPosts()
    {
        // Real bug this guards: a Plaid-imported charge still pending at the source (no
        // PostedDate yet) was invisible to the Amex "how much do I owe" calculation - the
        // same "count while pending" exception that already covered ManualScreenshot
        // charges was never extended to Plaid when Plaid was added as a real import source.
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 3, 20));
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
        Context.BankTransactions.AddRange(
            new BankTransaction
            {
                AccountId = amex.Id, TransactionDate = new DateOnly(2026, 2, 1), PostedDate = new DateOnly(2026, 2, 1),
                Description = "TRADER JOE S", Amount = -1250m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
            },
            // Reported by Plaid as still pending (no PostedDate yet) - $1,000 alone already
            // exceeds the $900 budget, so this must be caught before it ever posts.
            new BankTransaction
            {
                AccountId = amex.Id, TransactionDate = new DateOnly(2026, 3, 18), PostedDate = null,
                Description = "Publix", Amount = -1000m, ImportSource = "Plaid", CreatedAt = DateTimeOffset.UtcNow
            });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 3, 20), new DateOnly(2026, 4, 30));

        var openCycleRow = Assert.Single(result.Rows, r => r.Amount == -1000m);
        Assert.Equal(new DateOnly(2026, 4, 15), openCycleRow.Date);
        Assert.Equal("Amex Payment (includes $1,000.00 pending, not yet posted)", openCycleRow.Description);
    }

    [Fact]
    public async Task AmexCycle_StaysAtBudget_WhenPendingSelfReportedChargesDontExceedIt()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 3, 20));
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
        // Only $131.65 pending, well under the $900 budget.
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 3, 18), PostedDate = null,
            Description = "MORGAN COMPOUDING", Amount = -131.65m, ImportSource = "ManualScreenshot", CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 3, 20), new DateOnly(2026, 4, 30));

        var openCycleRow = Assert.Single(result.Rows, r => r.Date == new DateOnly(2026, 4, 15));
        Assert.Equal(-900m, openCycleRow.Amount); // budget floor still wins - not yet an overage
        Assert.Equal("Amex Payment (includes $131.65 pending, not yet posted)", openCycleRow.Description);
    }

    [Fact]
    public async Task NoPendingAnnotation_WhenThereAreNoOpenManuallyEnteredCharges()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 3, 20));
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
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 2, 1), PostedDate = new DateOnly(2026, 2, 1),
            Description = "TRADER JOE S", Amount = -1250m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 3, 20), new DateOnly(2026, 3, 31));

        Assert.DoesNotContain(result.Rows, r => r.Description.Contains("pending"));
    }

    [Fact]
    public async Task PendingSelfReportedCharges_AffectOnlyTheirOwnCycle_NotAnUnrelatedDeferredCycle()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 3, 20));
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
        Context.BankTransactions.AddRange(
            new BankTransaction
            {
                AccountId = amex.Id, TransactionDate = new DateOnly(2026, 2, 1), PostedDate = new DateOnly(2026, 2, 1),
                Description = "TRADER JOE S", Amount = -1250m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
            },
            new BankTransaction
            {
                AccountId = amex.Id, TransactionDate = new DateOnly(2026, 3, 18), PostedDate = null,
                Description = "MORGAN COMPOUDING", Amount = -131.65m, ImportSource = "ManualScreenshot", CreatedAt = DateTimeOffset.UtcNow
            });
        // Defers the already-closed cycle's due date (3/15) to 3/20 - must not touch the
        // currently-open cycle's own (unrelated) line, even though both lines share the same
        // AccountId and, before deferral, the closed cycle used to share 3/15 with nothing else.
        Context.PaymentDeferrals.Add(new PaymentDeferral
        {
            AccountId = amex.Id, OriginalDate = new DateOnly(2026, 3, 15), DeferredToDate = new DateOnly(2026, 3, 20), CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        // Window extended into April so the currently-open cycle (Feb 26-Mar 25, due Apr 15) -
        // the one the 3/18 pending charge actually belongs to - is included, not just the
        // already-closed, deferred cycle due 3/15 (now moved to 3/20).
        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 3, 20), new DateOnly(2026, 4, 30));

        var realCycleRow = Assert.Single(result.Rows, r => r.Amount == -1250m);
        Assert.True(realCycleRow.IsDeferred);
        Assert.Equal(new DateOnly(2026, 3, 20), realCycleRow.Date);
        Assert.Equal("Amex Payment", realCycleRow.Description);

        // The currently-open cycle's own line, unaffected by the unrelated closed cycle's
        // deferral even though both once shared 3/15 before the deferral moved one of them.
        var openCycleRow = Assert.Single(result.Rows, r => r.Date == new DateOnly(2026, 4, 15));
        Assert.False(openCycleRow.IsDeferred);
        Assert.Equal(-900m, openCycleRow.Amount); // $131.65 pending stays under the $900 budget
        Assert.Equal("Amex Payment (includes $131.65 pending, not yet posted)", openCycleRow.Description);
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
    public async Task DirectCategory_AlreadyPaidEarly_IsExcludedFromTheForecast_NoDoubleCount()
    {
        // Mortgage anchored for the 15th, but the real payment already posted on the 13th
        // (asOfDate is the 14th) - the forecast must not also project it on the 15th.
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 13));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var mortgage = new Category { Name = "Truist" };
        Context.Categories.Add(mortgage);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = mortgage.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = mortgage.Id, Amount = 2681.22m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 7, 15), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 13), PostedDate = new DateOnly(2026, 7, 13),
            Description = "TRUIST MORTGAGE", Amount = -2681.22m, ImportSource = "Test", CategoryId = mortgage.Id,
            ReconciledOccurrenceDate = new DateOnly(2026, 7, 15), CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows);
        Assert.True(row.IsExcluded);
        Assert.Equal(ConfirmationReason.AutoReconciled, row.ExclusionReason);
    }

    // Real gap this guards (found live 2026-08-04, user-identified): before this fix, a
    // regular recurring bill (unlike a manual Confirm Paid/Override) vanished from the
    // ledger the instant it reconciled - no strikethrough moment at all, just gone, forcing
    // a trip to Transactions to piece together what happened. Now every reconciliation
    // reason shares the same visible-then-fades behavior: it must disappear once it's
    // genuinely aged past the visibility window, but that's the *window* doing it, not
    // reconciliation itself silently omitting the row.
    [Fact]
    public async Task DirectCategory_AutoReconciled_AgesOutOfTheLedger_OnlyAfterTheVisibilityWindow_NotImmediately()
    {
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 22));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var mortgage = new Category { Name = "Truist" };
        Context.Categories.Add(mortgage);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = mortgage.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = mortgage.Id, Amount = 2681.22m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 7, 15), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 13), PostedDate = new DateOnly(2026, 7, 13),
            Description = "TRUIST MORTGAGE", Amount = -2681.22m, ImportSource = "Test", CategoryId = mortgage.Id,
            ReconciledOccurrenceDate = new DateOnly(2026, 7, 15), CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        // asOfDate is 8 days after the reconciled date - one day past the 7-day visibility window.
        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 23), new DateOnly(2026, 8, 10));

        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task DirectCategory_PastDueAndNotYetPaid_StaysProjectedRatherThanSilentlyDropping()
    {
        // Anchored for the 10th, asOfDate is the 14th (4 days overdue) - no matching
        // transaction has posted, so it must still show as still-owed, not vanish.
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 13));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var mortgage = new Category { Name = "Truist" };
        Context.Categories.Add(mortgage);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = mortgage.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = mortgage.Id, Amount = 2681.22m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 7, 10), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows);
        Assert.Equal(new DateOnly(2026, 7, 10), row.Date);
        Assert.Equal(-2681.22m, row.Amount);
    }

    [Fact]
    public async Task DirectCategory_PaidLate_IsExcludedOnceTheRealTransactionPosts()
    {
        // Anchored for the 10th, actually posted on the 12th - even though it posted
        // after its due date, it's still a match and must be excluded.
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 13));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var mortgage = new Category { Name = "Truist" };
        Context.Categories.Add(mortgage);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = mortgage.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = mortgage.Id, Amount = 2681.22m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 7, 10), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 12), PostedDate = new DateOnly(2026, 7, 12),
            Description = "TRUIST MORTGAGE", Amount = -2681.22m, ImportSource = "Test", CategoryId = mortgage.Id,
            ReconciledOccurrenceDate = new DateOnly(2026, 7, 10), CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows);
        Assert.True(row.IsExcluded);
        Assert.Equal(ConfirmationReason.AutoReconciled, row.ExclusionReason);
    }

    [Fact]
    public async Task DirectCategory_AnOldUnrelatedTransactionOutsideTheMatchWindow_DoesNotFalselyExcludeIt()
    {
        // A same-category transaction from two months ago (last month's mortgage payment)
        // must not be mistaken for satisfying this month's occurrence.
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 13));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var mortgage = new Category { Name = "Truist" };
        Context.Categories.Add(mortgage);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = mortgage.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = mortgage.Id, Amount = 2681.22m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 7, 10), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 6, 10), PostedDate = new DateOnly(2026, 6, 10),
            Description = "TRUIST MORTGAGE", Amount = -2681.22m, ImportSource = "Test", CategoryId = mortgage.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows);
        Assert.Equal(new DateOnly(2026, 7, 10), row.Date);
    }

    [Fact]
    public async Task DebtAccountPayment_AlreadyPaidEarly_IsExcludedFromTheForecast_ViaItsLinkedCategory()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var discover = new Account { Name = "Discover", Type = AccountType.Debt, MinPayment = 50m, ExtraPayment = 100m, PaymentDueDay = 15 };
        Context.Accounts.Add(discover);
        await Context.SaveChangesAsync();

        var discoverPayment = new Category { Name = "Discover Payment" };
        Context.Categories.Add(discoverPayment);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = discoverPayment.Id, Strategy = FundingStrategies.AccountPayment, AccountId = discover.Id });
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = discover.Id, TransactionDate = new DateOnly(2026, 7, 13), PostedDate = new DateOnly(2026, 7, 13),
            Description = "DISCOVER PAYMENT", Amount = -150m, ImportSource = "Test", CategoryId = discoverPayment.Id,
            ReconciledOccurrenceDate = new DateOnly(2026, 7, 15), CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows);
        Assert.True(row.IsExcluded);
        Assert.Equal(ConfirmationReason.AutoReconciled, row.ExclusionReason);
    }

    [Fact]
    public async Task DebtAccountPayment_MatchingTransactionSlightlyLower_StillReconciles_WithinTolerance()
    {
        // The issuer raised the real minimum payment slightly since this was configured -
        // a small underpayment relative to what's configured shouldn't block reconciliation.
        // Also verifies the real transaction's own amount is what gets shown, not the
        // configured $150 - found live 2026-08-04, a stale budgeted amount displayed next to
        // "AutoReconciled" would misrepresent what actually happened.
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var discover = new Account { Name = "Discover", Type = AccountType.Debt, MinPayment = 50m, ExtraPayment = 100m, PaymentDueDay = 15 };
        Context.Accounts.Add(discover);
        await Context.SaveChangesAsync();

        var discoverPayment = new Category { Name = "Discover Payment" };
        Context.Categories.Add(discoverPayment);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = discoverPayment.Id, Strategy = FundingStrategies.AccountPayment, AccountId = discover.Id });
        Context.BankTransactions.Add(new BankTransaction
        {
            // Configured/expected is $150; real payment is $145 - a 3.3% shortfall, within tolerance.
            AccountId = discover.Id, TransactionDate = new DateOnly(2026, 7, 13), PostedDate = new DateOnly(2026, 7, 13),
            Description = "DISCOVER PAYMENT", Amount = -145m, ImportSource = "Test", CategoryId = discoverPayment.Id,
            ReconciledOccurrenceDate = new DateOnly(2026, 7, 15), CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows);
        Assert.True(row.IsExcluded);
        Assert.Equal(ConfirmationReason.AutoReconciled, row.ExclusionReason);
        Assert.Equal(-145m, row.Amount);
    }

    [Fact]
    public async Task DebtAccountPayment_MatchingTransactionHigher_StillReconciles_NoUpperBound()
    {
        // Paid extra toward principal one month - overpaying must never block reconciliation.
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var discover = new Account { Name = "Discover", Type = AccountType.Debt, MinPayment = 50m, ExtraPayment = 100m, PaymentDueDay = 15 };
        Context.Accounts.Add(discover);
        await Context.SaveChangesAsync();

        var discoverPayment = new Category { Name = "Discover Payment" };
        Context.Categories.Add(discoverPayment);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = discoverPayment.Id, Strategy = FundingStrategies.AccountPayment, AccountId = discover.Id });
        Context.BankTransactions.Add(new BankTransaction
        {
            // Configured/expected is $150; real payment is $500 (way more than 5% over).
            AccountId = discover.Id, TransactionDate = new DateOnly(2026, 7, 13), PostedDate = new DateOnly(2026, 7, 13),
            Description = "DISCOVER PAYMENT", Amount = -500m, ImportSource = "Test", CategoryId = discoverPayment.Id,
            ReconciledOccurrenceDate = new DateOnly(2026, 7, 15), CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows);
        Assert.True(row.IsExcluded);
        Assert.Equal(ConfirmationReason.AutoReconciled, row.ExclusionReason);
    }

    [Fact]
    public async Task DebtAccountPayment_MatchingTransactionMuchLower_DoesNotReconcile_APartialPaymentIsNotAFullOne()
    {
        // The real-world bug this guards against: a partial payment must not be mistaken
        // for having satisfied the full forecasted amount.
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var discover = new Account { Name = "Discover", Type = AccountType.Debt, MinPayment = 50m, ExtraPayment = 100m, PaymentDueDay = 15 };
        Context.Accounts.Add(discover);
        await Context.SaveChangesAsync();

        var discoverPayment = new Category { Name = "Discover Payment" };
        Context.Categories.Add(discoverPayment);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = discoverPayment.Id, Strategy = FundingStrategies.AccountPayment, AccountId = discover.Id });
        Context.BankTransactions.Add(new BankTransaction
        {
            // Configured/expected is $150; only $60 was actually paid (40%) - well outside tolerance.
            AccountId = discover.Id, TransactionDate = new DateOnly(2026, 7, 13), PostedDate = new DateOnly(2026, 7, 13),
            Description = "DISCOVER PAYMENT", Amount = -60m, ImportSource = "Test", CategoryId = discoverPayment.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows);
        Assert.Equal(-150m, row.Amount);
    }

    [Fact]
    public async Task DebtAccountPayment_PastDueAndNotYetPaid_StaysProjected()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var discover = new Account { Name = "Discover", Type = AccountType.Debt, MinPayment = 50m, ExtraPayment = 100m, PaymentDueDay = 10 };
        Context.Accounts.Add(discover);
        await Context.SaveChangesAsync();

        var discoverPayment = new Category { Name = "Discover Payment" };
        Context.Categories.Add(discoverPayment);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = discoverPayment.Id, Strategy = FundingStrategies.AccountPayment, AccountId = discover.Id });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows);
        Assert.Equal(new DateOnly(2026, 7, 10), row.Date);
        Assert.Equal(-150m, row.Amount);
    }

    [Fact]
    public async Task PartialPayment_ReducesTheRemainingForecastedAmount()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var amex = new Account { Name = "Amex", Type = AccountType.Debt, MinPayment = 2000m, PaymentDueDay = 20 };
        Context.Accounts.Add(amex);
        await Context.SaveChangesAsync();

        var oneTimeEvent = new OneTimeEvent { Name = "Amex Payment (partial)", Amount = 1000m, Direction = Direction.Expense, Date = new DateOnly(2026, 7, 14), AccountId = amex.Id };
        Context.OneTimeEvents.Add(oneTimeEvent);
        await Context.SaveChangesAsync();

        Context.PartialPayments.Add(new PartialPayment
        {
            AccountId = amex.Id, OriginalDate = new DateOnly(2026, 7, 20), Amount = 1000m, PaidDate = new DateOnly(2026, 7, 14),
            OneTimeEventId = oneTimeEvent.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var amexRow = Assert.Single(result.Rows, r => r.AccountId == amex.Id && r.OriginalDate == new DateOnly(2026, 7, 20));
        Assert.Equal(-1000m, amexRow.Amount);
    }

    [Fact]
    public async Task PartialPayment_AppearsAsItsOwnSeparateLedgerLine_OnItsPaidDate()
    {
        // PartialPaymentService always records the paid amount as a real OneTimeEvent too -
        // the actual cash impact must show up on its own date, not just reduce the bill.
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var amex = new Account { Name = "Amex", Type = AccountType.Debt, MinPayment = 2000m, PaymentDueDay = 20 };
        Context.Accounts.Add(amex);
        await Context.SaveChangesAsync();

        var oneTimeEvent = new OneTimeEvent { Name = "Amex Payment (partial)", Amount = 1000m, Direction = Direction.Expense, Date = new DateOnly(2026, 7, 14), AccountId = amex.Id };
        Context.OneTimeEvents.Add(oneTimeEvent);
        await Context.SaveChangesAsync();

        Context.PartialPayments.Add(new PartialPayment
        {
            AccountId = amex.Id, OriginalDate = new DateOnly(2026, 7, 20), Amount = 1000m, PaidDate = new DateOnly(2026, 7, 14),
            OneTimeEventId = oneTimeEvent.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var paidRow = Assert.Single(result.Rows, r => r.Description == "Amex Payment (partial)");
        Assert.Equal(new DateOnly(2026, 7, 14), paidRow.Date);
        Assert.Equal(-1000m, paidRow.Amount);
        Assert.False(paidRow.IsExcluded); // no real posted transaction has matched it yet
    }

    [Fact]
    public async Task PartialPayment_IsExcluded_OnceARealMatchingTransactionPosts()
    {
        // Same real cash movement the partial payment recorded has now posted and synced
        // normally - the recorded line must stop double-counting it. Real shape confirmed
        // this session: the actual payment posts against the CHECKING account (never the
        // debt/Amex account's own id), tagged with that account's payment category, and
        // is stored as a negative amount (this app's expense convention) - not the debt
        // account's own AccountId with a positive amount, which was this test's old,
        // unrealistic assumption before the underlying bug was found and fixed.
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 21));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        var amex = new Account { Name = "Amex", Type = AccountType.Debt, MinPayment = 2000m, PaymentDueDay = 20 };
        Context.Accounts.AddRange(checking, amex);
        await Context.SaveChangesAsync();

        var amexPayment = new Category { Name = "Amex Payment" };
        Context.Categories.Add(amexPayment);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = amexPayment.Id, Strategy = FundingStrategies.AccountPayment, AccountId = amex.Id });

        var oneTimeEvent = new OneTimeEvent { Name = "Amex Payment (partial)", Amount = 1000m, Direction = Direction.Expense, Date = new DateOnly(2026, 7, 20), AccountId = amex.Id };
        Context.OneTimeEvents.Add(oneTimeEvent);
        await Context.SaveChangesAsync();

        Context.PartialPayments.Add(new PartialPayment
        {
            AccountId = amex.Id, OriginalDate = new DateOnly(2026, 7, 20), Amount = 1000m, PaidDate = new DateOnly(2026, 7, 20),
            OneTimeEventId = oneTimeEvent.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 20), PostedDate = new DateOnly(2026, 7, 20),
            Description = "ONLINE PAYMENT - THANK YOU", Amount = -1000m, ImportSource = "SimpleFin", CategoryId = amexPayment.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 21), new DateOnly(2026, 7, 31));

        var paidRow = Assert.Single(result.Rows, r => r.Description.StartsWith("Amex Payment (partial)"));
        Assert.True(paidRow.IsExcluded);
        Assert.Equal(ConfirmationReason.AutoReconciled, paidRow.ExclusionReason);
        Assert.Contains("matched a real posted payment on 07/20/2026", paidRow.Description);
    }

    [Fact]
    public async Task PartialPayment_ReconciliationMatch_RequiresTheExactAmount()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 21));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        var amex = new Account { Name = "Amex", Type = AccountType.Debt, MinPayment = 2000m, PaymentDueDay = 20 };
        Context.Accounts.AddRange(checking, amex);
        await Context.SaveChangesAsync();

        var amexPayment = new Category { Name = "Amex Payment" };
        Context.Categories.Add(amexPayment);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = amexPayment.Id, Strategy = FundingStrategies.AccountPayment, AccountId = amex.Id });

        var oneTimeEvent = new OneTimeEvent { Name = "Amex Payment (partial)", Amount = 1000m, Direction = Direction.Expense, Date = new DateOnly(2026, 7, 20), AccountId = amex.Id };
        Context.OneTimeEvents.Add(oneTimeEvent);
        await Context.SaveChangesAsync();

        Context.PartialPayments.Add(new PartialPayment
        {
            AccountId = amex.Id, OriginalDate = new DateOnly(2026, 7, 20), Amount = 1000m, PaidDate = new DateOnly(2026, 7, 20),
            OneTimeEventId = oneTimeEvent.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        // A real transaction close in date but for a different amount - must not match.
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 20), PostedDate = new DateOnly(2026, 7, 20),
            Description = "ONLINE PAYMENT - THANK YOU", Amount = -850m, ImportSource = "SimpleFin", CategoryId = amexPayment.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 21), new DateOnly(2026, 7, 31));

        var paidRow = Assert.Single(result.Rows, r => r.Description == "Amex Payment (partial)");
        Assert.False(paidRow.IsExcluded);
    }

    [Fact]
    public async Task PartialPayment_AgainstADirectFundedBill_StillMatches_ViaAccountIdFallback()
    {
        // Unlike a debt/Amex-payment partial payment, a Direct-funded bill's own AccountId
        // IS the checking/funding account the real transaction actually posts against (no
        // separate debt/Amex account in between) - so AccountId-based matching is already
        // correct here and must keep working via the fallback path.
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 21));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var mortgage = new Category { Name = "Truist" };
        Context.Categories.Add(mortgage);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = mortgage.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = mortgage.Id, Amount = 2681.22m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 7, 20), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });

        var oneTimeEvent = new OneTimeEvent { Name = "Truist Payment (partial)", Amount = 1000m, Direction = Direction.Expense, Date = new DateOnly(2026, 7, 18), AccountId = checking.Id };
        Context.OneTimeEvents.Add(oneTimeEvent);
        await Context.SaveChangesAsync();

        Context.PartialPayments.Add(new PartialPayment
        {
            AccountId = checking.Id, OriginalDate = new DateOnly(2026, 7, 20), Amount = 1000m, PaidDate = new DateOnly(2026, 7, 18),
            OneTimeEventId = oneTimeEvent.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 18), PostedDate = new DateOnly(2026, 7, 18),
            Description = "PARTIAL MORTGAGE PAYMENT", Amount = -1000m, ImportSource = "SimpleFin", CategoryId = null, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 21), new DateOnly(2026, 7, 31));

        var paidRow = Assert.Single(result.Rows, r => r.Description.StartsWith("Truist Payment (partial)"));
        Assert.True(paidRow.IsExcluded);
        Assert.Equal(ConfirmationReason.AutoReconciled, paidRow.ExclusionReason);
    }

    [Fact]
    public async Task MultiplePartialPayments_AgainstTheSameOccurrence_SumTogether()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var amex = new Account { Name = "Amex", Type = AccountType.Debt, MinPayment = 2000m, PaymentDueDay = 20 };
        Context.Accounts.Add(amex);
        await Context.SaveChangesAsync();

        var event1 = new OneTimeEvent { Name = "Amex Payment (partial)", Amount = 500m, Direction = Direction.Expense, Date = new DateOnly(2026, 7, 14), AccountId = amex.Id };
        var event2 = new OneTimeEvent { Name = "Amex Payment (partial)", Amount = 300m, Direction = Direction.Expense, Date = new DateOnly(2026, 7, 17), AccountId = amex.Id };
        Context.OneTimeEvents.AddRange(event1, event2);
        await Context.SaveChangesAsync();

        Context.PartialPayments.AddRange(
            new PartialPayment { AccountId = amex.Id, OriginalDate = new DateOnly(2026, 7, 20), Amount = 500m, PaidDate = new DateOnly(2026, 7, 14), OneTimeEventId = event1.Id, CreatedAt = DateTimeOffset.UtcNow },
            new PartialPayment { AccountId = amex.Id, OriginalDate = new DateOnly(2026, 7, 20), Amount = 300m, PaidDate = new DateOnly(2026, 7, 17), OneTimeEventId = event2.Id, CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var amexRow = Assert.Single(result.Rows, r => r.AccountId == amex.Id && r.OriginalDate == new DateOnly(2026, 7, 20));
        Assert.Equal(-1200m, amexRow.Amount); // 2000 - 500 - 300
        Assert.Equal(2, amexRow.PartialPayments.Count);
    }

    [Fact]
    public async Task PartialPayment_ComposesWithADeferral_ReducingAmountWithoutAffectingTheNewDate()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var amex = new Account { Name = "Amex", Type = AccountType.Debt, MinPayment = 2000m, PaymentDueDay = 20 };
        Context.Accounts.Add(amex);
        await Context.SaveChangesAsync();

        Context.PaymentDeferrals.Add(new PaymentDeferral
        {
            AccountId = amex.Id, OriginalDate = new DateOnly(2026, 7, 20), DeferredToDate = new DateOnly(2026, 7, 27), CreatedAt = DateTimeOffset.UtcNow
        });

        var oneTimeEvent = new OneTimeEvent { Name = "Amex Payment (partial)", Amount = 1000m, Direction = Direction.Expense, Date = new DateOnly(2026, 7, 14), AccountId = amex.Id };
        Context.OneTimeEvents.Add(oneTimeEvent);
        await Context.SaveChangesAsync();

        Context.PartialPayments.Add(new PartialPayment
        {
            AccountId = amex.Id, OriginalDate = new DateOnly(2026, 7, 20), Amount = 1000m, PaidDate = new DateOnly(2026, 7, 14),
            OneTimeEventId = oneTimeEvent.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var amexRow = Assert.Single(result.Rows, r => r.AccountId == amex.Id && r.OriginalDate == new DateOnly(2026, 7, 20));
        Assert.Equal(new DateOnly(2026, 7, 27), amexRow.Date);
        Assert.True(amexRow.IsDeferred);
        Assert.Equal(-1000m, amexRow.Amount);
    }

    [Fact]
    public async Task PartialPayment_PaidOnTheSameDateAsAnUnrelatedDeferral_DoesNotGetSweptIntoIt()
    {
        // Real-world bug: paying (partially) on the bill's original due date, which already
        // has its own deferral, must not cause the auto-created "cash paid" OneTimeEvent to
        // be treated as the SAME occurrence as the deferred bill just because it happens to
        // share the same account and date.
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var amex = new Account { Name = "Amex", Type = AccountType.Debt, MinPayment = 2000m, PaymentDueDay = 20 };
        Context.Accounts.Add(amex);
        await Context.SaveChangesAsync();

        Context.PaymentDeferrals.Add(new PaymentDeferral
        {
            AccountId = amex.Id, OriginalDate = new DateOnly(2026, 7, 20), DeferredToDate = new DateOnly(2026, 7, 25), CreatedAt = DateTimeOffset.UtcNow
        });

        // Paid on 7/20 - the bill's own original due date, same as the existing deferral.
        var oneTimeEvent = new OneTimeEvent { Name = "Amex Payment (partial)", Amount = 1000m, Direction = Direction.Expense, Date = new DateOnly(2026, 7, 20), AccountId = amex.Id };
        Context.OneTimeEvents.Add(oneTimeEvent);
        await Context.SaveChangesAsync();

        Context.PartialPayments.Add(new PartialPayment
        {
            AccountId = amex.Id, OriginalDate = new DateOnly(2026, 7, 20), Amount = 1000m, PaidDate = new DateOnly(2026, 7, 20),
            OneTimeEventId = oneTimeEvent.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var paidRow = Assert.Single(result.Rows, r => r.Description == "Amex Payment (partial)");
        Assert.Equal(new DateOnly(2026, 7, 20), paidRow.Date); // shows on the real paid date, not swept into the deferral
        Assert.False(paidRow.IsDeferred);
        Assert.Equal(-1000m, paidRow.Amount); // its own amount, not reduced by matching itself
        Assert.Empty(paidRow.PartialPayments);

        var amexRow = Assert.Single(result.Rows, r => r.Description == "Amex Payment");
        Assert.Equal(new DateOnly(2026, 7, 25), amexRow.Date);
        Assert.True(amexRow.IsDeferred);
        Assert.Equal(-1000m, amexRow.Amount); // 2000 - 1000 partial payment, correctly applied to the real bill
    }

    [Fact]
    public async Task PartialPayment_ShowsUpOnTheRow_WithIdAndDateForUndo()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var amex = new Account { Name = "Amex", Type = AccountType.Debt, MinPayment = 2000m, PaymentDueDay = 20 };
        Context.Accounts.Add(amex);
        await Context.SaveChangesAsync();

        var oneTimeEvent = new OneTimeEvent { Name = "Amex Payment (partial)", Amount = 1000m, Direction = Direction.Expense, Date = new DateOnly(2026, 7, 14), AccountId = amex.Id };
        Context.OneTimeEvents.Add(oneTimeEvent);
        await Context.SaveChangesAsync();

        var partialPayment = new PartialPayment
        {
            AccountId = amex.Id, OriginalDate = new DateOnly(2026, 7, 20), Amount = 1000m, PaidDate = new DateOnly(2026, 7, 14),
            OneTimeEventId = oneTimeEvent.Id, CreatedAt = DateTimeOffset.UtcNow
        };
        Context.PartialPayments.Add(partialPayment);
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var amexRow = Assert.Single(result.Rows, r => r.AccountId == amex.Id && r.OriginalDate == new DateOnly(2026, 7, 20));
        var summary = Assert.Single(amexRow.PartialPayments);
        Assert.Equal(partialPayment.Id, summary.PartialPaymentId);
        Assert.Equal(1000m, summary.Amount);
        Assert.Equal(new DateOnly(2026, 7, 14), summary.PaidDate);
    }

    [Fact]
    public async Task PartialPayment_ForADifferentOccurrence_DoesNotAffectThisOne()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var amex = new Account { Name = "Amex", Type = AccountType.Debt, MinPayment = 2000m, PaymentDueDay = 20 };
        Context.Accounts.Add(amex);
        await Context.SaveChangesAsync();

        var oneTimeEvent = new OneTimeEvent { Name = "Amex Payment (partial)", Amount = 1000m, Direction = Direction.Expense, Date = new DateOnly(2026, 6, 14), AccountId = amex.Id };
        Context.OneTimeEvents.Add(oneTimeEvent);
        await Context.SaveChangesAsync();

        // A partial payment against last month's occurrence, not this month's.
        Context.PartialPayments.Add(new PartialPayment
        {
            AccountId = amex.Id, OriginalDate = new DateOnly(2026, 6, 20), Amount = 1000m, PaidDate = new DateOnly(2026, 6, 14),
            OneTimeEventId = oneTimeEvent.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var amexRow = Assert.Single(result.Rows, r => r.AccountId == amex.Id && r.OriginalDate == new DateOnly(2026, 7, 20));
        Assert.Equal(-2000m, amexRow.Amount);
        Assert.Empty(amexRow.PartialPayments);
    }

    [Fact]
    public async Task ManuallyConfirmedPayment_StaysInTheLedgerMarkedExcluded_EvenWithNoMatchingTransaction()
    {
        // Chase-shaped case: no merchant rule can ever tell this account's real payments
        // apart from another card's, so the user confirms it by hand instead.
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var chaseAmazon = new Account { Name = "Chase Amazon Prime Visa", Type = AccountType.Debt, MinPayment = 357m, PaymentDueDay = 7 };
        Context.Accounts.Add(chaseAmazon);
        await Context.SaveChangesAsync();

        Context.PaymentConfirmations.Add(new PaymentConfirmation
        {
            AccountId = chaseAmazon.Id, OriginalDate = new DateOnly(2026, 7, 7), EffectiveDate = new DateOnly(2026, 7, 7),
            Amount = -357m, Reason = ConfirmationReason.AlreadyPaid, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows);
        Assert.True(row.IsExcluded);
        Assert.Equal(ConfirmationReason.AlreadyPaid, row.ExclusionReason);
        Assert.Equal(-357m, row.Amount);
        Assert.Equal(3000m, row.RunningBalance); // unaffected - the excluded amount never counts toward the balance
    }

    [Fact]
    public async Task ManuallyOverriddenPayment_StaysInTheLedgerMarkedExcluded_EvenThoughItWasNeverActuallyPaidAsScheduled()
    {
        // Split-payment case: the user is replacing this occurrence with their own plan
        // (e.g. two One-Time Events), not claiming it was paid as originally scheduled.
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var amex = new Account { Name = "Amex", Type = AccountType.Debt, MinPayment = 2000m, PaymentDueDay = 20 };
        Context.Accounts.Add(amex);
        await Context.SaveChangesAsync();

        Context.PaymentConfirmations.Add(new PaymentConfirmation
        {
            AccountId = amex.Id, OriginalDate = new DateOnly(2026, 7, 20), EffectiveDate = new DateOnly(2026, 7, 20),
            Amount = -2000m, Reason = ConfirmationReason.Overridden, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows);
        Assert.True(row.IsExcluded);
        Assert.Equal(ConfirmationReason.Overridden, row.ExclusionReason);
        Assert.Equal(-2000m, row.Amount);
    }

    // Real bug this guards (found live 2026-08-04, user-identified): a confirmation for one
    // bill (Water, a one-time top-up) was silently "inherited" by an unrelated recurring
    // income line (MAS) that just happened to share the same account and date - the
    // confirmation lookup only ever keyed on (AccountId, OriginalDate), with nothing to tell
    // two different same-day/same-account lines apart. CategoryId is what disambiguates them.
    [Fact]
    public async Task ConfirmationForOneCategory_IsNotIncorrectlyInheritedByAnUnrelatedLine_SharingTheSameAccountAndDate()
    {
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 29));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var water = new Category { Name = "Water" };
        var mas = new Category { Name = "MAS" };
        Context.Categories.AddRange(water, mas);
        await Context.SaveChangesAsync();

        Context.FundingRules.AddRange(
            new FundingRule { CategoryId = water.Id, Strategy = FundingStrategies.Direct },
            new FundingRule { CategoryId = mas.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.AddRange(
            new BudgetPeriod
            {
                CategoryId = water.Id, Amount = 193m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
                Anchor = new DateOnly(2026, 7, 30), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
            },
            new BudgetPeriod
            {
                CategoryId = mas.Id, Amount = 230m, Frequency = Frequency.Monthly, Direction = Direction.Income,
                Anchor = new DateOnly(2026, 7, 30), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
            });
        // Only Water was ever confirmed paid - MAS never was.
        Context.PaymentConfirmations.Add(new PaymentConfirmation
        {
            AccountId = checking.Id, CategoryId = water.Id, OriginalDate = new DateOnly(2026, 7, 30), EffectiveDate = new DateOnly(2026, 7, 30),
            Amount = -185.20m, Reason = ConfirmationReason.AlreadyPaid, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 30), new DateOnly(2026, 8, 15));

        var waterRow = Assert.Single(result.Rows, r => r.Description == "Water");
        Assert.True(waterRow.IsExcluded);
        Assert.Equal(ConfirmationReason.AlreadyPaid, waterRow.ExclusionReason);

        var masRow = Assert.Single(result.Rows, r => r.Description == "MAS");
        Assert.False(masRow.IsExcluded);
        Assert.Equal(230m, masRow.Amount);
    }

    [Fact]
    public async Task ManuallyExcludedPayment_CarriesItsConfirmationIdForUndo()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var chaseAmazon = new Account { Name = "Chase Amazon Prime Visa", Type = AccountType.Debt, MinPayment = 357m, PaymentDueDay = 7 };
        Context.Accounts.Add(chaseAmazon);
        await Context.SaveChangesAsync();

        var confirmation = new PaymentConfirmation
        {
            AccountId = chaseAmazon.Id, OriginalDate = new DateOnly(2026, 7, 7), EffectiveDate = new DateOnly(2026, 7, 7),
            Amount = -357m, Reason = ConfirmationReason.AlreadyPaid, CreatedAt = DateTimeOffset.UtcNow
        };
        Context.PaymentConfirmations.Add(confirmation);
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows);
        Assert.Equal(confirmation.Id, row.ConfirmationId);
        Assert.Equal(chaseAmazon.Id, row.AccountId);
        Assert.Equal(new DateOnly(2026, 7, 7), row.OriginalDate);
    }

    [Fact]
    public async Task ManuallyExcludedPayment_DisplaysAtItsCapturedEffectiveDate_NotTheOriginalScheduledDate()
    {
        // Simulates confirming/overriding a payment that had already been deferred to 7/15 at
        // the moment the user acted - the excluded row should stay where the user last saw it,
        // not jump back to the original 7/7 due date.
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var chaseAmazon = new Account { Name = "Chase Amazon Prime Visa", Type = AccountType.Debt, MinPayment = 357m, PaymentDueDay = 7 };
        Context.Accounts.Add(chaseAmazon);
        await Context.SaveChangesAsync();

        Context.PaymentConfirmations.Add(new PaymentConfirmation
        {
            AccountId = chaseAmazon.Id, OriginalDate = new DateOnly(2026, 7, 7), EffectiveDate = new DateOnly(2026, 7, 15),
            Amount = -357m, Reason = ConfirmationReason.AlreadyPaid, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows);
        Assert.Equal(new DateOnly(2026, 7, 15), row.Date);
        Assert.Equal(new DateOnly(2026, 7, 7), row.OriginalDate);
    }

    [Fact]
    public async Task ExcludedPayment_DoesNotAffectTheRunningBalanceOfLaterRows()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var chaseAmazon = new Account { Name = "Chase Amazon Prime Visa", Type = AccountType.Debt, MinPayment = 357m, PaymentDueDay = 7 };
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.AddRange(chaseAmazon, checking);
        await Context.SaveChangesAsync();

        Context.PaymentConfirmations.Add(new PaymentConfirmation
        {
            AccountId = chaseAmazon.Id, OriginalDate = new DateOnly(2026, 7, 7), EffectiveDate = new DateOnly(2026, 7, 7),
            Amount = -357m, Reason = ConfirmationReason.AlreadyPaid, CreatedAt = DateTimeOffset.UtcNow
        });
        Context.OneTimeEvents.Add(
            new OneTimeEvent { Name = "HVAC repair", Amount = 850m, Direction = Direction.Expense, Date = new DateOnly(2026, 7, 20), AccountId = checking.Id });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        Assert.Equal(2, result.Rows.Count);
        var laterRow = result.Rows.Single(r => !r.IsExcluded);
        Assert.Equal(2150m, laterRow.RunningBalance); // 3000 - 850, the excluded 357 never subtracted
    }

    [Fact]
    public async Task PaymentConfirmation_ForADifferentDate_DoesNotAffectOtherOccurrences()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var chaseAmazon = new Account { Name = "Chase Amazon Prime Visa", Type = AccountType.Debt, MinPayment = 357m, PaymentDueDay = 7 };
        Context.Accounts.Add(chaseAmazon);
        await Context.SaveChangesAsync();

        // Confirms a totally different (already-past) month's occurrence, not this month's -
        // it still shows up as its own excluded row (from its own captured snapshot, since no
        // live line matches it this run), but must not touch this month's real occurrence.
        Context.PaymentConfirmations.Add(new PaymentConfirmation
        {
            AccountId = chaseAmazon.Id, OriginalDate = new DateOnly(2026, 6, 7), EffectiveDate = new DateOnly(2026, 6, 7),
            Amount = -357m, Reason = ConfirmationReason.AlreadyPaid, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows, r => !r.IsExcluded);
        Assert.Equal(new DateOnly(2026, 7, 7), row.Date);
    }

    [Fact]
    public async Task PaymentConfirmation_WithNoMatchingLineThisRun_StillShowsAsAnExcludedRow_ForUndo()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var chaseAmazon = new Account { Name = "Chase Amazon Prime Visa", Type = AccountType.Debt, MinPayment = 357m, PaymentDueDay = 7 };
        Context.Accounts.Add(chaseAmazon);
        await Context.SaveChangesAsync();

        // A different month's occurrence, but recent enough (within the 7-day visibility
        // window) that it should still show up.
        var confirmation = new PaymentConfirmation
        {
            AccountId = chaseAmazon.Id, OriginalDate = new DateOnly(2026, 6, 7), EffectiveDate = new DateOnly(2026, 7, 10),
            Amount = -357m, Reason = ConfirmationReason.AlreadyPaid, CreatedAt = DateTimeOffset.UtcNow
        };
        Context.PaymentConfirmations.Add(confirmation);
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows, r => r.IsExcluded);
        Assert.Equal(confirmation.Id, row.ConfirmationId);
        Assert.Equal(-357m, row.Amount);
        Assert.Equal(new DateOnly(2026, 7, 10), row.Date);
    }

    [Fact]
    public async Task ExcludedPayment_OlderThanTheVisibilityWindow_IsHiddenFromTheLedger()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var chaseAmazon = new Account { Name = "Chase Amazon Prime Visa", Type = AccountType.Debt, MinPayment = 357m, PaymentDueDay = 7 };
        Context.Accounts.Add(chaseAmazon);
        await Context.SaveChangesAsync();

        // Effective date is more than 7 days before asOfDate (2026-7-14) - resolved long
        // enough ago that it shouldn't clutter a forward-looking forecast anymore.
        Context.PaymentConfirmations.Add(new PaymentConfirmation
        {
            AccountId = chaseAmazon.Id, OriginalDate = new DateOnly(2026, 7, 7), EffectiveDate = new DateOnly(2026, 7, 6),
            Amount = -357m, Reason = ConfirmationReason.AlreadyPaid, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task ExcludedPayment_WithinTheVisibilityWindow_StillShowsInTheLedger()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        var chaseAmazon = new Account { Name = "Chase Amazon Prime Visa", Type = AccountType.Debt, MinPayment = 357m, PaymentDueDay = 7 };
        Context.Accounts.Add(chaseAmazon);
        await Context.SaveChangesAsync();

        // Exactly 7 days before asOfDate - still within the window (inclusive).
        Context.PaymentConfirmations.Add(new PaymentConfirmation
        {
            AccountId = chaseAmazon.Id, OriginalDate = new DateOnly(2026, 7, 7), EffectiveDate = new DateOnly(2026, 7, 7),
            Amount = -357m, Reason = ConfirmationReason.AlreadyPaid, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GenerateAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var row = Assert.Single(result.Rows);
        Assert.True(row.IsExcluded);
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
