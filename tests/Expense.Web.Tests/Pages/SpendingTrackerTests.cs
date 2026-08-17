using Bunit;
using Expense.Domain.Services;
using Expense.Domain.Services.SpendingTracker;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class SpendingTrackerTests : BunitContext
{
    private readonly DataChangeNotifier _dataChangeNotifier = new();

    public SpendingTrackerTests()
    {
        Services.AddSingleton<IDataChangeNotifier>(_dataChangeNotifier);
    }

    private class FakeSpendingTrackerPageProvider(SpendingTrackerPageData data) : ISpendingTrackerPageProvider
    {
        public List<(int CategoryId, bool StartingNextPeriod)> ResetCalls { get; } = [];
        public List<(int CategoryId, DateOnly PeriodStart, DateOnly PeriodEnd)> TransactionRequests { get; } = [];
        public Dictionary<int, List<CategoryTransactionLine>> TransactionsByCategory { get; set; } = [];
        public DateOnly? LastWeekReferenceDate { get; private set; }
        public DateOnly? LastMonthReferenceDate { get; private set; }

        // Defaults to the same category list regardless of which period is requested - close
        // enough for tests that just need a stable set of categories to select/navigate
        // between. Overridable per test for the "category doesn't exist in this period"
        // edge case.
        public List<CategorySpendingSummary> WeekCategories { get; set; } = data.Week.Categories;
        public List<CategorySpendingSummary> MonthCategories { get; set; } = data.Month.Categories;

        public Task<SpendingTrackerPageData> GetSpendingTrackerAsync(CancellationToken cancellationToken = default) => Task.FromResult(data);

        // Same Sunday-start-week/calendar-month math as the real SpendingTrackerService, so
        // navigation tests can assert against real period boundaries rather than a fake stub.
        public Task<SpendingTrackerResult> GetWeekAsync(DateOnly referenceDate, CancellationToken cancellationToken = default)
        {
            LastWeekReferenceDate = referenceDate;
            var start = referenceDate.AddDays(-(int)referenceDate.DayOfWeek);
            return Task.FromResult(new SpendingTrackerResult { PeriodStart = start, PeriodEnd = start.AddDays(6), Categories = WeekCategories, PendingAmount = 0m });
        }

        public Task<SpendingTrackerResult> GetMonthAsync(DateOnly referenceDate, CancellationToken cancellationToken = default)
        {
            LastMonthReferenceDate = referenceDate;
            var start = new DateOnly(referenceDate.Year, referenceDate.Month, 1);
            return Task.FromResult(new SpendingTrackerResult { PeriodStart = start, PeriodEnd = start.AddMonths(1).AddDays(-1), Categories = MonthCategories, PendingAmount = 0m });
        }

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
    public void SpendingTracker_RightAlignsTheNumericColumns()
    {
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeData()));

        var cut = Render<SpendingTracker>();

        // "Carried Over"/"Net Remaining" render as two-line headers (a <br /> between the
        // words), so their TextContent has no space between the words.
        Assert.All(cut.FindAll("th"), h =>
        {
            if (h.TextContent is "Budget" or "Spent" or "Remaining" or "CarriedOver" or "NetRemaining")
            {
                Assert.Equal("text-right", h.GetAttribute("class"));
            }
        });
    }

    [Fact]
    public void SpendingTracker_RendersATotalsRow_IncludingPendingInTheSpentAndRemainingTotals()
    {
        // Week: Groceries (450 budget, 120 actual) + Restaurants (150 budget, 200 actual)
        // + 30 pending. Budget total = 600. Spent total = 120+200+30 = 350 - pending has
        // to count here, since it's real money already spent, just not yet categorized.
        // Remaining total = 600-350 = 250. Neither category is carryover-tracked, so
        // Carried Over totals 0.00 and Net Remaining totals the same 250.00 as Remaining.
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeData()));

        var cut = Render<SpendingTracker>();

        var totalsRow = cut.Find("#week-totals-row");
        Assert.Contains("600.00", totalsRow.TextContent);
        Assert.Contains("350.00", totalsRow.TextContent);
        Assert.Contains("250.00", totalsRow.TextContent);
        Assert.Contains("0.00", totalsRow.TextContent);
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
    public void CarryoverTrackedCategory_ShowsThePlainRemaining_AndTheRollingBalance_AsSeparateColumns()
    {
        // Groceries: Budget 450, Actual 470. Plain Remaining (Budget - Actual, no carryover)
        // is -20.00; Net Remaining is the rolling balance 30.00 (450 - 470 + 50 carried in).
        // Both must be visible at once, in their own columns - not one replacing the other.
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeCarryoverData()));

        var cut = Render<SpendingTracker>();

        Assert.Contains("-20.00", cut.Markup);
        Assert.Contains("30.00", cut.Markup);
    }

    [Fact]
    public void CarryoverTrackedCategory_ShowsTheCarriedInAmount_InItsOwnColumn()
    {
        // The cap itself (e.g. "1x period budget") is already visible/editable on the
        // Categories page - Spending Tracker doesn't also need to spell out its computed
        // dollar figure here, especially since it showed unconditionally regardless of
        // whether the cap was actually limiting anything.
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeCarryoverData()));

        var cut = Render<SpendingTracker>();

        Assert.Equal("+50.00", cut.Find("#carried-in-1").TextContent);
        Assert.Empty(cut.FindAll("[id^='carryover-cap-']"));
    }

    [Fact]
    public void NonCarryoverCategory_LeavesCarriedOverAndNetRemainingBlank_AndShowsNoResetButton()
    {
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeData()));

        var cut = Render<SpendingTracker>();

        Assert.Empty(cut.FindAll("[id^='carried-in-']"));
        Assert.Empty(cut.FindAll("[id^='reset-carryover-btn-']"));

        // Groceries week row: Category, Budget, Spent, Remaining, Carried Over, Net
        // Remaining, Action - the last three (Carried Over, Net Remaining) must be empty,
        // not a duplicate of Remaining or a stray 0.00.
        var groceriesRow = cut.Find("#category-link-week-1").Closest("tr")!;
        var cells = groceriesRow.QuerySelectorAll("td");
        Assert.Equal("", cells[4].TextContent.Trim());
        Assert.Equal("", cells[5].TextContent.Trim());
    }

    // Regression guard: without a same-sized invisible placeholder, a category with no
    // Reset button renders a shorter row than one with a button - since Week and Month show
    // the button on different categories, their rows would drift out of vertical alignment
    // with each other going down the list.
    [Fact]
    public void NonCarryoverCategory_StillReservesSpaceForAResetButton_SoRowHeightsStayAligned()
    {
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeData()));

        var cut = Render<SpendingTracker>();

        var placeholders = cut.FindAll("button").Where(b => b.TextContent.Contains("Reset") && b.GetAttribute("style")!.Contains("hidden")).ToList();
        Assert.NotEmpty(placeholders);
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

        Assert.Empty(cut.FindAll("#category-details-week"));
    }

    [Fact]
    public void ClickingACategoryName_ShowsItsContributingTransactions_AsASeparateTableBelow()
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

        var details = cut.Find("#category-details-week");
        Assert.Equal("TABLE", details.TagName);
        Assert.Contains("INGLES", details.TextContent);
        Assert.Contains("120.00", details.TextContent);
        Assert.Contains("07/14/2026", details.TextContent);
    }

    [Fact]
    public void CategoryDetailsTable_ShowsABoldTotalRow_ReconcilingWithTheCategorysActualFigure()
    {
        // Groceries' Actual on the week table (MakeData) is 120.00 - the detail total should
        // match it exactly, so the two numbers can be visually reconciled at a glance.
        var provider = new FakeSpendingTrackerPageProvider(MakeData())
        {
            TransactionsByCategory = new()
            {
                [1] =
                [
                    new CategoryTransactionLine { Date = new DateOnly(2026, 7, 13), Description = "INGLES", Amount = 100m },
                    new CategoryTransactionLine { Date = new DateOnly(2026, 7, 14), Description = "PUBLIX", Amount = 20m }
                ]
            }
        };
        Services.AddSingleton<ISpendingTrackerPageProvider>(provider);

        var cut = Render<SpendingTracker>();
        cut.Find("#category-link-week-1").Click();

        var totalRow = cut.Find("#category-details-week-total-row");
        Assert.Contains("Total", totalRow.TextContent);
        Assert.Contains("120.00", totalRow.TextContent);
        Assert.Contains("font-weight: 600", totalRow.GetAttribute("style"));
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

        Assert.Empty(cut.FindAll("#category-details-week"));
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

        var details = cut.Find("#category-details-week");
        Assert.Contains("No transactions found", details.TextContent);
    }

    [Fact]
    public void SelectingACategory_OnOneTable_DoesNotAffectTheOtherTable()
    {
        // Groceries (CategoryId 1) appears on both the week and month tables in MakeData().
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeData()));

        var cut = Render<SpendingTracker>();
        cut.Find("#category-link-week-1").Click();

        Assert.NotEmpty(cut.FindAll("#category-details-week"));
        Assert.Empty(cut.FindAll("#category-details-month"));
    }

    // Explicit regression guard for the exact bug the user called out: selecting a second
    // category must replace the detail table shown, never stack a second one alongside it.
    [Fact]
    public void SelectingADifferentCategory_ReplacesTheDetailTable_DoesNotStack()
    {
        var provider = new FakeSpendingTrackerPageProvider(MakeData())
        {
            TransactionsByCategory = new()
            {
                [1] = [new CategoryTransactionLine { Date = new DateOnly(2026, 7, 14), Description = "INGLES", Amount = 120m }],
                [2] = [new CategoryTransactionLine { Date = new DateOnly(2026, 7, 15), Description = "OLIVE GARDEN", Amount = 45m }]
            }
        };
        Services.AddSingleton<ISpendingTrackerPageProvider>(provider);

        var cut = Render<SpendingTracker>();
        cut.Find("#category-link-week-1").Click();
        cut.Find("#category-link-week-2").Click();

        Assert.Single(cut.FindAll("#category-details-week"));
        var details = cut.Find("#category-details-week");
        Assert.Contains("OLIVE GARDEN", details.TextContent);
        Assert.DoesNotContain("INGLES", details.TextContent);
    }

    // Week navigation mirrors Dashboard.razor's own (see DashboardTests.cs) - same
    // GetWeekAsync/GetMonthAsync calculations, so the two pages can never disagree about
    // period boundaries.
    [Fact]
    public void ClickingPreviousOnWeek_FetchesTheWeekBefore()
    {
        var provider = new FakeSpendingTrackerPageProvider(MakeData());
        Services.AddSingleton<ISpendingTrackerPageProvider>(provider);

        var cut = Render<SpendingTracker>();
        cut.Find("#week-previous").Click();

        Assert.Equal(new DateOnly(2026, 7, 5), provider.LastWeekReferenceDate);
        Assert.Contains("2026-07-05", cut.Markup);
        Assert.Contains("2026-07-11", cut.Markup);
    }

    [Fact]
    public void ClickingNextOnWeek_FetchesTheWeekAfter()
    {
        var provider = new FakeSpendingTrackerPageProvider(MakeData());
        Services.AddSingleton<ISpendingTrackerPageProvider>(provider);

        var cut = Render<SpendingTracker>();
        cut.Find("#week-next").Click();

        Assert.Equal(new DateOnly(2026, 7, 19), provider.LastWeekReferenceDate);
        Assert.Contains("2026-07-19", cut.Markup);
        Assert.Contains("2026-07-25", cut.Markup);
    }

    [Fact]
    public void ClickingPreviousTwiceThenCurrentOnWeek_ReturnsToTodaysRealWeek()
    {
        var provider = new FakeSpendingTrackerPageProvider(MakeData());
        Services.AddSingleton<ISpendingTrackerPageProvider>(provider);

        var cut = Render<SpendingTracker>();
        cut.Find("#week-previous").Click();
        cut.Find("#week-previous").Click();
        cut.Find("#week-current").Click();

        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), provider.LastWeekReferenceDate);
    }

    [Fact]
    public void ClickingPreviousOnMonth_FetchesTheMonthBefore()
    {
        var provider = new FakeSpendingTrackerPageProvider(MakeData());
        Services.AddSingleton<ISpendingTrackerPageProvider>(provider);

        var cut = Render<SpendingTracker>();
        cut.Find("#month-previous").Click();

        Assert.Equal(new DateOnly(2026, 6, 1), provider.LastMonthReferenceDate);
        Assert.Contains("2026-06-01", cut.Markup);
        Assert.Contains("2026-06-30", cut.Markup);
    }

    [Fact]
    public void ClickingNextOnMonth_FetchesTheMonthAfter()
    {
        var provider = new FakeSpendingTrackerPageProvider(MakeData());
        Services.AddSingleton<ISpendingTrackerPageProvider>(provider);

        var cut = Render<SpendingTracker>();
        cut.Find("#month-next").Click();

        Assert.Contains("2026-08-01", cut.Markup);
        Assert.Contains("2026-08-31", cut.Markup);
    }

    [Fact]
    public void NavigatingTheWeekTable_DoesNotAffectTheMonthTable()
    {
        var provider = new FakeSpendingTrackerPageProvider(MakeData());
        Services.AddSingleton<ISpendingTrackerPageProvider>(provider);

        var cut = Render<SpendingTracker>();
        cut.Find("#week-next").Click();

        Assert.Null(provider.LastMonthReferenceDate);
        Assert.Contains("2026-07-01", cut.Markup); // Month table unchanged
    }

    [Fact]
    public void NavigatingWithACategorySelected_KeepsItSelected_AndFetchesTheNewPeriodsTransactions()
    {
        var provider = new FakeSpendingTrackerPageProvider(MakeData())
        {
            TransactionsByCategory = new() { [1] = [new CategoryTransactionLine { Date = new DateOnly(2026, 7, 14), Description = "INGLES", Amount = 120m }] }
        };
        Services.AddSingleton<ISpendingTrackerPageProvider>(provider);

        var cut = Render<SpendingTracker>();
        cut.Find("#category-link-week-1").Click();
        cut.Find("#week-next").Click();

        Assert.NotEmpty(cut.FindAll("#category-details-week"));
        var requestsForCategory1 = provider.TransactionRequests.Where(r => r.CategoryId == 1).ToList();
        Assert.Equal(2, requestsForCategory1.Count); // once for the original period, once for the navigated one
        Assert.NotEqual(requestsForCategory1[0].PeriodStart, requestsForCategory1[1].PeriodStart);
    }

    [Fact]
    public void NavigatingBackToAPreviouslyViewedPeriod_ReusesTheCachedTransactions_DoesNotRefetch()
    {
        var provider = new FakeSpendingTrackerPageProvider(MakeData())
        {
            TransactionsByCategory = new() { [1] = [new CategoryTransactionLine { Date = new DateOnly(2026, 7, 14), Description = "INGLES", Amount = 120m }] }
        };
        Services.AddSingleton<ISpendingTrackerPageProvider>(provider);

        var cut = Render<SpendingTracker>();
        cut.Find("#category-link-week-1").Click(); // fetch for the original period
        cut.Find("#week-next").Click();             // fetch for the next period
        cut.Find("#week-previous").Click();          // back to the original period - already cached

        Assert.Equal(2, provider.TransactionRequests.Count(r => r.CategoryId == 1));
    }

    // If the selected category isn't budgeted (or no longer exists) in the newly-navigated
    // period, there's nothing to show it against - falls back to clearing the selection
    // rather than pointing the detail table at a category that isn't even on screen.
    [Fact]
    public void NavigatingToAPeriodWithoutTheSelectedCategory_ClearsTheSelection()
    {
        var provider = new FakeSpendingTrackerPageProvider(MakeData());
        Services.AddSingleton<ISpendingTrackerPageProvider>(provider);

        var cut = Render<SpendingTracker>();
        cut.Find("#category-link-week-1").Click();
        Assert.NotEmpty(cut.FindAll("#category-details-week"));

        provider.WeekCategories = []; // the navigated-to period has no categories at all
        cut.Find("#week-next").Click();

        Assert.Empty(cut.FindAll("#category-details-week"));
    }

    // Real gap this guards (2026-08-17): a background scheduled sync completing while the
    // user just sits on this page never used to be reflected without a manual refresh -
    // and naively refetching "current" data on that signal would have silently reset any
    // Previous/Next navigation back to the current week/month, which would have been its
    // own new bug.
    [Fact]
    public void DataChangeNotifier_Firing_RefreshesBothTables_WithoutResettingNavigation()
    {
        var provider = new FakeSpendingTrackerPageProvider(MakeData());
        Services.AddSingleton<ISpendingTrackerPageProvider>(provider);

        var cut = Render<SpendingTracker>();
        cut.Find("#week-next").Click(); // navigate off the current week first
        Assert.Contains("2026-07-19", cut.Markup);

        // Simulates a background sync changing Groceries' actual spend - nothing on this
        // page did anything to cause it.
        provider.WeekCategories = [new CategorySpendingSummary { CategoryId = 1, CategoryName = "Groceries", Budget = 450m, Actual = 999m }];
        _dataChangeNotifier.NotifyChanged();

        cut.WaitForAssertion(() => Assert.Contains("999.00", cut.Markup));
        // Still on the navigated-to week, not silently reset back to the current one.
        Assert.Contains("2026-07-19", cut.Markup);
    }
}
