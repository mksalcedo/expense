using ClosedXML.Excel;
using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Budgets;
using Expense.Domain.Services.Forecast;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Export;

/// <summary>
/// Formula-driven Excel export, mirroring (and completing) the "Budget New" tab's own
/// master-cell pattern - every recurring item (income, fixed bills, debt payments) gets
/// one row on the Assumptions sheet, and every occurrence of it on the Forecast sheet is
/// a formula referencing that same cell, so changing one assumption updates every
/// downstream occurrence instead of requiring a separate edit per row. The old spreadsheet
/// only did this for income; expenses were always literal, even though a master table sat
/// right next to them unused - this export applies the pattern consistently to everything
/// that actually repeats.
///
/// Amex is the one exception: its payment is a genuinely different amount each cycle (the
/// MAX(actual, budget) rule). Its qualifying spending categories (Groceries, Gas, etc.) and
/// its Extra Payment each still get their own master cell, and a future cycle's payment is a
/// pure formula summing them - only an already-closed/in-progress cycle's real, posted-charges
/// total is a literal (wrapped in an Excel MAX() against the live budget formula), since
/// Excel can't query Postgres for that live.
///
/// One-time events are never formula-driven either - by definition they occur exactly once.
/// </summary>
public class ForecastExcelExporter(BudgetProrationService proration, RecurrenceExpander recurrenceExpander, AmexCycleCalculator amexCycleCalculator)
{
    private const int NameCol = 1, AmountCol = 2, ExtraCol = 3, TotalCol = 4, FrequencyCol = 5, DirectionCol = 6, DateAssumptionsCol = 7, MonthlyAmountCol = 8;
    private const int DateCol = 1, DescriptionCol = 2, AmountColF = 3, BalanceCol = 4;

    public async Task<XLWorkbook> ExportAsync(
        ExpenseDbContext context, DateOnly asOfDate, DateOnly windowEnd, CancellationToken cancellationToken = default)
    {
        var workbook = new XLWorkbook();
        var assumptions = workbook.Worksheets.Add("Assumptions");
        var forecast = workbook.Worksheets.Add("Forecast");

        assumptions.Cell(1, NameCol).Value = "Name";
        assumptions.Cell(1, AmountCol).Value = "Amount";
        assumptions.Cell(1, ExtraCol).Value = "Extra";
        assumptions.Cell(1, TotalCol).Value = "Total";
        assumptions.Cell(1, FrequencyCol).Value = "Frequency";
        assumptions.Cell(1, DirectionCol).Value = "Direction";
        assumptions.Cell(1, DateAssumptionsCol).Value = "Date";
        assumptions.Cell(1, MonthlyAmountCol).Value = "Monthly Amount";
        var nextAssumptionsRow = 2;

        var startingBalance = await context.CheckingBalanceSnapshots
            .OrderByDescending(s => s.AsOfDate)
            .Select(s => s.Balance)
            .FirstOrDefaultAsync(cancellationToken);

        var recurringRules = new List<RecurringRule>();
        var directionByName = new Dictionary<string, Direction>();

        // Direct categories (income, fixed bills) - one Assumptions row each, Amount is the master cell.
        var directPeriods = await context.BudgetPeriods
            .Where(p => p.EffectiveThrough == null && p.Anchor != null && p.AccountId != null)
            .Join(context.FundingRules.Where(f => f.Strategy == FundingStrategies.Direct),
                p => p.CategoryId, f => f.CategoryId, (p, _) => p)
            .Include(p => p.Category)
            .Where(p => p.Category.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var period in directPeriods)
        {
            var row = nextAssumptionsRow++;
            WriteAssumptionsRow(assumptions, row, period.Category.Name, period.Amount, 0m, period.Frequency, period.Direction, period.Anchor!.Value);
            directionByName[period.Category.Name] = period.Direction;

            recurringRules.Add(new RecurringRule
            {
                Name = period.Category.Name, Direction = period.Direction, Amount = period.Amount, Frequency = period.Frequency,
                Anchor = period.Anchor!.Value, AccountId = period.AccountId!.Value, Active = true, StartDate = DateOnly.MinValue
            });
        }

        // Debt accounts - one Assumptions row each, Min/Extra Payment both master cells.
        var debtAccounts = await context.Accounts
            .Where(a => a.Type == AccountType.Debt && a.IsActive && a.PaymentDueDay != null)
            .ToListAsync(cancellationToken);

        foreach (var account in debtAccounts)
        {
            var minPayment = account.MinPayment ?? 0m;
            var extraPayment = account.ExtraPayment ?? 0m;
            if (minPayment + extraPayment == 0m) continue;

            var name = $"{account.Name} Payment";
            var anchor = ClampedDate(asOfDate.Year, asOfDate.Month, account.PaymentDueDay!.Value);
            var row = nextAssumptionsRow++;
            WriteAssumptionsRow(assumptions, row, name, minPayment, extraPayment, Frequency.Monthly, Direction.Expense, anchor);
            directionByName[name] = Direction.Expense;

            recurringRules.Add(new RecurringRule
            {
                Name = name, Direction = Direction.Expense, Amount = minPayment + extraPayment, Frequency = Frequency.Monthly,
                Anchor = anchor, AccountId = account.Id,
                Active = true, StartDate = DateOnly.MinValue
            });
        }

        var oneTimeEvents = await context.OneTimeEvents.ToListAsync(cancellationToken);
        var lines = recurrenceExpander.Expand(recurringRules, oneTimeEvents, asOfDate, windowEnd);

        var assumptionsRowByName = new Dictionary<string, int>();
        for (var r = 2; r < nextAssumptionsRow; r++)
        {
            assumptionsRowByName[assumptions.Cell(r, NameCol).GetString()] = r;
        }

        // Qualifying spending categories (Groceries, Gas, etc.) - each gets its own Assumptions
        // row (in whatever frequency it was actually budgeted in) so the budget behind the Amex
        // payment is visible and editable, not just baked into the payment amount.
        var qualifyingCategoryIds = await context.FundingRules
            .Where(f => f.Strategy == FundingStrategies.PayInFullAmex)
            .Select(f => f.CategoryId)
            .ToListAsync(cancellationToken);

        decimal monthlyBudgetTotal = 0m;
        var categoryAssumptionsRows = new List<(int Row, Frequency Frequency)>();
        foreach (var categoryId in qualifyingCategoryIds)
        {
            var currentPeriod = await context.BudgetPeriods
                .Where(p => p.CategoryId == categoryId && p.EffectiveFrom <= asOfDate && (p.EffectiveThrough == null || p.EffectiveThrough >= asOfDate))
                .Include(p => p.Category)
                .FirstOrDefaultAsync(cancellationToken);
            if (currentPeriod is not null)
            {
                monthlyBudgetTotal += proration.Convert(currentPeriod.Amount, currentPeriod.Frequency, Frequency.Monthly);

                var categoryRow = nextAssumptionsRow++;
                WriteAssumptionsRow(assumptions, categoryRow, currentPeriod.Category.Name, currentPeriod.Amount, 0m, currentPeriod.Frequency, Direction.Expense, null);
                categoryAssumptionsRows.Add((categoryRow, currentPeriod.Frequency));
            }
        }

        var budgetSumFormula = categoryAssumptionsRows.Count == 0
            ? "0"
            : string.Join("+", categoryAssumptionsRows.Select(r => MonthlyEquivalentFormula(r.Row, r.Frequency)));

        // Amex/ActiveSpending accounts - the Extra Payment portion is a master cell, and now so
        // is the budget portion (it references the category rows above); only the real,
        // already-posted actual-charges figure for a closed/in-progress cycle stays a literal,
        // since Excel can't query Postgres for that live.
        var amexFormulaOverrides = new Dictionary<(DateOnly Date, string Description), string>();
        var activeSpendingAccounts = await context.Accounts
            .Where(a => a.Type == AccountType.ActiveSpending && a.IsActive && a.StatementCloseDay != null && a.PaymentDueDay != null)
            .ToListAsync(cancellationToken);

        foreach (var account in activeSpendingAccounts)
        {
            var qualifyingTransactions = await context.BankTransactions
                .Where(t => t.AccountId == account.Id && t.PostedDate != null && t.CategoryId != null && qualifyingCategoryIds.Contains(t.CategoryId.Value))
                .ToListAsync(cancellationToken);

            var extraPrincipal = account.ExtraPayment ?? 0m;
            var extraName = $"{account.Name} Extra Payment";
            var extraAnchor = ClampedDate(asOfDate.Year, asOfDate.Month, account.PaymentDueDay!.Value);
            var extraRow = nextAssumptionsRow++;
            WriteAssumptionsRow(assumptions, extraRow, extraName, 0m, extraPrincipal, Frequency.Monthly, Direction.Expense, extraAnchor);

            var cycles = amexCycleCalculator.CalculateDuePayments(
                account.StatementCloseDay!.Value, account.PaymentDueDay!.Value, extraPrincipal, monthlyBudgetTotal, qualifyingTransactions, asOfDate, asOfDate, windowEnd);

            var description = $"{account.Name} Payment";
            foreach (var cycle in cycles)
            {
                var baseFormula = cycle.IsFuture
                    ? budgetSumFormula
                    : $"MAX({FormatNumber(cycle.ActualAmount)},{budgetSumFormula})";

                lines.Add(new LedgerLine { Date = cycle.DueDate, Description = description, Amount = -cycle.Amount, AccountId = account.Id });
                amexFormulaOverrides[(cycle.DueDate, description)] = $"=-({baseFormula}+Assumptions!$D${extraRow})";
                directionByName[description] = Direction.Expense;
            }
        }

        var lastAssumptionsRow = nextAssumptionsRow - 1;
        var totalRow = nextAssumptionsRow++;
        assumptions.Cell(totalRow, NameCol).Value = "Net Monthly Cash Flow";
        assumptions.Cell(totalRow, MonthlyAmountCol).FormulaA1 = $"=SUM(H2:H{lastAssumptionsRow})";
        assumptions.Row(totalRow).Style.Font.Bold = true;

        forecast.Cell(1, DateCol).Value = "Date";
        forecast.Cell(1, DescriptionCol).Value = "Description";
        forecast.Cell(1, AmountColF).Value = "Amount";
        forecast.Cell(1, BalanceCol).Value = "Running Balance";

        var excelRow = 2;
        var previousBalanceRow = (int?)null;
        foreach (var line in lines.OrderBy(l => l.Date))
        {
            forecast.Cell(excelRow, DateCol).Value = line.Date.ToDateTime(TimeOnly.MinValue);
            forecast.Cell(excelRow, DescriptionCol).Value = line.Description;

            var amountCell = forecast.Cell(excelRow, AmountColF);
            if (amexFormulaOverrides.TryGetValue((line.Date, line.Description), out var amexFormula))
            {
                amountCell.FormulaA1 = amexFormula;
            }
            else if (assumptionsRowByName.TryGetValue(line.Description, out var refRow))
            {
                var isIncome = directionByName.TryGetValue(line.Description, out var direction) && direction == Direction.Income;
                amountCell.FormulaA1 = isIncome ? $"=Assumptions!$D${refRow}" : $"=-Assumptions!$D${refRow}";
            }
            else
            {
                amountCell.Value = line.Amount; // one-time event - never repeats, no master cell needed
            }

            var balanceCell = forecast.Cell(excelRow, BalanceCol);
            balanceCell.FormulaA1 = previousBalanceRow is { } prevRow
                ? $"=D{prevRow}+C{excelRow}"
                : $"={FormatNumber(startingBalance)}+C{excelRow}";

            previousBalanceRow = excelRow;
            excelRow++;
        }

        ApplyFormatting(assumptions, forecast);

        return workbook;
    }

    private const string CurrencyFormat = "$#,##0.00";

    private const double MoneyColumnWidthMultiplier = 1.10;

    private static void ApplyFormatting(IXLWorksheet assumptions, IXLWorksheet forecast)
    {
        assumptions.Row(1).Style.Font.Bold = true;
        forecast.Row(1).Style.Font.Bold = true;

        assumptions.Columns(AmountCol, TotalCol).Style.NumberFormat.Format = CurrencyFormat;
        assumptions.Columns(AmountCol, TotalCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        assumptions.Columns(FrequencyCol, DirectionCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        assumptions.Column(DateAssumptionsCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        assumptions.Column(MonthlyAmountCol).Style.NumberFormat.Format = CurrencyFormat;
        assumptions.Column(MonthlyAmountCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

        forecast.Column(DateCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        forecast.Columns(AmountColF, BalanceCol).Style.NumberFormat.Format = CurrencyFormat;
        forecast.Columns(AmountColF, BalanceCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

        assumptions.Columns().AdjustToContents();
        forecast.Columns().AdjustToContents();

        // A tight autofit leaves money columns looking squished next to each other - a
        // little extra breathing room makes the sheet easier to scan.
        WidenForCurrency(assumptions.Column(AmountCol));
        WidenForCurrency(assumptions.Column(ExtraCol));
        WidenForCurrency(assumptions.Column(TotalCol));
        WidenForCurrency(forecast.Column(AmountColF));
        WidenForCurrency(forecast.Column(BalanceCol));
        WidenForCurrency(assumptions.Column(MonthlyAmountCol));
    }

    private static void WidenForCurrency(IXLColumn column) => column.Width *= MoneyColumnWidthMultiplier;

    private static void WriteAssumptionsRow(
        IXLWorksheet sheet, int row, string name, decimal amount, decimal extra, Frequency frequency, Direction direction, DateOnly? anchorDate)
    {
        sheet.Cell(row, NameCol).Value = name;
        sheet.Cell(row, AmountCol).Value = amount;
        sheet.Cell(row, ExtraCol).Value = extra;
        sheet.Cell(row, TotalCol).FormulaA1 = $"=B{row}+C{row}";
        sheet.Cell(row, FrequencyCol).Value = frequency.ToString();
        sheet.Cell(row, DirectionCol).Value = direction.ToString();
        if (anchorDate is { } date) sheet.Cell(row, DateAssumptionsCol).Value = date.ToDateTime(TimeOnly.MinValue);
        sheet.Cell(row, MonthlyAmountCol).FormulaA1 = MonthlyAmountFormula(row, frequency);
    }

    // Matches BudgetProrationService's own day-count constants (30.4375 = 365.25/12), written
    // as an Excel formula rather than pre-computed in C# so the conversion itself lives on the
    // Assumptions sheet and Excel evaluates it exactly - no repeating-decimal literal to round.
    private static string MonthlyEquivalentFormula(int row, Frequency frequency)
    {
        var cell = $"Assumptions!$D${row}";
        return frequency switch
        {
            Frequency.Weekly => $"{cell}*30.4375/7",
            Frequency.Biweekly => $"{cell}*30.4375/14",
            Frequency.Monthly => cell,
            Frequency.Quarterly => $"{cell}/3",
            Frequency.Annual => $"{cell}/12",
            _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, null)
        };
    }

    // A simpler, occurrence-count-based monthly conversion (e.g. 26 biweekly paychecks/year
    // over 12 months) than MonthlyEquivalentFormula's day-based one - this is what the user
    // asked for specifically for this column, so the two conventions differ slightly (by a
    // fraction of a percent for Weekly/Biweekly rows) from the rest of the app's own math.
    private static string MonthlyAmountFormula(int row, Frequency frequency)
    {
        var factor = frequency switch
        {
            Frequency.Weekly => "52/12",
            Frequency.Biweekly => "26/12",
            Frequency.Monthly => null,
            Frequency.Quarterly => "4/12",
            Frequency.Annual => "1/12",
            _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, null)
        };
        var magnitude = factor is null ? $"D{row}" : $"D{row}*{factor}";
        return $"=IF(F{row}=\"Income\",1,-1)*{magnitude}";
    }

    private static string FormatNumber(decimal value) => Math.Round(value, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static DateOnly ClampedDate(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
}
