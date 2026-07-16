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

        var forecast = workbook.Worksheet("Forecast");
        var forecastRow = FindRowByDescription(forecast, "Amex Payment");
        var formula = forecast.Cell(forecastRow, 3).FormulaA1;

        // Actual (1250) beat budget (900), so the base snapshot is 1250; extra payment (0) is still a live reference.
        Assert.Equal($"-(1250+Assumptions!$D${extraRow})", formula);
    }

    [Fact]
    public async Task Export_AmexPayment_ProratedBudgetLiteral_IsRoundedToCents()
    {
        // Weekly-to-monthly proration involves dividing by 365.25/12/7, which produces a
        // long repeating decimal - the embedded literal must be rounded, not dumped raw.
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

        var forecast = workbook.Worksheet("Forecast");
        var forecastRow = FindRowByDescription(forecast, "Amex Payment");
        var formula = forecast.Cell(forecastRow, 3).FormulaA1;

        Assert.Matches(@"-\(\d+\.\d{2}\+Assumptions", formula);
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

    private static int FindRowByName(IXLWorksheet sheet, string name) =>
        sheet.RowsUsed().Single(r => r.Cell(1).GetString() == name).RowNumber();

    private static int FindRowByDescription(IXLWorksheet sheet, string description) =>
        sheet.RowsUsed().Single(r => r.Cell(2).GetString() == description).RowNumber();

    private static List<int> FindAllRowsByDescription(IXLWorksheet sheet, string description) =>
        sheet.RowsUsed().Where(r => r.Cell(2).GetString() == description).Select(r => r.RowNumber()).ToList();
}
