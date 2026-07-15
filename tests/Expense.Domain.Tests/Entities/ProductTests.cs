using Expense.Domain.Entities;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Entities;

public class ProductTests : DatabaseTestBase
{
    [Fact]
    public async Task Product_SavedAndReloaded_RoundTripsWithItsCategory()
    {
        var category = new Category { Name = "Supplements" };
        Context.Categories.Add(category);
        await Context.SaveChangesAsync();

        var product = new Product { ProductPattern = "Doctor's Best Magnesium", CategoryId = category.Id };
        Context.Products.Add(product);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.Products
            .Include(p => p.Category)
            .SingleAsync(p => p.Id == product.Id);

        Assert.Equal("Doctor's Best Magnesium", reloaded.ProductPattern);
        Assert.Equal("Supplements", reloaded.Category.Name);
    }
}
