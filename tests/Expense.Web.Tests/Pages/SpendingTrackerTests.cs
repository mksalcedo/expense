using Bunit;
using Expense.Domain.Services.SpendingTracker;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class SpendingTrackerTests : BunitContext
{
    private class FakeSpendingTrackerPageProvider(SpendingTrackerPageData data) : ISpendingTrackerPageProvider
    {
        public List<(int CategoryId, bool StartingNextPeriod)> ResetCalls { get; } = [];
        public List<(int CategoryId, DateOnly PeriodStart, DateOnly PeriodEnd)> TransactionRequests { get; } = [];
        public Dictionary<int, List<CategoryTransactionLine>> TransactionsByCategory { get; set; } = [];

        public Task<SpendingTrackerPageData> GetSpendingTrackerAsync(CancellationToken cancellationToken = default) => Task.FromResult(data);
        public Task<SpendingTrackerResult> GetWeekAsync(DateOnly referenceDate, CancellationToken cancellationToken = default) => Task.FromResult(data.Week);
        public Task<SpendingTrackerResult> GetMonthAsync(DateOnly referenceDate, CancellationToken cancellationToken = default) => Task.FromResult(data.Month);

        public Task ResetCarryoverAsync(int categoryId, bool startingNextPeriod, CancellationToken cancellationToken = default)
        {
            ResetCalls.Add((categoryId, startingNextPeriod));
            return Task.CompletedTask;
        }

        public Task<List<CategoryTransactionLine>> GetCategoryTransactionsAsync(int categoryId, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
        {
            TransactionRequests.Add((categoryId, periodStart, periodEnd));
            return Task.FromResult(TransactionsByCategory.GetValueOrDefault(categoryId, []));
        }
    }

    private static SpendingTrackerPageData MakeData() => new()
    {
        Week = new SpendingTrackerResult
        {
            PeriodStart = new DateOnly(2026, 7, 12),
            PeriodEnd = new DateOnly(2026, 7, 18),
            Categories =
            [
                new CategorySpendingSummary { CategoryId = 1, CategoryName = "Groceries", Budget = 450m, Actual = 120m },
                new CategorySpendingSummary { CategoryId = 2, CategoryName = "Restaurants", Budget = 150m, Actual = 200m }
            ],
            PendingAmount = 30m
        },
        Month = new SpendingTrackerResult
        {
            PeriodStart = new DateOnly(2026, 7, 1),
            PeriodEnd = new DateOnly(2026, 7, 31),
            Categories =
            [
                new CategorySpendingSummary { CategoryId = 1, CategoryName = "Groceries", Budget = 1956.70m, Actual = 800m }
            ],
            PendingAmount = 60m
        }
    };

    [Fact]
    public void SpendingTracker_RendersWeekAndMonthCategorySummaries()
    {
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeData()));

        var cut = Render<SpendingTracker>();

        Assert.Contains("Groceries", cut.Markup);
        Assert.Contains("Restaurants", cut.Markup);
        Assert.Contains("450.00", cut.Markup);
        Assert.Contains("120.00", cut.Markup);
        Assert.Contains("1,956.70", cut.Markup);
    }

    [Fact]
    public void SpendingTracker_RendersRemainingIncludingOverspend()
    {
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeData()));

        var cut = Render<SpendingTracker>();

        // Groceries week: 450 - 120 = 330 remaining
        Assert.Contains("330.00", cut.Markup);
        // Restaurants week: 150 - 200 = -50 (overspent)
        Assert.Contains("-50.00", cut.Markup);
    }

    [Fact]
    public void SpendingTracker_RendersPendingAmountForBothPeriods()
    {
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeData()));

        var cut = Render<SpendingTracker>();

        Assert.Contains("Pending", cut.Markup);
        Assert.Contains("30.00", cut.Markup);
        Assert.Contains("60.00", cut.Markup);
    }

    [Fact]
    public void SpendingTracker_RightAlignsBudgetActualAndRemainingColumns()
    {
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeData()));

        var cut = Render<SpendingTracker>();

        Assert.All(cut.FindAll("th"), h =>
        {
            if (h.TextContent is "Budget" or "Actual" or "Remaining")
            {
                Assert.Equal("text-right", h.GetAttribute("class"));
            }
        });
    }

    [Fact]
    public void SpendingTracker_RendersATotalsRow_IncludingPendingInTheActualAndRemainingTotals()
    {
        // Week: Groceries (450 budget, 120 actual) + Restaurants (150 budget, 200 actual)
        // + 30 pending. Budget total = 600. Actual total = 120+200+30 = 350 - pending has
        // to count here, since it's real money already spent, just not yet categorized.
        // Remaining total = 600-350 = 250.
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeData()));

        var cut = Render<SpendingTracker>();

        var totalsRow = cut.Find("#week-totals-row");
        Assert.Contains("600.00", totalsRow.TextContent);
        Assert.Contains("350.00", totalsRow.TextContent);
        Assert.Contains("250.00", totalsRow.TextContent);
    }

    [Fact]
    public void SpendingTracker_RendersPeriodDateRanges()
    {
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeData()));

        var cut = Render<SpendingTracker>();

        Assert.Contains("2026-07-12", cut.Markup);
        Assert.Contains("2026-07-18", cut.Markup);
        Assert.Contains("2026-07-01", cut.Markup);
        Assert.Contains("2026-07-31", cut.Markup);
    }

    private static SpendingTrackerPageData MakeCarryoverData() => new()
    {
        Week = new SpendingTrackerResult
        {
            PeriodStart = new DateOnly(2026, 7, 12),
            PeriodEnd = new DateOnly(2026, 7, 18),
            Categories =
            [
                new CategorySpendingSummary
                {
                    CategoryId = 1, CategoryName = "Groceries", Budget = 450m, Actual = 470m,
                    IsCarryoverTracked = true, CarriedIn = 50m, RollingBalance = 30m, CarryoverCap = 450m
                }
            ],
            PendingAmount = 0m
        },
        Month = new SpendingTrackerResult
        {
            PeriodStart = new DateOnly(2026, 7, 1),
            PeriodEnd = new DateOnly(2026, 7, 31),
            Categories = [new CategorySpendingSummary { CategoryId = 1, CategoryName = "Groceries", Budget = 1956.70m, Actual = 800m }],
            PendingAmount = 0m
        }
    };

    [Fact]
    public void CarryoverTrackedCategory_ShowsTheRollingBalance_NotPlainRemaining()
    {
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeCarryoverData()));

        var cut = Render<SpendingTracker>();

        // Plain Remaining would be 450 - 470 = -20; the rolling balance (30, including the
        // +50 carried in from a prior surplus week) is what should actually be shown.
        Assert.Contains("30.00", cut.Markup);
    }

    [Fact]
    public void CarryoverTrackedCategory_ShowsTheCarriedInNote_WithTheCap()
    {
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeCarryoverData()));

        var cut = Render<SpendingTracker>();

        var note = cut.Find("#carried-in-note-1");
        Assert.Contains("+50.00 carried in", note.TextContent);
        Assert.Contains("450.00", note.TextContent);
    }

    [Fact]
    public void NonCarryoverCategory_ShowsNoCarriedInNote_AndNoResetButton()
    {
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeData()));

        var cut = Render<SpendingTracker>();

        Assert.Empty(cut.FindAll("[id^='carried-in-note-']"));
        Assert.Empty(cut.FindAll("[id^='reset-carryover-btn-']"));
    }

    [Fact]
    public void ClickingReset_ShowsThisPeriodAndNextPeriodChoices()
    {
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeCarryoverData()));

        var cut = Render<SpendingTracker>();
        cut.Find("#reset-carryover-btn-1").Click();

        Assert.NotEmpty(cut.FindAll("#reset-this-period-btn-1"));
        Assert.NotEmpty(cut.FindAll("#reset-next-period-btn-1"));
    }

    [Fact]
    public void ClickingResetThisPeriod_CallsTheProvider_WithStartingNextPeriodFalse()
    {
        var provider = new FakeSpendingTrackerPageProvider(MakeCarryoverData());
        Services.AddSingleton<ISpendingTrackerPageProvider>(provider);

        var cut = Render<SpendingTracker>();
        cut.Find("#reset-carryover-btn-1").Click();
        cut.Find("#reset-this-period-btn-1").Click();

        var call = Assert.Single(provider.ResetCalls);
        Assert.Equal(1, call.CategoryId);
        Assert.False(call.StartingNextPeriod);
    }

    [Fact]
    public void ClickingResetNextPeriod_CallsTheProvider_WithStartingNextPeriodTrue()
    {
        var provider = new FakeSpendingTrackerPageProvider(MakeCarryoverData());
        Services.AddSingleton<ISpendingTrackerPageProvider>(provider);

        var cut = Render<SpendingTracker>();
        cut.Find("#reset-carryover-btn-1").Click();
        cut.Find("#reset-next-period-btn-1").Click();

        var call = Assert.Single(provider.ResetCalls);
        Assert.True(call.StartingNextPeriod);
    }

    [Fact]
    public void ClickingCancel_HidesTheResetChoices_WithoutCallingTheProvider()
    {
        var provider = new FakeSpendingTrackerPageProvider(MakeCarryoverData());
        Services.AddSingleton<ISpendingTrackerPageProvider>(provider);

        var cut = Render<SpendingTracker>();
        cut.Find("#reset-carryover-btn-1").Click();
        cut.Find("#cancel-reset-btn-1").Click();

        Assert.Empty(cut.FindAll("#reset-options-1"));
        Assert.NotEmpty(cut.FindAll("#reset-carryover-btn-1"));
        Assert.Empty(provider.ResetCalls);
    }

    [Fact]
    public void TotalsRow_SumsTheRollingBalance_NotPlainRemaining_ForCarryoverTrackedCategories()
    {
        // Groceries: rolling balance 30 (not plain Remaining -20) - the total should reflect
        // what's actually displayed in the column above it.
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeCarryoverData()));

        var cut = Render<SpendingTracker>();

        var totalsRow = cut.Find("#week-totals-row");
        Assert.Contains("30.00", totalsRow.TextContent);
    }

    [Fact]
    public void CategoryDetails_AreNotShown_UntilTheCategoryNameIsClicked()
    {
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeData()));

        var cut = Render<SpendingTracker>();

        Assert.Empty(cut.FindAll("#category-details-week-1"));
    }

    [Fact]
    public void ClickingACategoryName_ShowsItsContributingTransactions()
    {
        var provider = new FakeSpendingTrackerPageProvider(MakeData())
        {
            TransactionsByCategory = new()
            {
                [1] =
                [
                    new CategoryTransactionLine { Date = new DateOnly(2026, 7, 14), Description = "INGLES", Amount = 120m }
                ]
            }
        };
        Services.AddSingleton<ISpendingTrackerPageProvider>(provider);

        var cut = Render<SpendingTracker>();
        cut.Find("#category-link-week-1").Click();

        var details = cut.Find("#category-details-week-1");
        Assert.Contains("INGLES", details.TextContent);
        Assert.Contains("120.00", details.TextContent);
        Assert.Contains("07/14/2026", details.TextContent);
    }

    [Fact]
    public void ClickingACategoryName_RequestsTransactions_ScopedToThatCategoryAndPeriod()
    {
        var provider = new FakeSpendingTrackerPageProvider(MakeData());
        Services.AddSingleton<ISpendingTrackerPageProvider>(provider);

        var cut = Render<SpendingTracker>();
        cut.Find("#category-link-week-1").Click();

        var request = Assert.Single(provider.TransactionRequests);
        Assert.Equal(1, request.CategoryId);
        Assert.Equal(new DateOnly(2026, 7, 12), request.PeriodStart);
        Assert.Equal(new DateOnly(2026, 7, 18), request.PeriodEnd);
    }

    [Fact]
    public void ClickingACategoryNameTwice_HidesItsDetailsAgain()
    {
        var provider = new FakeSpendingTrackerPageProvider(MakeData());
        Services.AddSingleton<ISpendingTrackerPageProvider>(provider);

        var cut = Render<SpendingTracker>();
        cut.Find("#category-link-week-1").Click();
        cut.Find("#category-link-week-1").Click();

        Assert.Empty(cut.FindAll("#category-details-week-1"));
    }

    [Fact]
    public void ClickingACategoryNameASecondTime_DoesNotReFetchTransactions()
    {
        var provider = new FakeSpendingTrackerPageProvider(MakeData());
        Services.AddSingleton<ISpendingTrackerPageProvider>(provider);

        var cut = Render<SpendingTracker>();
        cut.Find("#category-link-week-1").Click();
        cut.Find("#category-link-week-1").Click(); // collapse
        cut.Find("#category-link-week-1").Click(); // expand again

        Assert.Single(provider.TransactionRequests);
    }

    [Fact]
    public void NoTransactionsForTheCategory_ShowsAnExplicitEmptyMessage()
    {
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeData()));

        var cut = Render<SpendingTracker>();
        cut.Find("#category-link-week-1").Click();

        var details = cut.Find("#category-details-week-1");
        Assert.Contains("No transactions found", details.TextContent);
    }

    [Fact]
    public void ExpandingACategory_OnOneTable_DoesNotAffectTheSameCategoryOnTheOtherTable()
    {
        // Groceries (CategoryId 1) appears on both the week and month tables in MakeData().
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeData()));

        var cut = Render<SpendingTracker>();
        cut.Find("#category-link-week-1").Click();

        Assert.NotEmpty(cut.FindAll("#category-details-week-1"));
        Assert.Empty(cut.FindAll("#category-details-month-1"));
    }
}
