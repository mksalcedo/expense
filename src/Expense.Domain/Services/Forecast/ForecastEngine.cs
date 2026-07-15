using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Budgets;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Forecast;

/// <summary>
/// Ties RecurrenceExpander + AmexCycleCalculator + the latest checking balance +
/// each debt account's configured payment into one dated ledger with a running
/// balance. Always starts from the latest real checking balance, never from
/// reconciling history.
/// </summary>
public class ForecastEngine(BudgetProrationService proration, RecurrenceExpander recurrenceExpander, AmexCycleCalculator amexCycleCalculator)
{
    public async Task<ForecastResult> GenerateAsync(
        ExpenseDbContext context, DateOnly asOfDate, DateOnly windowEnd, CancellationToken cancellationToken = default)
    {
        var startingBalance = await context.CheckingBalanceSnapshots
            .OrderByDescending(s => s.AsOfDate)
            .Select(s => s.Balance)
            .FirstOrDefaultAsync(cancellationToken);

        var directPeriods = await context.BudgetPeriods
            .Where(p => p.EffectiveThrough == null && p.Anchor != null && p.AccountId != null)
            .Join(context.FundingRules.Where(f => f.Strategy == FundingStrategies.Direct),
                p => p.CategoryId, f => f.CategoryId, (p, _) => p)
            .Include(p => p.Category)
            .Where(p => p.Category.IsActive)
            .ToListAsync(cancellationToken);

        var recurringRules = directPeriods.Select(p => new RecurringRule
        {
            Name = p.Category.Name,
            Direction = p.Direction,
            Amount = p.Amount,
            Frequency = p.Frequency,
            Anchor = p.Anchor!.Value,
            AccountId = p.AccountId!.Value,
            Active = true,
            StartDate = DateOnly.MinValue
        }).ToList();

        var oneTimeEvents = await context.OneTimeEvents.ToListAsync(cancellationToken);

        var lines = recurrenceExpander.Expand(recurringRules, oneTimeEvents, asOfDate, windowEnd);

        var debtAccounts = await context.Accounts
            .Where(a => a.Type == AccountType.Debt && a.IsActive && a.PaymentDueDay != null)
            .ToListAsync(cancellationToken);

        foreach (var account in debtAccounts)
        {
            var amount = (account.MinPayment ?? 0m) + (account.ExtraPayment ?? 0m);
            if (amount == 0m) continue;

            var syntheticRule = new RecurringRule
            {
                Name = $"{account.Name} Payment",
                Direction = Direction.Expense,
                Amount = amount,
                Frequency = Frequency.Monthly,
                Anchor = ClampedDate(asOfDate.Year, asOfDate.Month, account.PaymentDueDay!.Value),
                AccountId = account.Id,
                Active = true,
                StartDate = DateOnly.MinValue
            };

            lines.AddRange(recurrenceExpander.Expand([syntheticRule], [], asOfDate, windowEnd));
        }

        var activeSpendingAccounts = await context.Accounts
            .Where(a => a.Type == AccountType.ActiveSpending && a.IsActive
                        && a.StatementCloseDay != null && a.PaymentDueDay != null)
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
                    .Where(p => p.CategoryId == categoryId
                                && p.EffectiveFrom <= asOfDate
                                && (p.EffectiveThrough == null || p.EffectiveThrough >= asOfDate))
                    .FirstOrDefaultAsync(cancellationToken);

                if (currentPeriod is not null)
                {
                    monthlyBudgetTotal += proration.Convert(currentPeriod.Amount, currentPeriod.Frequency, Frequency.Monthly);
                }
            }

            var qualifyingTransactions = await context.BankTransactions
                .Where(t => t.AccountId == account.Id && t.PostedDate != null
                            && t.CategoryId != null && qualifyingCategoryIds.Contains(t.CategoryId.Value))
                .ToListAsync(cancellationToken);

            var cycles = amexCycleCalculator.CalculateDuePayments(
                account.StatementCloseDay!.Value, account.PaymentDueDay!.Value, account.ExtraPayment ?? 0m,
                monthlyBudgetTotal, qualifyingTransactions, asOfDate, asOfDate, windowEnd);

            foreach (var cycle in cycles)
            {
                lines.Add(new LedgerLine
                {
                    Date = cycle.DueDate, Description = $"{account.Name} Payment", Amount = -cycle.Amount, AccountId = account.Id
                });
            }
        }

        var rows = new List<ForecastLedgerRow>();
        var runningBalance = startingBalance;
        foreach (var line in lines.OrderBy(l => l.Date))
        {
            runningBalance += line.Amount;
            rows.Add(new ForecastLedgerRow { Date = line.Date, Description = line.Description, Amount = line.Amount, RunningBalance = runningBalance });
        }

        return new ForecastResult { StartingBalance = startingBalance, Rows = rows };
    }

    private static DateOnly ClampedDate(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
}
