namespace Expense.Domain.Services.Categories;

public class CategoriesPageData
{
    public required List<CategoryRow> Categories { get; set; }
    public required List<AccountOption> Accounts { get; set; }
}
