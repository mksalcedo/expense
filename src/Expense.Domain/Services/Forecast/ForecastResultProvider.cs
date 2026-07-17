using Expense.Domain.Data;
using Expense.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Expense.Domain.Services.Forecast;

/// <summary>
/// Thin DI-composition wiring (like Program.cs) rather than TDD'd logic - the only
/// "behavior" here is reading the configurable horizon setting and today's date, both
/// already exercised end-to-end by ForecastEngineTests via explicit asOfDate/windowEnd.
/// </summary>
public class ForecastResultProvider(
    IDbContextFactory<ExpenseDbContext> contextFactory, ForecastEngine engine, IOptions<AppSettings> options,
    PaymentDeferralService deferrals) : IForecastResultProvider
{
    public async Task<ForecastResult> GetForecastAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var asOfDate = DateOnly.FromDateTime(DateTime.Today);
        var windowEnd = asOfDate.AddMonths(options.Value.ForecastHorizonMonths);
        return await engine.GenerateAsync(context, asOfDate, windowEnd, cancellationToken);
    }

    public async Task DeferPaymentAsync(
        int accountId, DateOnly originalDate, DateOnly deferredToDate, string? note, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await deferrals.CreateAsync(context, accountId, originalDate, deferredToDate, note);
    }

    public async Task RemoveDeferralAsync(int deferralId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await deferrals.RemoveAsync(context, deferralId);
    }
}
