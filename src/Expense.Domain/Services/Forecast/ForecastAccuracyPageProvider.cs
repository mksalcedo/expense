using Expense.Domain.Data;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Forecast;

/// <summary>Thin DI-composition wiring (like ForecastResultProvider) - all real logic lives in ForecastAccuracyService.</summary>
public class ForecastAccuracyPageProvider(IDbContextFactory<ExpenseDbContext> contextFactory, ForecastAccuracyService accuracy)
    : IForecastAccuracyPageProvider
{
    private const int LookbackDays = 90;

    public async Task<List<AccuracyComparison>> GetRecentAccuracyAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var asOfDate = DateOnly.FromDateTime(DateTime.Today);
        var results = await accuracy.GetRecentAccuracyAsync(context, asOfDate, LookbackDays, cancellationToken);
        return results.OrderByDescending(r => Math.Abs(r.Delta ?? 0m)).ToList();
    }
}
