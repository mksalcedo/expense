using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Ingestion.ManualCharges;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Forecast;

/// <summary>
/// Classifies each categorized BankTransaction - posted, or still pending via Plaid/a manual
/// Amex screenshot (same carve-out already used by ForecastEngine/ForecastAccuracyService/
/// SpendingTrackerService for "does this count yet") - against the nearest occurrence in
/// its category's real recurring schedule, once - durably recording the answer on the
/// transaction itself (ReconciledOccurrenceDate) instead of ForecastEngine re-deriving it
/// via a date-window search relative to "today" on every render. See
/// docs/forecast-reconciliation-marker-plan.md for why: a search-based approach can never
/// be complete (there's always some "how far back is far enough" question relative to
/// today), while this depends only on each transaction's own fixed PostedDate against the
/// category's actual history, so it's correct regardless of how much time has since passed.
///
/// Re-derives the classification for every qualifying transaction on every call, rather
/// than only ones currently null - cheap at this app's real data scale, and it means a
/// transaction that gets recategorized later (there are several call sites across
/// CategorizationService/TransactionManagementService that can change CategoryId) self
/// -corrects on the next run instead of every one of those call sites needing to remember
/// to reset the marker.
/// </summary>
public class TransactionReconciliationService(RecurrenceExpander recurrenceExpander, AmexCycleCalculator amexCycleCalculator)
{
    public async Task<List<ReconciliationChange>> ReconcileAsync(
        ExpenseDbContext context, DateOnly asOfDate, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        var earliestBudgetStart = await context.BudgetPeriods
            .Select(p => (DateOnly?)p.EffectiveFrom)
            .MinAsync(cancellationToken);
        var earliestTransactionDate = await context.BankTransactions
            .Where(t => t.PostedDate != null)
            .Select(t => t.PostedDate)
            .MinAsync(cancellationToken);

        // A generous month of buffer before the earliest known fact (budget or real
        // transaction) - not a precisely-computed bound, since there's nothing to be
        // precise about here: this is re-derived fresh from real data every call, not a
        // fixed radius relative to "today" that could ever go stale.
        var knownDates = new[] { earliestBudgetStart, earliestTransactionDate }.Where(d => d is not null).Select(d => d!.Value).ToList();
        var earliestRelevantDate = (knownDates.Count > 0 ? knownDates.Min() : asOfDate).AddDays(-31);

        // Every historical BudgetPeriod version, not just the current one - a transaction
        // from months ago must be classified against whatever was actually scheduled at
        // the time (same fix as ForecastAccuracyService).
        var directPeriods = await context.BudgetPeriods
            .Where(p => p.Anchor != null && p.AccountId != null && p.EffectiveFrom <= asOfDate)
            .Join(context.FundingRules.Where(f => f.Strategy == FundingStrategies.Direct),
                p => p.CategoryId, f => f.CategoryId, (p, _) => p)
            .Include(p => p.Category)
            .ToListAsync(cancellationToken);

        var recurringRules = directPeriods.Select(p => new RecurringRule
        {
            Name = p.Category.Name,
            Direction = p.Direction,
            Amount = p.Amount,
            Frequency = p.Frequency,
            Anchor = p.Anchor!.Value,
            AccountId = p.AccountId!.Value,
            CategoryId = p.CategoryId,
            Active = true,
            StartDate = p.EffectiveFrom,
            EndDate = p.EffectiveThrough
        }).ToList();

        var accountPaymentCategoryIds = await context.FundingRules
            .Where(f => f.Strategy == FundingStrategies.AccountPayment && f.AccountId != null)
            .ToDictionaryAsync(f => f.AccountId!.Value, f => (int?)f.CategoryId, cancellationToken);

        var debtAccounts = await context.Accounts
            .Where(a => a.Type == AccountType.Debt && a.PaymentDueDay != null)
            .ToListAsync(cancellationToken);

        foreach (var account in debtAccounts)
        {
            var amount = (account.MinPayment ?? 0m) + (account.ExtraPayment ?? 0m);
            if (amount == 0m) continue;

            recurringRules.Add(new RecurringRule
            {
                Name = $"{account.Name} Payment",
                Direction = Direction.Expense,
                Amount = amount,
                Frequency = Frequency.Monthly,
                Anchor = ClampedDate(earliestRelevantDate.Year, earliestRelevantDate.Month, account.PaymentDueDay!.Value),
                AccountId = account.Id,
                CategoryId = accountPaymentCategoryIds.GetValueOrDefault(account.Id),
                Active = true,
                StartDate = earliestRelevantDate
            });
        }

        // Widened forward by MaxMatchWindowDays, mirroring ForecastEngine's own backward
        // widening - a real transaction can post before its occurrence's own anchor date
        // (e.g. income that trickles in ahead of a monthly anchor), and that occurrence must
        // already exist as a candidate for such a transaction to have anything to match.
        var candidates = recurrenceExpander.Expand(recurringRules, [], earliestRelevantDate, asOfDate.AddDays(RecurrenceExpander.MaxMatchWindowDays))
            .Where(l => l.CategoryId is not null)
            .ToList();

        // Amex/ActiveSpending payment categories don't follow a simple day-of-month rule -
        // their due dates come from AmexCycleCalculator's own statement-cycle logic (same
        // as ForecastEngine/ForecastAccuracyService). Only DueDate/IsFuture are read here,
        // so the amount/charge-transaction arguments are irrelevant placeholders.
        var activeSpendingAccounts = await context.Accounts
            .Where(a => a.Type == AccountType.ActiveSpending && a.StatementCloseDay != null && a.PaymentDueDay != null)
            .ToListAsync(cancellationToken);

        foreach (var account in activeSpendingAccounts)
        {
            var categoryId = accountPaymentCategoryIds.GetValueOrDefault(account.Id);
            if (categoryId is null) continue;

            var cycles = amexCycleCalculator.CalculateDuePayments(
                account.StatementCloseDay!.Value, account.PaymentDueDay!.Value, 0m, 0m, [], asOfDate, earliestRelevantDate, asOfDate);

            candidates.AddRange(cycles.Where(c => !c.IsFuture).Select(cycle => new LedgerLine
            {
                Date = cycle.DueDate, Description = $"{account.Name} Payment", Amount = 0m, AccountId = account.Id,
                CategoryId = categoryId, MatchWindowDays = RecurrenceExpander.MatchWindowDaysFor(Frequency.Monthly)
            }));
        }

        var transactions = await context.BankTransactions
            .Where(t => t.CategoryId != null
                        && (t.PostedDate != null || t.ImportSource == ManualChargeMatchingService.ManualScreenshotImportSource || t.ImportSource == "Plaid"))
            .ToListAsync(cancellationToken);

        var changes = new List<ReconciliationChange>();
        foreach (var txn in transactions)
        {
            // Falls back to TransactionDate for a still-pending row (PostedDate null) - once
            // Plaid merges the real posting into this same row, PostedDate becomes the
            // authoritative date and this naturally starts using that instead, on the very
            // next reconciliation run (this whole method re-derives fresh every time).
            var effectiveDate = txn.PostedDate ?? txn.TransactionDate;

            var best = candidates
                .Where(c => c.CategoryId == txn.CategoryId
                            && effectiveDate >= c.Date.AddDays(-c.MatchWindowDays)
                            && effectiveDate <= c.Date.AddDays(c.MatchWindowDays))
                .OrderBy(c => Math.Abs(c.Date.DayNumber - effectiveDate.DayNumber))
                .FirstOrDefault();

            var newValue = best?.Date;
            if (txn.ReconciledOccurrenceDate == newValue) continue;

            changes.Add(new ReconciliationChange
            {
                TransactionId = txn.Id, Description = txn.Description, OldValue = txn.ReconciledOccurrenceDate, NewValue = newValue
            });
            if (!dryRun)
            {
                txn.ReconciledOccurrenceDate = newValue;
            }
        }

        if (!dryRun)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return changes;
    }

    private static DateOnly ClampedDate(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
}
