namespace Expense.Domain.Services.Categories;

/// <summary>Thin abstraction over CategoryManagementService so UI components can be tested against a fake result.</summary>
public interface ICategoriesPageProvider
{
    Task<CategoriesPageData> GetCategoriesAsync(CancellationToken cancellationToken = default);

    Task CreateCategoryAsync(string name, string fundingStrategy, BudgetInput? budget = null, CancellationToken cancellationToken = default);

    Task UpdateCategoryAsync(int categoryId, string name, string fundingStrategy, BudgetInput? budget = null, CancellationToken cancellationToken = default);

    Task DeactivateCategoryAsync(int categoryId, CancellationToken cancellationToken = default);

    Task ReactivateCategoryAsync(int categoryId, CancellationToken cancellationToken = default);
}
