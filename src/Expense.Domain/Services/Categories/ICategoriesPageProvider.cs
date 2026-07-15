namespace Expense.Domain.Services.Categories;

/// <summary>Thin abstraction over CategoryManagementService so UI components can be tested against a fake result.</summary>
public interface ICategoriesPageProvider
{
    Task<CategoriesPageData> GetCategoriesAsync(CancellationToken cancellationToken = default);

    Task CreateCategoryAsync(string name, bool isBudgeted, string fundingStrategy, CancellationToken cancellationToken = default);

    Task RenameCategoryAsync(int categoryId, string newName, CancellationToken cancellationToken = default);

    Task SetFundingStrategyAsync(int categoryId, string strategy, CancellationToken cancellationToken = default);

    Task DeactivateCategoryAsync(int categoryId, CancellationToken cancellationToken = default);
}
