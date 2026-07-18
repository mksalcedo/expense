using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Budgets;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class BudgetsTests : BunitContext
{
    private class FakeBudgetsPageProvider : IBudgetsPageProvider
    {
        public List<BudgetRow> Rows { get; set; } = [];

        public int? LastCategoryId { get; private set; }
        public decimal? LastAmount { get; private set; }
        public Frequency? LastFrequency { get; private set; }

        public Task<BudgetsPageData> GetBudgetsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BudgetsPageData { Budgets = Rows });

        public Task SetBudgetAsync(int categoryId, decimal amount, Frequency frequency, CancellationToken cancellationToken = default)
        {
            LastCategoryId = categoryId;
            LastAmount = amount;
            LastFrequency = frequency;
            return Task.CompletedTask;
        }
    }

    private static FakeBudgetsPageProvider MakeProvider() => new()
    {
        Rows =
        [
            new BudgetRow { CategoryId = 1, CategoryName = "Groceries", Amount = 450m, Frequency = Frequency.Weekly, EffectiveFrom = new DateOnly(2026, 1, 1), MonthlyEquivalent = 1957m },
            new BudgetRow { CategoryId = 2, CategoryName = "Medical", Amount = null, Frequency = null, EffectiveFrom = null, MonthlyEquivalent = null }
        ]
    };

    [Fact]
    public void Budgets_RendersEachCategoryWithItsCurrentAmountAndMonthlyEquivalent()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IBudgetsPageProvider>(provider);

        var cut = Render<Budgets>();

        Assert.Contains("Groceries", cut.Markup);
        Assert.Contains("Medical", cut.Markup);
        Assert.Equal("450", cut.Find("#budget-amount-1").GetAttribute("value"));
    }

    [Fact]
    public void Budgets_RightAlignsTheMonthlyEquivalentColumn()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IBudgetsPageProvider>(provider);

        var cut = Render<Budgets>();

        var header = cut.FindAll("th").Single(h => h.TextContent == "Monthly equivalent");
        Assert.Equal("text-right", header.GetAttribute("class"));
    }

    [Fact]
    public void ChangingAmount_SavesWithTheRowsCurrentFrequency()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IBudgetsPageProvider>(provider);

        var cut = Render<Budgets>();
        cut.Find("#budget-amount-1").Change("500");

        Assert.Equal(1, provider.LastCategoryId);
        Assert.Equal(500m, provider.LastAmount);
        Assert.Equal(Frequency.Weekly, provider.LastFrequency);
    }

    [Fact]
    public void ChangingFrequency_SavesWithTheRowsCurrentAmount()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IBudgetsPageProvider>(provider);

        var cut = Render<Budgets>();
        cut.Find("#budget-frequency-1").Change("Monthly");

        Assert.Equal(1, provider.LastCategoryId);
        Assert.Equal(450m, provider.LastAmount);
        Assert.Equal(Frequency.Monthly, provider.LastFrequency);
    }

    [Fact]
    public void SettingAnAmountForACategoryWithNoPriorBudget_SavesWithTheDefaultFrequency()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IBudgetsPageProvider>(provider);

        var cut = Render<Budgets>();
        cut.Find("#budget-amount-2").Change("200");

        Assert.Equal(2, provider.LastCategoryId);
        Assert.Equal(200m, provider.LastAmount);
        Assert.NotNull(provider.LastFrequency);
    }
}
