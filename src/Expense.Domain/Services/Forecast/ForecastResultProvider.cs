using Expense.Domain.Data;
using Expense.Domain.Entities;
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
    PaymentDeferralService deferrals, PaymentConfirmationService confirmations, PartialPaymentService partialPayments,
    TransactionReconciliationService reconciliation, PaymentAmountAdjustmentService amountAdjustments) : IForecastResultProvider
{
    public async Task<ForecastResult> GetForecastAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var asOfDate = DateOnly.FromDateTime(DateTime.Today);

        // Cheap and idempotent at this app's real data scale - keeps a manual
        // recategorization (Review Queue, Transactions page) reflected on the very next
        // forecast render instead of only at the next scheduled sync, same as before this
        // marker existed (see docs/forecast-reconciliation-marker-plan.md).
        await reconciliation.ReconcileAsync(context, asOfDate, cancellationToken: cancellationToken);

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

    public async Task ConfirmPaymentAsync(
        int accountId, int? categoryId, DateOnly originalDate, DateOnly effectiveDate, decimal amount, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await confirmations.CreateAsync(context, accountId, categoryId, originalDate, effectiveDate, amount, ConfirmationReason.AlreadyPaid);
    }

    public async Task OverridePaymentAsync(
        int accountId, int? categoryId, DateOnly originalDate, DateOnly effectiveDate, decimal amount, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await confirmations.CreateAsync(context, accountId, categoryId, originalDate, effectiveDate, amount, ConfirmationReason.Overridden);
    }

    public async Task RemoveConfirmationAsync(int confirmationId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await confirmations.RemoveAsync(context, confirmationId);
    }

    public async Task PayPartialAmountAsync(
        int accountId, DateOnly originalDate, DateOnly paidDate, decimal amount, Direction direction, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await partialPayments.CreateAsync(context, accountId, originalDate, paidDate, amount, direction, cancellationToken);
    }

    public async Task RemovePartialPaymentAsync(int partialPaymentId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await partialPayments.RemoveAsync(context, partialPaymentId, cancellationToken);
    }

    public async Task AdjustAmountAsync(int accountId, int? categoryId, DateOnly originalDate, decimal amount, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await amountAdjustments.CreateAsync(context, accountId, categoryId, originalDate, amount);
    }

    public async Task RemoveAmountAdjustmentAsync(int adjustmentId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await amountAdjustments.RemoveAsync(context, adjustmentId);
    }
}
