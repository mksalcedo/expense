using Expense.Domain.Entities;
using Expense.Domain.Services.Budgets;
using Expense.Domain.Services.Forecast;
using Expense.Domain.Tests.TestSupport;

namespace Expense.Domain.Tests.Services.Forecast;

public class ForecastAccuracyServiceTests : DatabaseTestBase
{
    private readonly ForecastAccuracyService _sut = new(new RecurrenceExpander(), new AmexCycleCalculator(), new BudgetProrationService());

    [Fact]
    public async Task DirectFundedBill_PostsAtADifferentAmount_FlagsTheDelta()
    {
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var utility = new Category { Name = "GPC" };
        Context.Categories.Add(utility);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = utility.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = utility.Id, Amount = 400m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 7, 5), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 5), PostedDate = new DateOnly(2026, 7, 5),
            Description = "GEORGIA POWER", Amount = -612.50m, ImportSource = "Test", CategoryId = utility.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var results = await _sut.GetRecentAccuracyAsync(Context, new DateOnly(2026, 7, 14), lookbackDays: 90);

        // A 90-day lookback on a Monthly bill naturally spans more than one occurrence -
        // filter to the specific one this test set up a real transaction for.
        var comparison = Assert.Single(results, r => r.Name == "GPC" && r.ScheduledDate == new DateOnly(2026, 7, 5));
        Assert.Equal(400m, comparison.ScheduledAmount);
        Assert.Equal(new DateOnly(2026, 7, 5), comparison.ActualDate);
        Assert.Equal(612.50m, comparison.ActualAmount);
        Assert.Equal(212.50m, comparison.Delta);
    }

    [Fact]
    public async Task DebtAccountPayment_PostsAtTheConfiguredAmount_HasZeroDelta()
    {
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        var discover = new Account
        {
            Name = "Discover", Type = AccountType.Debt, MinPayment = 173m, ExtraPayment = 0m, PaymentDueDay = 3
        };
        Context.Accounts.AddRange(checking, discover);
        await Context.SaveChangesAsync();

        var discoverPayment = new Category { Name = "Discover Payment" };
        Context.Categories.Add(discoverPayment);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = discoverPayment.Id, Strategy = FundingStrategies.AccountPayment, AccountId = discover.Id });
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 3), PostedDate = new DateOnly(2026, 7, 3),
            Description = "DISCOVER PAYMENT", Amount = -173m, ImportSource = "Test", CategoryId = discoverPayment.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var results = await _sut.GetRecentAccuracyAsync(Context, new DateOnly(2026, 7, 14), lookbackDays: 90);

        var comparison = Assert.Single(results, r => r.Name == "Discover Payment" && r.ScheduledDate == new DateOnly(2026, 7, 3));
        Assert.Equal(173m, comparison.ScheduledAmount);
        Assert.Equal(173m, comparison.ActualAmount);
        Assert.Equal(0m, comparison.Delta);
    }

    [Fact]
    public async Task AmexClosedCycle_RealChargesExceedBudget_FlagsTheOverage()
    {
        var amex = new Account
        {
            Name = "Amex", Type = AccountType.ActiveSpending, ExtraPayment = 1100m,
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
        // Cycle: 2026-05-26 to 2026-06-25, due 2026-07-15 - closed well before asOfDate.
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 6, 10), PostedDate = new DateOnly(2026, 6, 10),
            Description = "WHOLE FOODS", Amount = -1400m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var results = await _sut.GetRecentAccuracyAsync(Context, new DateOnly(2026, 7, 20), lookbackDays: 90);

        var comparison = Assert.Single(results, r => r.Name == "Amex Payment" && r.ScheduledDate == new DateOnly(2026, 7, 15));
        Assert.Equal(2000m, comparison.ScheduledAmount); // 900 budget + 1100 extra
        Assert.Equal(2500m, comparison.ActualAmount); // 1400 real charges + 1100 extra
        Assert.Equal(500m, comparison.Delta);
    }

    [Fact]
    public async Task OccurrenceWithNoMatchingTransaction_AfterItsMatchWindowCloses_IsReportedAsUnmatched()
    {
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var utility = new Category { Name = "Water" };
        Context.Categories.Add(utility);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = utility.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = utility.Id, Amount = 150m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 6, 1), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        // Establishes that this database's transaction history genuinely covers the 6/1
        // occurrence's period (an unrelated category/transaction, just to confirm we've had
        // real visibility since before then) - without this, "never observed any data at
        // all" would look identical to "this specific bill never posted".
        var otherCategory = new Category { Name = "Groceries" };
        Context.Categories.Add(otherCategory);
        await Context.SaveChangesAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 5, 1), PostedDate = new DateOnly(2026, 5, 1),
            Description = "KROGER", Amount = -50m, ImportSource = "Test", CategoryId = otherCategory.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        // asOfDate is well past the monthly match window (14 days) for the 6/1 occurrence -
        // nothing ever posted for it, even though we've had real transaction visibility
        // since well before that date.
        var results = await _sut.GetRecentAccuracyAsync(Context, new DateOnly(2026, 7, 14), lookbackDays: 90);

        var comparison = Assert.Single(results, r => r.Name == "Water" && r.ScheduledDate == new DateOnly(2026, 6, 1));
        Assert.Null(comparison.ActualAmount);
        Assert.Null(comparison.ActualDate);
        Assert.False(comparison.WasMatched);
    }

    [Fact]
    public async Task OccurrenceBeforeAnyRealTransactionDataExists_IsNotFalselyReportedAsAMiss()
    {
        // Real scenario this guards: SimpleFin's own sync window only pulls a limited
        // history, so bank_transactions might only go back to (say) 2026-05-31 even though
        // the app itself has existed longer. An occurrence from before that date has no
        // observed data at all - "no match found" there means "we never looked", not "it
        // didn't post" - and must not be shown as a false miss.
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var utility = new Category { Name = "Verizon" };
        Context.Categories.Add(utility);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = utility.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = utility.Id, Amount = 210.49m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 4, 25), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        // The only real transaction data this database has ever seen starts 2026-05-31 -
        // nothing exists for the 4/25 occurrence, and nothing ever will.
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 5, 31), PostedDate = new DateOnly(2026, 5, 31),
            Description = "VERIZON WIRELESS", Amount = -201.17m, ImportSource = "Test", CategoryId = utility.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var results = await _sut.GetRecentAccuracyAsync(Context, new DateOnly(2026, 7, 14), lookbackDays: 90);

        Assert.DoesNotContain(results, r => r.Name == "Verizon" && r.ScheduledDate == new DateOnly(2026, 4, 25));
        Assert.Contains(results, r => r.Name == "Verizon" && r.ScheduledDate == new DateOnly(2026, 5, 25)); // within observed data - still a real comparison
    }

    [Fact]
    public async Task DirectFundedBill_BudgetWasEditedAfterAnOccurrencePosted_UsesTheBudgetInEffectAtTheTime()
    {
        // Real bug this guards: editing a budget today must not rewrite history - a past
        // occurrence should be judged against whatever was actually scheduled back then,
        // not whatever the category's budget happens to be as of asOfDate.
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var utility = new Category { Name = "Perimeter" };
        Context.Categories.Add(utility);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = utility.Id, Strategy = FundingStrategies.Direct });
        // In effect when the 7/15 occurrence actually happened.
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = utility.Id, Amount = 1000m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 7, 15), AccountId = checking.Id,
            EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveThrough = new DateOnly(2026, 7, 21)
        });
        // Edited down to 900 on 7/22 - AFTER the 7/15 payment already posted at 1000.
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = utility.Id, Amount = 900m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 7, 15), AccountId = checking.Id,
            EffectiveFrom = new DateOnly(2026, 7, 22), EffectiveThrough = null
        });
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 21), PostedDate = new DateOnly(2026, 7, 21),
            Description = "PERIMETER", Amount = -1000m, ImportSource = "Test", CategoryId = utility.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var results = await _sut.GetRecentAccuracyAsync(Context, new DateOnly(2026, 7, 24), lookbackDays: 90);

        var comparison = Assert.Single(results, r => r.Name == "Perimeter" && r.ScheduledDate == new DateOnly(2026, 7, 15));
        Assert.Equal(1000m, comparison.ScheduledAmount); // what was actually budgeted then, not today's 900
        Assert.Equal(1000m, comparison.ActualAmount);
        Assert.Equal(0m, comparison.Delta);
    }

    [Fact]
    public async Task AmexCycle_BudgetWasEditedAfterTheCycleClosed_UsesTheBudgetInEffectDuringThatCycle()
    {
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
        // Cycle: 2026-05-26 to 2026-06-25, due 2026-07-15 - budgeted at 900 while it was open.
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = groceries.Id, Amount = 900m, Frequency = Frequency.Monthly,
            EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveThrough = new DateOnly(2026, 7, 21)
        });
        // Edited down to 700 on 7/22 - AFTER that cycle already closed and posted.
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = groceries.Id, Amount = 700m, Frequency = Frequency.Monthly,
            EffectiveFrom = new DateOnly(2026, 7, 22), EffectiveThrough = null
        });
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 6, 10), PostedDate = new DateOnly(2026, 6, 10),
            Description = "WHOLE FOODS", Amount = -800m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var results = await _sut.GetRecentAccuracyAsync(Context, new DateOnly(2026, 7, 24), lookbackDays: 90);

        var comparison = Assert.Single(results, r => r.Name == "Amex Payment" && r.ScheduledDate == new DateOnly(2026, 7, 15));
        Assert.Equal(900m, comparison.ScheduledAmount); // budget in effect during that cycle, not today's 700
        Assert.Equal(800m, comparison.ActualAmount);
        Assert.Equal(-100m, comparison.Delta);
    }

    [Fact]
    public async Task OccurrenceStillWithinItsMatchWindow_IsNotYetReportedAsAMiss()
    {
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var utility = new Category { Name = "AT&T" };
        Context.Categories.Add(utility);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = utility.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = utility.Id, Amount = 80m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 7, 12), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await Context.SaveChangesAsync();

        // asOfDate (7/14) is only 2 days past the 7/12 occurrence - well within the Monthly
        // match window (14 days) - too early to call this specific occurrence a miss (an
        // earlier, genuinely-unmatched occurrence from a prior month may still legitimately
        // appear as a real miss - that's correct, just not what this test is checking).
        var results = await _sut.GetRecentAccuracyAsync(Context, new DateOnly(2026, 7, 14), lookbackDays: 90);

        Assert.DoesNotContain(results, r => r.Name == "AT&T" && r.ScheduledDate == new DateOnly(2026, 7, 12));
    }
}
