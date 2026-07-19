using Expense.Domain.Entities;
using Expense.Domain.Services.Budgets;
using Expense.Domain.Services.HistoricalAnalysis;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.HistoricalAnalysis;

public class HistoricalAnalysisServiceTests : DatabaseTestBase
{
    private readonly HistoricalAnalysisService _sut = new(new BudgetProrationService());

    private async Task<Account> CreateAccountAsync(string name = "Amex", AccountType type = AccountType.ActiveSpending)
    {
        var account = new Account { Name = name, Type = type };
        Context.Accounts.Add(account);
        await Context.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task WeeklyReport_UsesTheBudgetInEffectDuringThatWeek_NotTodays()
    {
        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = groceries.Id, Strategy = FundingStrategies.PayInFullAmex });
        Context.BudgetPeriods.AddRange(
            new BudgetPeriod { CategoryId = groceries.Id, Amount = 400m, Frequency = Frequency.Weekly, EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveThrough = new DateOnly(2026, 6, 30) },
            new BudgetPeriod { CategoryId = groceries.Id, Amount = 450m, Frequency = Frequency.Weekly, EffectiveFrom = new DateOnly(2026, 7, 1) });
        await Context.SaveChangesAsync();

        // A week entirely within the old ($400) period - Sunday 2026-06-07 to Saturday 2026-06-13
        var report = await _sut.GetWeeklyReportAsync(Context, new DateOnly(2026, 6, 7));

        var summary = Assert.Single(report, s => s.CategoryId == groceries.Id);
        Assert.Equal(400m, summary.Budget);
    }

    [Fact]
    public async Task WeeklyReport_ComputesActualFromCategorizedTransactionsInThatWeek()
    {
        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = groceries.Id, Strategy = FundingStrategies.PayInFullAmex });
        Context.BudgetPeriods.Add(new BudgetPeriod { CategoryId = groceries.Id, Amount = 450m, Frequency = Frequency.Weekly, EffectiveFrom = new DateOnly(2026, 1, 1) });
        var amex = await CreateAccountAsync();
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 6, 10), PostedDate = new DateOnly(2026, 6, 10),
            Description = "INGLES", Amount = -88m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var report = await _sut.GetWeeklyReportAsync(Context, new DateOnly(2026, 6, 7)); // Sunday 6/7 - Saturday 6/13

        var summary = Assert.Single(report, s => s.CategoryId == groceries.Id);
        Assert.Equal(88m, summary.Actual);
    }

    [Fact]
    public async Task MonthlyReport_ProratesTheBudgetToTheMonth()
    {
        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = groceries.Id, Strategy = FundingStrategies.PayInFullAmex });
        Context.BudgetPeriods.Add(new BudgetPeriod { CategoryId = groceries.Id, Amount = 450m, Frequency = Frequency.Weekly, EffectiveFrom = new DateOnly(2026, 1, 1) });
        await Context.SaveChangesAsync();

        var report = await _sut.GetMonthlyReportAsync(Context, new DateOnly(2026, 6, 1));

        var summary = Assert.Single(report, s => s.CategoryId == groceries.Id);
        var expected = new BudgetProrationService().Convert(450m, Frequency.Weekly, Frequency.Monthly);
        Assert.Equal(expected, summary.Budget);
        Assert.Equal(new DateOnly(2026, 6, 1), summary.PeriodStart);
        Assert.Equal(new DateOnly(2026, 6, 30), summary.PeriodEnd);
    }

    [Fact]
    public async Task WeeklyReport_IncludesDirectCategories_NotJustPayInFullAmex()
    {
        var truist = new Category { Name = "Truist" };
        Context.Categories.Add(truist);
        await Context.SaveChangesAsync();
        var checking = await CreateAccountAsync("Wells Fargo Checking", AccountType.Checking);
        Context.FundingRules.Add(new FundingRule { CategoryId = truist.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = truist.Id, Amount = 2681.22m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 1, 4), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await Context.SaveChangesAsync();

        var report = await _sut.GetWeeklyReportAsync(Context, new DateOnly(2026, 6, 7));

        Assert.Contains(report, s => s.CategoryId == truist.Id);
    }

    [Fact]
    public async Task GetYearToDateAsync_SumsActualSpendFromJanuaryFirstThroughAsOfDate()
    {
        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = groceries.Id, Strategy = FundingStrategies.PayInFullAmex });
        var amex = await CreateAccountAsync();
        Context.BankTransactions.AddRange(
            new BankTransaction { AccountId = amex.Id, TransactionDate = new DateOnly(2026, 2, 1), PostedDate = new DateOnly(2026, 2, 1), Description = "INGLES", Amount = -100m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow },
            new BankTransaction { AccountId = amex.Id, TransactionDate = new DateOnly(2026, 6, 1), PostedDate = new DateOnly(2026, 6, 1), Description = "INGLES", Amount = -50m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow },
            new BankTransaction { AccountId = amex.Id, TransactionDate = new DateOnly(2026, 8, 1), PostedDate = new DateOnly(2026, 8, 1), Description = "INGLES", Amount = -999m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var report = await _sut.GetYearToDateAsync(Context, new DateOnly(2026, 7, 1));

        var summary = Assert.Single(report, s => s.CategoryId == groceries.Id);
        Assert.Equal(150m, summary.Actual); // Feb + Jun, not the August one (after asOfDate)
        Assert.Equal(new DateOnly(2026, 1, 1), summary.PeriodStart);
        Assert.Equal(new DateOnly(2026, 7, 1), summary.PeriodEnd);
    }

    [Fact]
    public async Task GetCategoryTrendAsync_ReturnsWeeklyActualsInChronologicalOrder()
    {
        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = groceries.Id, Strategy = FundingStrategies.PayInFullAmex });
        Context.BudgetPeriods.Add(new BudgetPeriod { CategoryId = groceries.Id, Amount = 450m, Frequency = Frequency.Weekly, EffectiveFrom = new DateOnly(2026, 1, 1) });
        var amex = await CreateAccountAsync();
        Context.BankTransactions.AddRange(
            new BankTransaction { AccountId = amex.Id, TransactionDate = new DateOnly(2026, 6, 8), PostedDate = new DateOnly(2026, 6, 8), Description = "W1", Amount = -100m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow }, // week of 6/7
            new BankTransaction { AccountId = amex.Id, TransactionDate = new DateOnly(2026, 6, 15), PostedDate = new DateOnly(2026, 6, 15), Description = "W2", Amount = -200m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow }, // week of 6/14
            new BankTransaction { AccountId = amex.Id, TransactionDate = new DateOnly(2026, 6, 22), PostedDate = new DateOnly(2026, 6, 22), Description = "W3", Amount = -300m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow }); // week of 6/21
        await Context.SaveChangesAsync();

        // asOfDate is a Wednesday in the week of 6/21 (the 3rd week) - trend covers the 3 weeks ending there
        var trend = await _sut.GetCategoryTrendAsync(Context, groceries.Id, Frequency.Weekly, periodCount: 3, new DateOnly(2026, 6, 24));

        Assert.Equal(3, trend.Count);
        Assert.Equal(100m, trend[0].Actual);
        Assert.Equal(new DateOnly(2026, 6, 7), trend[0].PeriodStart);
        Assert.Equal(200m, trend[1].Actual);
        Assert.Equal(300m, trend[2].Actual);
        Assert.Equal(new DateOnly(2026, 6, 21), trend[2].PeriodStart);
    }

    [Fact]
    public async Task Get4WeekAverageAsync_AveragesActualSpendAcrossTheLastFourWeeks_AgainstTheCurrentBudget()
    {
        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = groceries.Id, Strategy = FundingStrategies.PayInFullAmex });
        // Budget changed recently - the rolling average should compare against the CURRENT (500) budget, not the old one
        Context.BudgetPeriods.AddRange(
            new BudgetPeriod { CategoryId = groceries.Id, Amount = 400m, Frequency = Frequency.Weekly, EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveThrough = new DateOnly(2026, 6, 20) },
            new BudgetPeriod { CategoryId = groceries.Id, Amount = 500m, Frequency = Frequency.Weekly, EffectiveFrom = new DateOnly(2026, 6, 21) });
        var amex = await CreateAccountAsync();
        Context.BankTransactions.AddRange(
            new BankTransaction { AccountId = amex.Id, TransactionDate = new DateOnly(2026, 6, 8), PostedDate = new DateOnly(2026, 6, 8), Description = "W1", Amount = -100m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow },
            new BankTransaction { AccountId = amex.Id, TransactionDate = new DateOnly(2026, 6, 15), PostedDate = new DateOnly(2026, 6, 15), Description = "W2", Amount = -200m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow },
            new BankTransaction { AccountId = amex.Id, TransactionDate = new DateOnly(2026, 6, 22), PostedDate = new DateOnly(2026, 6, 22), Description = "W3", Amount = -300m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow },
            new BankTransaction { AccountId = amex.Id, TransactionDate = new DateOnly(2026, 6, 29), PostedDate = new DateOnly(2026, 6, 29), Description = "W4", Amount = -400m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var averages = await _sut.Get4WeekAverageAsync(Context, new DateOnly(2026, 7, 1));

        var summary = Assert.Single(averages, s => s.CategoryId == groceries.Id);
        Assert.Equal(250m, summary.AverageActual); // (100+200+300+400)/4
        Assert.Equal(500m, summary.CurrentBudget); // current, not the historical 400
    }

    [Fact]
    public async Task WeeklyReport_ExcludesAccountPaymentAndNoneCategories()
    {
        var discoverPayment = new Category { Name = "Discover Payment" };
        var offBudget = new Category { Name = "Off-Budget/Misc" };
        Context.Categories.AddRange(discoverPayment, offBudget);
        await Context.SaveChangesAsync();
        Context.FundingRules.AddRange(
            new FundingRule { CategoryId = discoverPayment.Id, Strategy = FundingStrategies.AccountPayment },
            new FundingRule { CategoryId = offBudget.Id, Strategy = FundingStrategies.None });
        await Context.SaveChangesAsync();

        var report = await _sut.GetWeeklyReportAsync(Context, new DateOnly(2026, 6, 7));

        Assert.Empty(report);
    }

    [Fact]
    public async Task GetRecurringProductReportAsync_GroupsByProductWithCountAveragePriceTotalAndLastPurchased()
    {
        var supplements = new Category { Name = "Supplements" };
        Context.Categories.Add(supplements);
        await Context.SaveChangesAsync();
        var fishOil = new Product { ProductPattern = "%FISH OIL%", CategoryId = supplements.Id };
        Context.Products.Add(fishOil);
        await Context.SaveChangesAsync();

        Context.AmazonOrderItems.AddRange(
            new AmazonOrderItem { OrderId = "O1", OrderDate = new DateOnly(2026, 3, 1), ItemTitle = "Nordic Fish Oil", Price = 20m, Quantity = 1, TaxAllocated = 1m, ProductId = fishOil.Id, CategoryId = supplements.Id, CreatedAt = DateTimeOffset.UtcNow },
            new AmazonOrderItem { OrderId = "O2", OrderDate = new DateOnly(2026, 5, 1), ItemTitle = "Nordic Fish Oil", Price = 24m, Quantity = 1, TaxAllocated = 1m, ProductId = fishOil.Id, CategoryId = supplements.Id, CreatedAt = DateTimeOffset.UtcNow },
            // Unrelated, unmatched item - should never show up in this report
            new AmazonOrderItem { OrderId = "O3", OrderDate = new DateOnly(2026, 4, 1), ItemTitle = "Random gadget", Price = 40m, Quantity = 1, TaxAllocated = 0m, CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var report = await _sut.GetRecurringProductReportAsync(Context);

        var summary = Assert.Single(report);
        Assert.Equal("%FISH OIL%", summary.ProductPattern);
        Assert.Equal("Supplements", summary.CategoryName);
        Assert.Equal(2, summary.Purchases);
        Assert.Equal(22m, summary.AveragePrice); // (20+24)/2
        Assert.Equal(46m, summary.TotalSpent); // 20+1 + 24+1
        Assert.Equal(new DateOnly(2026, 5, 1), summary.LastPurchased);
    }

    [Fact]
    public async Task GetRecurringProductReportAsync_ExcludesRefundRowsFromPurchasesAndAverage_ButStillNetsThemIntoTotalSpent()
    {
        var supplements = new Category { Name = "Supplements" };
        Context.Categories.Add(supplements);
        await Context.SaveChangesAsync();
        var fishOil = new Product { ProductPattern = "%FISH OIL%", CategoryId = supplements.Id };
        Context.Products.Add(fishOil);
        await Context.SaveChangesAsync();

        Context.AmazonOrderItems.AddRange(
            new AmazonOrderItem { OrderId = "O1", OrderDate = new DateOnly(2026, 3, 1), ItemTitle = "Nordic Fish Oil", Price = 20m, Quantity = 1, TaxAllocated = 1m, ProductId = fishOil.Id, CategoryId = supplements.Id, CreatedAt = DateTimeOffset.UtcNow },
            // A refund - its own independent negative-price row (see AmazonImportService), matched to the same product.
            new AmazonOrderItem { OrderId = "O1", OrderDate = new DateOnly(2026, 3, 5), ItemTitle = "Nordic Fish Oil", Price = -21m, Quantity = 1, TaxAllocated = 0m, ProductId = fishOil.Id, CategoryId = supplements.Id, CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var report = await _sut.GetRecurringProductReportAsync(Context);

        var summary = Assert.Single(report);
        Assert.Equal(1, summary.Purchases); // the refund row doesn't count as a purchase
        Assert.Equal(20m, summary.AveragePrice); // averaged over the real purchase only
        Assert.Equal(0m, summary.TotalSpent); // 20+1 purchase, netted against the -21 refund
    }
}
