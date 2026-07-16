using ClosedXML.Excel;
using Expense.Domain.Entities;
using Expense.Domain.Services.Budgets;
using Expense.Domain.Services.Export;
using Expense.Domain.Services.Forecast;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.Export;

public class ForecastExcelExporterTests : DatabaseTestBase
{
    private readonly ForecastExcelExporter _sut = new(new BudgetProrationService(), new RecurrenceExpander(), new AmexCycleCalculator());

    private async Task SeedCheckingBalanceAsync(decimal balance, DateOnly asOfDate)
    {
        Context.CheckingBalanceSnapshots.Add(new CheckingBalanceSnapshot { AsOfDate = asOfDate, Balance = balance });
        await Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Export_CreatesAnAssumptionsRowForADirectCategory_WithALiteralAmount()
    {
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 1));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var truist = new Category { Name = "Truist" };
        Context.Categories.Add(truist);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = truist.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = truist.Id, Amount = 2681.22m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 7, 4), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await Context.SaveChangesAsync();

        using var workbook = await _sut.ExportAsync(Context, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        var assumptions = workbook.Worksheet("Assumptions");
        var truistRow = FindRowByName(assumptions, "Truist");
        Assert.Equal(2681.22m, assumptions.Cell(truistRow, 2).GetValue<decimal>());
    }

    [Fact]
    public async Task Export_ForecastRowForADirectCategory_ReferencesTheAssumptionsCellNotALiteral()
    {
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 1));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var truist = new Category { Name = "Truist" };
        Context.Categories.Add(truist);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = truist.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = truist.Id, Amount = 2681.22m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 7, 4), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await Context.SaveChangesAsync();

        using var workbook = await _sut.ExportAsync(Context, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        var assumptions = workbook.Worksheet("Assumptions");
        var truistAssumptionsRow = FindRowByName(assumptions, "Truist");

        var forecast = workbook.Worksheet("Forecast");
        var forecastRow = FindRowByDescription(forecast, "Truist");
        var amountCell = forecast.Cell(forecastRow, 3);

        Assert.True(amountCell.HasFormula);
        Assert.Contains($"Assumptions!$D${truistAssumptionsRow}", amountCell.FormulaA1);
    }

    [Fact]
    public async Task Export_TwoOccurrencesOfTheSameCategory_BothReferenceTheSameAssumptionsCell()
    {
        // The whole point: editing one Assumptions cell should cascade to every occurrence.
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 1, 1));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var truist = new Category { Name = "Truist" };
        Context.Categories.Add(truist);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = truist.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = truist.Id, Amount = 2681.22m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 7, 4), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await Context.SaveChangesAsync();

        using var workbook = await _sut.ExportAsync(Context, new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30));

        var forecast = workbook.Worksheet("Forecast");
        var truistRows = FindAllRowsByDescription(forecast, "Truist");

        Assert.Equal(3, truistRows.Count); // July, August, September
        var formulas = truistRows.Select(r => forecast.Cell(r, 3).FormulaA1).Distinct().ToList();
        Assert.Single(formulas); // all three point at the exact same Assumptions cell
    }

    [Fact]
    public async Task Export_IncomeCategory_FormulaIsPositive_ExpenseCategory_FormulaIsNegative()
    {
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 1));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();

        var paycheck = new Category { Name = "Paycheck" };
        var truist = new Category { Name = "Truist" };
        Context.Categories.AddRange(paycheck, truist);
        await Context.SaveChangesAsync();
        Context.FundingRules.AddRange(
            new FundingRule { CategoryId = paycheck.Id, Strategy = FundingStrategies.Direct },
            new FundingRule { CategoryId = truist.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.AddRange(
            new BudgetPeriod { CategoryId = paycheck.Id, Amount = 2000m, Frequency = Frequency.Biweekly, Direction = Direction.Income, Anchor = new DateOnly(2026, 7, 17), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1) },
            new BudgetPeriod { CategoryId = truist.Id, Amount = 2681.22m, Frequency = Frequency.Monthly, Direction = Direction.Expense, Anchor = new DateOnly(2026, 7, 15), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1) });
        await Context.SaveChangesAsync();

        // Narrow window so biweekly Paycheck's neighboring occurrences (7/3 and 7/31) and
        // Truist's neighboring monthly occurrences fall outside it - each appears exactly once.
        using var workbook = await _sut.ExportAsync(Context, new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 20));

        var forecast = workbook.Worksheet("Forecast");
        var paycheckFormula = forecast.Cell(FindRowByDescription(forecast, "Paycheck"), 3).FormulaA1;
        var truistFormula = forecast.Cell(FindRowByDescription(forecast, "Truist"), 3).FormulaA1;

        Assert.DoesNotContain("-", paycheckFormula);
        Assert.StartsWith("-", truistFormula);
    }

    [Fact]
    public async Task Export_RunningBalance_CarriesForwardFromStartingBalanceThroughEachRow()
    {
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 1));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();
        Context.OneTimeEvents.AddRange(
            new OneTimeEvent { Name = "HVAC repair", Amount = 850m, Direction = Direction.Expense, Date = new DateOnly(2026, 7, 20), AccountId = checking.Id },
            new OneTimeEvent { Name = "Refund", Amount = 200m, Direction = Direction.Income, Date = new DateOnly(2026, 7, 25), AccountId = checking.Id });
        await Context.SaveChangesAsync();

        using var workbook = await _sut.ExportAsync(Context, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        var forecast = workbook.Worksheet("Forecast");
        var firstRow = FindRowByDescription(forecast, "HVAC repair");
        var secondRow = FindRowByDescription(forecast, "Refund");

        Assert.True(forecast.Cell(firstRow, 4).HasFormula);
        Assert.Contains($"C{firstRow}", forecast.Cell(firstRow, 4).FormulaA1);

        // The second row's balance must build on the first row's balance cell, not the starting-balance literal again.
        Assert.Contains($"D{firstRow}", forecast.Cell(secondRow, 4).FormulaA1);
        Assert.Contains($"C{secondRow}", forecast.Cell(secondRow, 4).FormulaA1);
    }

    [Fact]
    public async Task Export_DebtAccountPayment_ReferencesAnAssumptionsCellBuiltFromMinPlusExtraPayment()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        Context.Accounts.Add(new Account
        {
            Name = "Discover", Type = AccountType.Debt, MinPayment = 50m, ExtraPayment = 100m, PaymentDueDay = 20
        });
        await Context.SaveChangesAsync();

        using var workbook = await _sut.ExportAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var assumptions = workbook.Worksheet("Assumptions");
        var assumptionsRow = FindRowByName(assumptions, "Discover Payment");
        Assert.Equal(50m, assumptions.Cell(assumptionsRow, 2).GetValue<decimal>());
        Assert.Equal(100m, assumptions.Cell(assumptionsRow, 3).GetValue<decimal>());
        Assert.Equal($"B{assumptionsRow}+C{assumptionsRow}", assumptions.Cell(assumptionsRow, 4).FormulaA1);

        var forecast = workbook.Worksheet("Forecast");
        var forecastRow = FindRowByDescription(forecast, "Discover Payment");
        Assert.Equal($"-Assumptions!$D${assumptionsRow}", forecast.Cell(forecastRow, 3).FormulaA1);
    }

    [Fact]
    public async Task Export_AmexPayment_OnlyTheExtraPaymentPortionIsAMasterCell()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 3, 1));
        var amex = new Account { Name = "Amex", Type = AccountType.ActiveSpending, ExtraPayment = 0m, StatementCloseDay = 25, PaymentDueDay = 15 };
        Context.Accounts.Add(amex);
        await Context.SaveChangesAsync();

        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = groceries.Id, Strategy = FundingStrategies.PayInFullAmex });
        Context.BudgetPeriods.Add(new BudgetPeriod { CategoryId = groceries.Id, Amount = 900m, Frequency = Frequency.Monthly, EffectiveFrom = new DateOnly(2026, 1, 1) });
        Context.BankTransactions.Add(new BankTransaction
        {
            AccountId = amex.Id, TransactionDate = new DateOnly(2026, 2, 1), PostedDate = new DateOnly(2026, 2, 1),
            Description = "TRADER JOE S", Amount = -1250m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        using var workbook = await _sut.ExportAsync(Context, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        var assumptions = workbook.Worksheet("Assumptions");
        var extraRow = FindRowByName(assumptions, "Amex Extra Payment");
        var groceriesRow = FindRowByName(assumptions, "Groceries");

        var forecast = workbook.Worksheet("Forecast");
        var forecastRow = FindRowByDescription(forecast, "Amex Payment");
        var formula = forecast.Cell(forecastRow, 3).FormulaA1;

        // Actual (1250) beat budget (900), so the cycle's closed, MAX(actual, live budget
        // reference) wins; extra payment (0) is still a live reference too.
        Assert.Equal($"-(MAX(1250,Assumptions!$D${groceriesRow})+Assumptions!$D${extraRow})", formula);
    }

    [Fact]
    public async Task Export_AmexPayment_WeeklyBudgetedCategory_ProratesViaALiveFormula_NotARoundedLiteral()
    {
        // Weekly-to-monthly proration used to get pre-computed in C# and embedded as a raw
        // literal with ~25 decimal places (a real bug found via real-data verification). Now
        // the conversion itself is an Excel formula (30.4375/7), so there's no literal to round.
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 1, 1));
        var amex = new Account { Name = "Amex", Type = AccountType.ActiveSpending, ExtraPayment = 700m, StatementCloseDay = 26, PaymentDueDay = 20 };
        Context.Accounts.Add(amex);
        await Context.SaveChangesAsync();

        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = groceries.Id, Strategy = FundingStrategies.PayInFullAmex });
        Context.BudgetPeriods.Add(new BudgetPeriod { CategoryId = groceries.Id, Amount = 450m, Frequency = Frequency.Weekly, EffectiveFrom = new DateOnly(2026, 1, 1) });
        await Context.SaveChangesAsync();

        using var workbook = await _sut.ExportAsync(Context, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        var assumptions = workbook.Worksheet("Assumptions");
        var groceriesRow = FindRowByName(assumptions, "Groceries");

        var forecast = workbook.Worksheet("Forecast");
        var forecastRow = FindRowByDescription(forecast, "Amex Payment");
        var formula = forecast.Cell(forecastRow, 3).FormulaA1;

        Assert.Contains($"Assumptions!$D${groceriesRow}*30.4375/7", formula);
        Assert.DoesNotMatch(@"\d+\.\d{5,}", formula); // no long floating-point literal anywhere
    }

    [Fact]
    public async Task Export_PayInFullAmexCategories_EachGetTheirOwnAssumptionsRow()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 3, 1));
        var amex = new Account { Name = "Amex", Type = AccountType.ActiveSpending, ExtraPayment = 1100m, StatementCloseDay = 25, PaymentDueDay = 15 };
        Context.Accounts.Add(amex);
        await Context.SaveChangesAsync();

        var groceries = new Category { Name = "Groceries" };
        var gas = new Category { Name = "Gas" };
        Context.Categories.AddRange(groceries, gas);
        await Context.SaveChangesAsync();
        Context.FundingRules.AddRange(
            new FundingRule { CategoryId = groceries.Id, Strategy = FundingStrategies.PayInFullAmex },
            new FundingRule { CategoryId = gas.Id, Strategy = FundingStrategies.PayInFullAmex });
        Context.BudgetPeriods.AddRange(
            new BudgetPeriod { CategoryId = groceries.Id, Amount = 900m, Frequency = Frequency.Monthly, EffectiveFrom = new DateOnly(2026, 1, 1) },
            new BudgetPeriod { CategoryId = gas.Id, Amount = 100m, Frequency = Frequency.Weekly, EffectiveFrom = new DateOnly(2026, 1, 1) });
        await Context.SaveChangesAsync();

        using var workbook = await _sut.ExportAsync(Context, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        var assumptions = workbook.Worksheet("Assumptions");
        var groceriesRow = FindRowByName(assumptions, "Groceries");
        var gasRow = FindRowByName(assumptions, "Gas");

        Assert.Equal(900m, assumptions.Cell(groceriesRow, 2).GetValue<decimal>());
        Assert.Equal("Monthly", assumptions.Cell(groceriesRow, 5).GetString());
        Assert.Equal(100m, assumptions.Cell(gasRow, 2).GetValue<decimal>());
        Assert.Equal("Weekly", assumptions.Cell(gasRow, 5).GetString());
    }

    [Fact]
    public async Task Export_FutureAmexCycle_HasNoLiteralAtAll_JustTheBudgetFormula()
    {
        // A cycle that hasn't started yet has no actual data by definition - so its payment
        // should be pure live formula, no MAX() and no literal snapshot whatsoever.
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 1, 1));
        var amex = new Account { Name = "Amex", Type = AccountType.ActiveSpending, ExtraPayment = 1100m, StatementCloseDay = 25, PaymentDueDay = 15 };
        Context.Accounts.Add(amex);
        await Context.SaveChangesAsync();

        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = groceries.Id, Strategy = FundingStrategies.PayInFullAmex });
        Context.BudgetPeriods.Add(new BudgetPeriod { CategoryId = groceries.Id, Amount = 900m, Frequency = Frequency.Monthly, EffectiveFrom = new DateOnly(2026, 1, 1) });
        await Context.SaveChangesAsync();

        using var workbook = await _sut.ExportAsync(Context, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var assumptions = workbook.Worksheet("Assumptions");
        var groceriesRow = FindRowByName(assumptions, "Groceries");
        var extraRow = FindRowByName(assumptions, "Amex Extra Payment");

        var forecast = workbook.Worksheet("Forecast");
        var forecastRow = FindAllRowsByDescription(forecast, "Amex Payment").Max(); // a later, still-future cycle
        var formula = forecast.Cell(forecastRow, 3).FormulaA1;

        Assert.Equal($"-(Assumptions!$D${groceriesRow}+Assumptions!$D${extraRow})", formula);
        Assert.DoesNotContain("MAX", formula);
    }

    [Fact]
    public async Task Export_OneTimeEvent_IsALiteralValue_NeverAFormula()
    {
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 1));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();
        Context.OneTimeEvents.Add(new OneTimeEvent
        {
            Name = "HVAC repair", Amount = 850m, Direction = Direction.Expense, Date = new DateOnly(2026, 7, 20), AccountId = checking.Id
        });
        await Context.SaveChangesAsync();

        using var workbook = await _sut.ExportAsync(Context, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        var forecast = workbook.Worksheet("Forecast");
        var row = FindRowByDescription(forecast, "HVAC repair");
        var amountCell = forecast.Cell(row, 3);

        Assert.False(amountCell.HasFormula);
        Assert.Equal(-850m, amountCell.GetValue<decimal>());
    }

    [Fact]
    public async Task Export_HeaderRows_AreBold()
    {
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 1));

        using var workbook = await _sut.ExportAsync(Context, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        var assumptions = workbook.Worksheet("Assumptions");
        var forecast = workbook.Worksheet("Forecast");

        Assert.True(assumptions.Cell(1, 1).Style.Font.Bold);
        Assert.True(forecast.Cell(1, 1).Style.Font.Bold);
    }

    [Fact]
    public async Task Export_DateColumn_IsCenterAligned()
    {
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 1));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();
        Context.OneTimeEvents.Add(new OneTimeEvent
        {
            Name = "HVAC repair", Amount = 850m, Direction = Direction.Expense, Date = new DateOnly(2026, 7, 20), AccountId = checking.Id
        });
        await Context.SaveChangesAsync();

        using var workbook = await _sut.ExportAsync(Context, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        var forecast = workbook.Worksheet("Forecast");
        var row = FindRowByDescription(forecast, "HVAC repair");

        Assert.Equal(XLAlignmentHorizontalValues.Center, forecast.Cell(row, 1).Style.Alignment.Horizontal);
    }

    [Fact]
    public async Task Export_AmountAndBalanceColumns_AreRightAlignedAndCurrencyFormatted()
    {
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 1));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();
        Context.OneTimeEvents.Add(new OneTimeEvent
        {
            Name = "HVAC repair", Amount = 850m, Direction = Direction.Expense, Date = new DateOnly(2026, 7, 20), AccountId = checking.Id
        });
        await Context.SaveChangesAsync();

        using var workbook = await _sut.ExportAsync(Context, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        var forecast = workbook.Worksheet("Forecast");
        var row = FindRowByDescription(forecast, "HVAC repair");

        var amountCell = forecast.Cell(row, 3);
        var balanceCell = forecast.Cell(row, 4);

        Assert.Equal(XLAlignmentHorizontalValues.Right, amountCell.Style.Alignment.Horizontal);
        Assert.Equal(XLAlignmentHorizontalValues.Right, balanceCell.Style.Alignment.Horizontal);
        Assert.Equal("$#,##0.00", amountCell.Style.NumberFormat.Format);
        Assert.Equal("$#,##0.00", balanceCell.Style.NumberFormat.Format);
    }

    [Fact]
    public async Task Export_AssumptionsAmountColumns_AreCurrencyFormatted()
    {
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 1));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();
        var truist = new Category { Name = "Truist" };
        Context.Categories.Add(truist);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = truist.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = truist.Id, Amount = 2681.22m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 7, 4), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await Context.SaveChangesAsync();

        using var workbook = await _sut.ExportAsync(Context, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        var assumptions = workbook.Worksheet("Assumptions");
        var row = FindRowByName(assumptions, "Truist");

        Assert.Equal("$#,##0.00", assumptions.Cell(row, 2).Style.NumberFormat.Format); // Amount
        Assert.Equal("$#,##0.00", assumptions.Cell(row, 3).Style.NumberFormat.Format); // Extra
        Assert.Equal("$#,##0.00", assumptions.Cell(row, 4).Style.NumberFormat.Format); // Total
    }

    [Fact]
    public async Task Export_ColumnsAreWideEnoughForTheirContent()
    {
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 1));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();
        var category = new Category { Name = "Chase Sapphire Reserve Payment" };
        Context.Categories.Add(category);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = category.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = category.Id, Amount = 459m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 7, 4), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await Context.SaveChangesAsync();

        using var workbook = await _sut.ExportAsync(Context, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        var forecast = workbook.Worksheet("Forecast");
        // ClosedXML's default column width (8.43) is far too narrow for a description
        // this long - AdjustToContents must have actually run, not just be left at default.
        Assert.True(forecast.Column(2).Width > 20);
    }

    [Fact]
    public async Task Export_AssumptionsDateColumn_ShowsWhenADirectCategoryOccurs()
    {
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 1));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();
        var truist = new Category { Name = "Truist" };
        Context.Categories.Add(truist);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = truist.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = truist.Id, Amount = 2681.22m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 7, 4), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await Context.SaveChangesAsync();

        using var workbook = await _sut.ExportAsync(Context, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        var assumptions = workbook.Worksheet("Assumptions");
        Assert.Equal("Date", assumptions.Cell(1, 7).GetString());
        var row = FindRowByName(assumptions, "Truist");
        Assert.Equal(new DateTime(2026, 7, 4), assumptions.Cell(row, 7).GetValue<DateTime>());
    }

    [Fact]
    public async Task Export_AssumptionsDateColumn_ShowsADebtAccountsPaymentDueDate()
    {
        await SeedCheckingBalanceAsync(3000m, new DateOnly(2026, 7, 13));
        Context.Accounts.Add(new Account
        {
            Name = "Discover", Type = AccountType.Debt, MinPayment = 50m, ExtraPayment = 100m, PaymentDueDay = 20
        });
        await Context.SaveChangesAsync();

        using var workbook = await _sut.ExportAsync(Context, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 31));

        var assumptions = workbook.Worksheet("Assumptions");
        var row = FindRowByName(assumptions, "Discover Payment");
        Assert.Equal(new DateTime(2026, 7, 20), assumptions.Cell(row, 7).GetValue<DateTime>());
    }

    [Fact]
    public async Task Export_FrequencyAndDirectionColumns_AreCenterAligned()
    {
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 1));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();
        var truist = new Category { Name = "Truist" };
        Context.Categories.Add(truist);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = truist.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = truist.Id, Amount = 2681.22m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 7, 4), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await Context.SaveChangesAsync();

        using var workbook = await _sut.ExportAsync(Context, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        var assumptions = workbook.Worksheet("Assumptions");
        var row = FindRowByName(assumptions, "Truist");

        Assert.Equal(XLAlignmentHorizontalValues.Center, assumptions.Cell(row, 5).Style.Alignment.Horizontal);
        Assert.Equal(XLAlignmentHorizontalValues.Center, assumptions.Cell(row, 6).Style.Alignment.Horizontal);
    }

    [Fact]
    public async Task Export_MoneyColumns_AreWidenedBeyondATightAutofit()
    {
        await SeedCheckingBalanceAsync(1000m, new DateOnly(2026, 7, 1));
        var checking = new Account { Name = "Checking", Type = AccountType.Checking };
        Context.Accounts.Add(checking);
        await Context.SaveChangesAsync();
        var truist = new Category { Name = "Truist" };
        Context.Categories.Add(truist);
        await Context.SaveChangesAsync();
        Context.FundingRules.Add(new FundingRule { CategoryId = truist.Id, Strategy = FundingStrategies.Direct });
        Context.BudgetPeriods.Add(new BudgetPeriod
        {
            CategoryId = truist.Id, Amount = 2681.22m, Frequency = Frequency.Monthly, Direction = Direction.Expense,
            Anchor = new DateOnly(2026, 7, 4), AccountId = checking.Id, EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await Context.SaveChangesAsync();

        using var workbook = await _sut.ExportAsync(Context, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        var assumptions = workbook.Worksheet("Assumptions");
        var forecast = workbook.Worksheet("Forecast");

        // Build same-content baseline sheets with a plain autofit (no extra widening) to
        // prove the money columns really got wider than a tight fit would produce.
        using var baselineBook = new XLWorkbook();
        var baselineAssumptions = baselineBook.Worksheets.Add("Assumptions");
        foreach (var row in assumptions.RowsUsed())
        {
            for (var c = 1; c <= 4; c++) baselineAssumptions.Cell(row.RowNumber(), c).Value = row.Cell(c).GetFormattedString();
        }
        baselineAssumptions.Columns().AdjustToContents();

        var baselineForecast = baselineBook.Worksheets.Add("Forecast");
        foreach (var row in forecast.RowsUsed())
        {
            for (var c = 1; c <= 4; c++) baselineForecast.Cell(row.RowNumber(), c).Value = row.Cell(c).GetFormattedString();
        }
        baselineForecast.Columns().AdjustToContents();

        Assert.True(assumptions.Column(2).Width > baselineAssumptions.Column(2).Width);
        Assert.True(assumptions.Column(3).Width > baselineAssumptions.Column(3).Width);
        Assert.True(assumptions.Column(4).Width > baselineAssumptions.Column(4).Width);
        Assert.True(forecast.Column(3).Width > baselineForecast.Column(3).Width);
        Assert.True(forecast.Column(4).Width > baselineForecast.Column(4).Width);
    }

    private static int FindRowByName(IXLWorksheet sheet, string name) =>
        sheet.RowsUsed().Single(r => r.Cell(1).GetString() == name).RowNumber();

    private static int FindRowByDescription(IXLWorksheet sheet, string description) =>
        sheet.RowsUsed().Single(r => r.Cell(2).GetString() == description).RowNumber();

    private static List<int> FindAllRowsByDescription(IXLWorksheet sheet, string description) =>
        sheet.RowsUsed().Where(r => r.Cell(2).GetString() == description).Select(r => r.RowNumber()).ToList();
}
