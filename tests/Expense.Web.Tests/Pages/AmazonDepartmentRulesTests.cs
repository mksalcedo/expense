using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Ingestion.Amazon;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class AmazonDepartmentRulesTests : BunitContext
{
    private class FakeProvider : IAmazonDepartmentRulesPageProvider
    {
        public List<AmazonDepartmentMapping> Mappings { get; set; } = [];
        public List<Category> Categories { get; set; } =
        [
            new() { Id = 1, Name = "Groceries" },
            new() { Id = 2, Name = "Supplements" },
            new() { Id = 3, Name = "Off-Budget/Misc" },
        ];

        public string? LastUpsertedDepartment { get; private set; }
        public int? LastUpsertedCategoryId { get; private set; }
        public int? LastDeletedId { get; private set; }
        public int NextReapplyCount { get; set; }

        public Task<AmazonDepartmentRulesPageData> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AmazonDepartmentRulesPageData { Mappings = Mappings, Categories = Categories });

        public Task<int> UpsertAsync(string departmentName, int categoryId, CancellationToken cancellationToken = default)
        {
            LastUpsertedDepartment = departmentName;
            LastUpsertedCategoryId = categoryId;
            return Task.FromResult(NextReapplyCount);
        }

        public Task DeleteAsync(int mappingId, CancellationToken cancellationToken = default)
        {
            LastDeletedId = mappingId;
            Mappings = Mappings.Where(m => m.Id != mappingId).ToList();
            return Task.CompletedTask;
        }
    }

    private FakeProvider Register(params AmazonDepartmentMapping[] mappings)
    {
        var provider = new FakeProvider { Mappings = mappings.ToList() };
        Services.AddSingleton<IAmazonDepartmentRulesPageProvider>(provider);
        return provider;
    }

    [Fact]
    public void ListsEveryMapping_WithItsCategoryPreselected()
    {
        Register(
            new AmazonDepartmentMapping { Id = 10, DepartmentName = "Grocery", CategoryId = 1 },
            new AmazonDepartmentMapping { Id = 11, DepartmentName = "Supplements", CategoryId = 2 });

        var cut = Render<AmazonDepartmentRules>();

        Assert.Contains("Grocery", cut.Markup);
        Assert.Contains("Supplements", cut.Markup);
        var select10 = cut.Find("#rule-category-10");
        Assert.Equal("1", select10.GetAttribute("value") ?? select10.Children.First(o => o.HasAttribute("selected")).GetAttribute("value"));
    }

    [Fact]
    public void AddingARule_CallsUpsert_AndReportsReFiledCount()
    {
        var provider = Register();
        provider.NextReapplyCount = 3;

        var cut = Render<AmazonDepartmentRules>();
        cut.Find("#new-rule-department").Change("Kitchen");
        cut.Find("#new-rule-category").Change("3");
        cut.Find("#new-rule-add").Click();

        Assert.Equal("Kitchen", provider.LastUpsertedDepartment);
        Assert.Equal(3, provider.LastUpsertedCategoryId);
        Assert.Contains("3 order(s)", cut.Find("#rules-message").TextContent);
    }

    [Fact]
    public void AddButton_IsDisabled_UntilBothFieldsAreSet()
    {
        Register();

        var cut = Render<AmazonDepartmentRules>();

        Assert.True(cut.Find("#new-rule-add").HasAttribute("disabled"));
        cut.Find("#new-rule-department").Change("Kitchen");
        Assert.True(cut.Find("#new-rule-add").HasAttribute("disabled")); // still no category
        cut.Find("#new-rule-category").Change("3");
        Assert.False(cut.Find("#new-rule-add").HasAttribute("disabled"));
    }

    [Fact]
    public void RepointingARulesCategory_CallsUpsertWithTheSameDepartment()
    {
        var provider = Register(new AmazonDepartmentMapping { Id = 10, DepartmentName = "Grocery", CategoryId = 1 });

        var cut = Render<AmazonDepartmentRules>();
        cut.Find("#rule-category-10").Change("3");

        Assert.Equal("Grocery", provider.LastUpsertedDepartment);
        Assert.Equal(3, provider.LastUpsertedCategoryId);
    }

    [Fact]
    public void DeletingARule_CallsDelete_AndRemovesTheRow()
    {
        var provider = Register(new AmazonDepartmentMapping { Id = 10, DepartmentName = "Grocery", CategoryId = 1 });

        var cut = Render<AmazonDepartmentRules>();
        cut.Find("#rule-delete-10").Click();

        Assert.Equal(10, provider.LastDeletedId);
        Assert.Empty(cut.FindAll("#rule-delete-10"));
    }
}
