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
/// MAX(actual, budget) rule), so there's no repeated value to eliminate the way a mortgage
/// payment has. Only its Extra Payment portion (which IS constant) gets a master cell; the
/// actual-vs-budget comparison is baked in as a literal per-cycle snapshot, since Excel
/// can't query Postgres live for real transaction data.
///
/// One-time events are never formula-driven either - by definition they occur exactly once.
/// </summary>
public class ForecastExcelExporter(BudgetProrationService proration, RecurrenceExpander recurrenceExpander, AmexCycleCalculator amexCycleCalculator)
{
    private const int NameCol = 1, AmountCol = 2, ExtraCol = 3, TotalCol = 4, FrequencyCol = 5, DirectionCol = 6, DateAssumptionsCol = 7;
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

        // Amex/ActiveSpending accounts - only the Extra Payment portion is a master cell.
        var amexFormulaOverrides = new Dictionary<(DateOnly Date, string Description), string>();
        var activeSpendingAccounts = await context.Accounts
            .Where(a => a.Type == AccountType.ActiveSpending && a.IsActive && a.StatementCloseDay != null && a.PaymentDueDay != null)
            .ToListAsync(cancellationToken);

        foreach (var account in activeSpendingAccounts)
        {
            var qualifyingCategoryIds = await context.FundingRules
                .Where(f => f.Strategy == FundingStrategies.PayInFullAmex)
                .Select(f => f.CategoryId)
                .ToListAsync(cancellationToken);

            decimal monthlyBudgetTotal = 0m;
            foreach (var categoryId in qualifyingCategoryIds)
            {
                var currentPeriod = await context.BudgetPeriods
                    .Where(p => p.CategoryId == categoryId && p.EffectiveFrom <= asOfDate && (p.EffectiveThrough == null || p.EffectiveThrough >= asOfDate))
                    .FirstOrDefaultAsync(cancellationToken);
                if (currentPeriod is not null)
                {
                    monthlyBudgetTotal += proration.Convert(currentPeriod.Amount, currentPeriod.Frequency, Frequency.Monthly);
                }
            }

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
                var baseAmount = cycle.Amount - extraPrincipal;
                lines.Add(new LedgerLine { Date = cycle.DueDate, Description = description, Amount = -cycle.Amount, AccountId = account.Id });
                amexFormulaOverrides[(cycle.DueDate, description)] = $"=-({FormatNumber(baseAmount)}+Assumptions!$D${extraRow})";
                directionByName[description] = Direction.Expense;
            }
        }

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
    }

    private static void WidenForCurrency(IXLColumn column) => column.Width *= MoneyColumnWidthMultiplier;

    private static void WriteAssumptionsRow(
        IXLWorksheet sheet, int row, string name, decimal amount, decimal extra, Frequency frequency, Direction direction, DateOnly anchorDate)
    {
        sheet.Cell(row, NameCol).Value = name;
        sheet.Cell(row, AmountCol).Value = amount;
        sheet.Cell(row, ExtraCol).Value = extra;
        sheet.Cell(row, TotalCol).FormulaA1 = $"=B{row}+C{row}";
        sheet.Cell(row, FrequencyCol).Value = frequency.ToString();
        sheet.Cell(row, DirectionCol).Value = direction.ToString();
        sheet.Cell(row, DateAssumptionsCol).Value = anchorDate.ToDateTime(TimeOnly.MinValue);
    }

    private static string FormatNumber(decimal value) => Math.Round(value, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static DateOnly ClampedDate(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
}
