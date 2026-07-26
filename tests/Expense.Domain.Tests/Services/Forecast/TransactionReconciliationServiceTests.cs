using Expense.Domain.Entities;
using Expense.Domain.Services.Forecast;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.Forecast;

public class TransactionReconciliationServiceTests : DatabaseTestBase
{
    private readonly TransactionReconciliationService _sut = new(new RecurrenceExpander(), new AmexCycleCalculator());

    [Fact]
    public async Task TransactionPostedWellBeforeAsOfDate_StillClassifiesToTheCorrectOccurrence()
    {
        // Real bug this guards: a date-window search relative to "today" silently excluded
        // a transaction that posted more than ~28 days before asOfDate. Classification here
        // depends only on the transaction's own PostedDate against the category's real
        // schedule - not on how long ago "today" makes that look, so there's no distance
        // from asOfDate that can ever break this.
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
        var txn = new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 8), PostedDate = new DateOnly(2026, 7, 8),
            Description = "SSA TREAS 310", Amount = 652m, ImportSource = "Test", CategoryId = ssa.Id, CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(txn);
        await Context.SaveChangesAsync();

        // asOfDate is 90+ days after the transaction posted - far beyond the old 28-day bound.
        await _sut.ReconcileAsync(Context, new DateOnly(2026, 10, 20));

        var reloaded = await Context.BankTransactions.SingleAsync(t => t.Id == txn.Id);
        Assert.Equal(new DateOnly(2026, 7, 10), reloaded.ReconciledOccurrenceDate);
    }

    [Fact]
    public async Task TransactionIsClassifiedAgainstTheBudgetVersionInEffectWhenItPosted_NotTheCurrentOne()
    {
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var perimeter = new Category { Name = "Perimeter" };
        Context.Categories.Add(perimeter);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = perimeter.Id, Strategy = FundingStrategies.Direct });
        // In effect when the 7/15 occurrence actually happened.
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = perimeter.Id, Amount = 1000m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 7, 15), AccountId = checking.Id,
            EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveThrough = new DateOnly(2026, 7, 21)
        });
        // Edited down to 900 on 7/22 - after the 7/15 payment already posted.
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = perimeter.Id, Amount = 900m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 7, 15), AccountId = checking.Id,
            EffectiveFrom = new DateOnly(2026, 7, 22), EffectiveThrough = null
        });
        var txn = new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 21), PostedDate = new DateOnly(2026, 7, 21),
            Description = "PERIMETER", Amount = -1000m, ImportSource = "Test", CategoryId = perimeter.Id, CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(txn);
        await Context.SaveChangesAsync();

        await _sut.ReconcileAsync(Context, new DateOnly(2026, 7, 24));

        var reloaded = await Context.BankTransactions.SingleAsync(t => t.Id == txn.Id);
        Assert.Equal(new DateOnly(2026, 7, 15), reloaded.ReconciledOccurrenceDate);
    }

    [Fact]
    public async Task RecategorizedTransaction_GetsReclassifiedOnTheNextRun()
    {
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var ssa = new Category { Name = "SSA" };
        var groceries = new Category { Name = "Groceries" };
        Context.Categories.AddRange(ssa, groceries);
        await Context.SaveChangesAsync();

        Context.FundingRules.Add(new FundingRule { CategoryId = ssa.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = ssa.Id, Amount = 652m, Frequency = Frequency.Monthly, Direction = Direction.Income,
            Anchor = new DateOnly(2026, 7, 10), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        var txn = new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 8), PostedDate = new DateOnly(2026, 7, 8),
            Description = "SSA TREAS 310", Amount = 652m, ImportSource = "Test", CategoryId = ssa.Id, CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(txn);
        await Context.SaveChangesAsync();

        await _sut.ReconcileAsync(Context, new DateOnly(2026, 7, 24));
        Assert.Equal(new DateOnly(2026, 7, 10), (await Context.BankTransactions.SingleAsync(t => t.Id == txn.Id)).ReconciledOccurrenceDate);

        // Miscategorized - a human moves it to Groceries (no determinable schedule) later.
        txn.CategoryId = groceries.Id;
        await Context.SaveChangesAsync();

        await _sut.ReconcileAsync(Context, new DateOnly(2026, 7, 24));

        var reloaded = await Context.BankTransactions.SingleAsync(t => t.Id == txn.Id);
        Assert.Null(reloaded.ReconciledOccurrenceDate);
    }

    [Fact]
    public async Task TransactionInACategoryWithNoDeterminableSchedule_IsLeftUnclassified()
    {
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();

        var txn = new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 8), PostedDate = new DateOnly(2026, 7, 8),
            Description = "KROGER", Amount = -50m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(txn);
        await Context.SaveChangesAsync();

        await _sut.ReconcileAsync(Context, new DateOnly(2026, 7, 24));

        var reloaded = await Context.BankTransactions.SingleAsync(t => t.Id == txn.Id);
        Assert.Null(reloaded.ReconciledOccurrenceDate);
    }

    [Fact]
    public async Task DebtAccountPayment_ClassifiesCorrectly()
    {
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        var discover = new Account { Name = "Discover", Type = AccountType.Debt, MinPayment = 173m, ExtraPayment = 0m, PaymentDueDay = 3 };
        Context.Accounts.AddRange(checking, discover);
        await Context.SaveChangesAsync();

        var discoverPayment = new Category { Name = "Discover Payment" };
        Context.Categories.Add(discoverPayment);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = discoverPayment.Id, Strategy = FundingStrategies.AccountPayment, AccountId = discover.Id });
        var txn = new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 3), PostedDate = new DateOnly(2026, 7, 3),
            Description = "DISCOVER PAYMENT", Amount = -173m, ImportSource = "Test", CategoryId = discoverPayment.Id, CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(txn);
        await Context.SaveChangesAsync();

        await _sut.ReconcileAsync(Context, new DateOnly(2026, 7, 24));

        var reloaded = await Context.BankTransactions.SingleAsync(t => t.Id == txn.Id);
        Assert.Equal(new DateOnly(2026, 7, 3), reloaded.ReconciledOccurrenceDate);
    }

    [Fact]
    public async Task DryRun_ReportsChanges_ButDoesNotPersistThem()
    {
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
        var txn = new BankTransaction
        {
            AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 8), PostedDate = new DateOnly(2026, 7, 8),
            Description = "SSA TREAS 310", Amount = 652m, ImportSource = "Test", CategoryId = ssa.Id, CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(txn);
        await Context.SaveChangesAsync();

        var changes = await _sut.ReconcileAsync(Context, new DateOnly(2026, 7, 24), dryRun: true);

        Assert.Contains(changes, c => c.TransactionId == txn.Id && c.NewValue == new DateOnly(2026, 7, 10));
        var reloaded = await Context.BankTransactions.SingleAsync(t => t.Id == txn.Id);
        Assert.Null(reloaded.ReconciledOccurrenceDate);
    }
}
