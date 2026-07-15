using Expense.Domain.Data;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.SpendingTracker;

/// <summary>
/// Thin DI-composition wiring (like ForecastResultProvider) - the only "behavior" here is
/// reading today's date, already exercised end-to-end by SpendingTrackerServiceTests via
/// explicit asOfDate.
/// </summary>
public class SpendingTrackerPageProvider(IDbContextFactory<ExpenseDbContext> contextFactory, SpendingTrackerService tracker) : ISpendingTrackerPageProvider
{
    public async Task<SpendingTrackerPageData> GetSpendingTrackerAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var asOfDate = DateOnly.FromDateTime(DateTime.Today);

        var week = await tracker.GetCurrentWeekAsync(context, asOfDate, cancellationToken);
        var month = await tracker.GetCurrentMonthAsync(context, asOfDate, cancellationToken);

        return new SpendingTrackerPageData { Week = week, Month = month };
    }
}
