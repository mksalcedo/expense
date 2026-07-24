using Bunit;
using Expense.Domain.Services.Forecast;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class ForecastAccuracyTests : BunitContext
{
    private class FakeForecastAccuracyPageProvider(List<AccuracyComparison> results) : IForecastAccuracyPageProvider
    {
        public Task<List<AccuracyComparison>> GetRecentAccuracyAsync(CancellationToken cancellationToken = default) => Task.FromResult(results);
    }

    private void RegisterFakes(List<AccuracyComparison>? results = null) =>
        Services.AddSingleton<IForecastAccuracyPageProvider>(new FakeForecastAccuracyPageProvider(results ?? []));

    [Fact]
    public void RendersOneRowPerComparison_WithScheduledAndActualFigures()
    {
        RegisterFakes(
        [
            new AccuracyComparison
            {
                Name = "GPC", AccountId = 1, ScheduledDate = new DateOnly(2026, 7, 5), ScheduledAmount = 400m,
                ActualDate = new DateOnly(2026, 7, 5), ActualAmount = 612.50m
            }
        ]);

        var cut = Render<ForecastAccuracy>();

        Assert.Contains("GPC", cut.Markup);
        Assert.Contains("400.00", cut.Markup);
        Assert.Contains("612.50", cut.Markup);
        Assert.Contains("212.50", cut.Markup); // delta
    }

    [Fact]
    public void UnmatchedOccurrence_ShowsAsAMiss_NotABlankActual()
    {
        RegisterFakes(
        [
            new AccuracyComparison { Name = "Water", AccountId = 1, ScheduledDate = new DateOnly(2026, 6, 1), ScheduledAmount = 150m }
        ]);

        var cut = Render<ForecastAccuracy>();

        Assert.Contains("never posted", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LargeDelta_IsHighlighted()
    {
        RegisterFakes(
        [
            new AccuracyComparison
            {
                Name = "Amex Payment", AccountId = 1, ScheduledDate = new DateOnly(2026, 7, 15), ScheduledAmount = 2000m,
                ActualDate = new DateOnly(2026, 7, 15), ActualAmount = 2500m
            }
        ]);

        var cut = Render<ForecastAccuracy>();

        var row = cut.FindAll("tbody tr").Single(r => r.TextContent.Contains("Amex Payment"));
        Assert.Contains("background-color", row.GetAttribute("style") ?? "");
    }

    [Fact]
    public void SmallDelta_IsNotHighlighted()
    {
        RegisterFakes(
        [
            new AccuracyComparison
            {
                Name = "Discover Payment", AccountId = 1, ScheduledDate = new DateOnly(2026, 7, 3), ScheduledAmount = 173m,
                ActualDate = new DateOnly(2026, 7, 3), ActualAmount = 173m
            }
        ]);

        var cut = Render<ForecastAccuracy>();

        var row = cut.FindAll("tbody tr").Single(r => r.TextContent.Contains("Discover Payment"));
        Assert.DoesNotContain("background-color", row.GetAttribute("style") ?? "");
    }

    [Fact]
    public void NoComparisons_ShowsAFriendlyEmptyState()
    {
        RegisterFakes([]);

        var cut = Render<ForecastAccuracy>();

        Assert.Contains("Nothing to show", cut.Markup);
    }
}
