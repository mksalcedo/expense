using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Forecast;

/// <summary>
/// CRUD for PaymentAmountAdjustment - lets a user correct one specific occurrence's
/// forecasted amount to a real known figure without marking it resolved/paid.
/// </summary>
public class PaymentAmountAdjustmentService
{
    public async Task<PaymentAmountAdjustment> CreateAsync(
        ExpenseDbContext context, int accountId, int? categoryId, DateOnly originalDate, decimal amount)
    {
        var adjustment = new PaymentAmountAdjustment
        {
            AccountId = accountId,
            CategoryId = categoryId,
            OriginalDate = originalDate,
            Amount = amount,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.PaymentAmountAdjustments.Add(adjustment);
        await context.SaveChangesAsync();
        return adjustment;
    }

    public async Task RemoveAsync(ExpenseDbContext context, int adjustmentId)
    {
        var adjustment = await context.PaymentAmountAdjustments.SingleAsync(a => a.Id == adjustmentId);
        context.PaymentAmountAdjustments.Remove(adjustment);
        await context.SaveChangesAsync();
    }
}
