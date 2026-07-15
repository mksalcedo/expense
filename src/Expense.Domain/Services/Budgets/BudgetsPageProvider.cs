using Expense.Domain.Data;
using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Budgets;

/// <summary>Thin DI-composition wiring (like ForecastResultProvider) - all real logic lives in BudgetManagementService/BudgetProrationService.</summary>
public class BudgetsPageProvider(
    IDbContextFactory<ExpenseDbContext> contextFactory, BudgetManagementService budgets, BudgetProrationService proration) : IBudgetsPageProvider
{
    public async Task<BudgetsPageData> GetBudgetsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Only categories whose amount is actually entered via BudgetPeriod belong on this
        // (currently still editable) page - AccountPayment categories' amounts live on their
        // linked Account instead, and editing them here would silently do nothing.
        var budgetableStrategies = new[] { FundingStrategies.Direct, FundingStrategies.PayInFullAmex };
        var budgetedCategories = await context.Categories
            .Where(c => c.IsActive)
            .Join(context.FundingRules.Where(r => budgetableStrategies.Contains(r.Strategy)),
                c => c.Id, r => r.CategoryId, (c, _) => c)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var currentPeriods = (await budgets.GetCurrentBudgetsAsync(context)).ToDictionary(p => p.CategoryId);

        var rows = budgetedCategories.Select(c =>
        {
            currentPeriods.TryGetValue(c.Id, out var period);
            return new BudgetRow
            {
                CategoryId = c.Id,
                CategoryName = c.Name,
                Amount = period?.Amount,
                Frequency = period?.Frequency,
                EffectiveFrom = period?.EffectiveFrom,
                MonthlyEquivalent = period is null ? null : proration.Convert(period.Amount, period.Frequency, Frequency.Monthly)
            };
        }).ToList();

        return new BudgetsPageData { Budgets = rows };
    }

    public async Task SetBudgetAsync(int categoryId, decimal amount, Frequency frequency, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await budgets.SetBudgetAsync(context, categoryId, amount, frequency, DateOnly.FromDateTime(DateTime.Today));
    }
}
