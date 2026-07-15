using Expense.Domain.Entities;

namespace Expense.Domain.Services.Budgets;

/// <summary>Thin abstraction over BudgetManagementService so UI components can be tested against a fake result.</summary>
public interface IBudgetsPageProvider
{
    Task<BudgetsPageData> GetBudgetsAsync(CancellationToken cancellationToken = default);

    Task SetBudgetAsync(int categoryId, decimal amount, Frequency frequency, CancellationToken cancellationToken = default);
}
