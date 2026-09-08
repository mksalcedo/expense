using Expense.Domain.Entities;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Entities;

public class AmazonDepartmentMappingTests : DatabaseTestBase
{
    [Fact]
    public async Task Mapping_SavedAndReloaded_RoundTripsWithItsCategory()
    {
        var supplements = new Category { Name = "Supplements" };
        Context.Categories.Add(supplements);
        await Context.SaveChangesAsync();

        var mapping = new AmazonDepartmentMapping { DepartmentName = "Nutrition & Wellness", CategoryId = supplements.Id };
        Context.AmazonDepartmentMappings.Add(mapping);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.AmazonDepartmentMappings
            .Include(m => m.Category)
            .SingleAsync(m => m.Id == mapping.Id);

        Assert.Equal("Nutrition & Wellness", reloaded.DepartmentName);
        Assert.Equal("Supplements", reloaded.Category.Name);
    }

    [Fact]
    public async Task DepartmentName_IsUnique_CaseInsensitively()
    {
        var supplements = new Category { Name = "Supplements" };
        Context.Categories.Add(supplements);
        await Context.SaveChangesAsync();

        Context.AmazonDepartmentMappings.Add(new AmazonDepartmentMapping { DepartmentName = "Supplements", CategoryId = supplements.Id });
        await Context.SaveChangesAsync();

        Context.AmazonDepartmentMappings.Add(new AmazonDepartmentMapping { DepartmentName = "supplements", CategoryId = supplements.Id });

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }
}
