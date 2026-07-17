using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class ReviewQueueTests : BunitContext
{
    // Stateful fake: CategorizeTransactionAsync/CategorizeAmazonItemAsync actually
    // remove the resolved group from the data, mirroring what the real backend does -
    // needed to reproduce the "stale dropdown state leaks onto a different remaining
    // row" bug, which only shows up when the list actually shrinks between renders.
    private class FakeReviewQueueProvider : IReviewQueueProvider
    {
        public List<PendingTransactionGroup> TransactionGroups { get; set; } = [];
        public List<PendingAmazonItemGroup> AmazonItemGroups { get; set; } = [];
        public List<Category> Categories { get; set; } = [];

        public int? LastTransactionId { get; private set; }
        public int? LastAmazonItemId { get; private set; }
        public int? LastCategoryId { get; private set; }
        public string? LastPattern { get; private set; }
        public int ReapplyRulesCallCount { get; private set; }
        public ReapplyRulesResult NextReapplyResult { get; set; } = new();

        public Task<ReviewQueueData> GetReviewQueueAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReviewQueueData { TransactionGroups = TransactionGroups, AmazonItemGroups = AmazonItemGroups, Categories = Categories });

        public Task<int> CategorizeTransactionAsync(int transactionId, int categoryId, string? merchantPatternToCreate, CancellationToken cancellationToken = default)
        {
            LastTransactionId = transactionId;
            LastCategoryId = categoryId;
            LastPattern = merchantPatternToCreate;
            TransactionGroups = TransactionGroups.Where(g => !g.TransactionIds.Contains(transactionId)).ToList();
            return Task.FromResult(0);
        }

        public Task<int> CategorizeAmazonItemAsync(int itemId, int categoryId, string? productPatternToCreate, CancellationToken cancellationToken = default)
        {
            LastAmazonItemId = itemId;
            LastCategoryId = categoryId;
            LastPattern = productPatternToCreate;
            AmazonItemGroups = AmazonItemGroups.Where(g => !g.ItemIds.Contains(itemId)).ToList();
            return Task.FromResult(0);
        }

        public Task<ReapplyRulesResult> ReapplyRulesAsync(CancellationToken cancellationToken = default)
        {
            ReapplyRulesCallCount++;
            return Task.FromResult(NextReapplyResult);
        }

        public List<int>? LastBulkTransactionIds { get; private set; }
        public List<int>? LastBulkItemIds { get; private set; }
        public int? LastBulkCategoryId { get; private set; }

        public Task<int> BulkCategorizeTransactionsAsync(IReadOnlyList<int> transactionIds, int categoryId, CancellationToken cancellationToken = default)
        {
            LastBulkTransactionIds = transactionIds.ToList();
            LastBulkCategoryId = categoryId;
            TransactionGroups = TransactionGroups.Where(g => !g.TransactionIds.Any(transactionIds.Contains)).ToList();
            return Task.FromResult(transactionIds.Count);
        }

        public Task<int> BulkCategorizeAmazonItemsAsync(IReadOnlyList<int> itemIds, int categoryId, CancellationToken cancellationToken = default)
        {
            LastBulkItemIds = itemIds.ToList();
            LastBulkCategoryId = categoryId;
            AmazonItemGroups = AmazonItemGroups.Where(g => !g.ItemIds.Any(itemIds.Contains)).ToList();
            return Task.FromResult(itemIds.Count);
        }
    }

    private static FakeReviewQueueProvider MakeProvider() => new()
    {
        Categories = [new Category { Id = 1, Name = "Groceries" }, new Category { Id = 2, Name = "Restaurants" }],
        TransactionGroups =
        [
            new PendingTransactionGroup
            {
                SuggestedPattern = "PUBLIX", SampleDescription = "PUBLIX NORCROSS GA", SampleDate = new DateOnly(2026, 7, 13),
                TransactionIds = [10, 11, 12], TotalAmount = -62m, AccountName = "Wells Fargo Checking"
            },
            new PendingTransactionGroup
            {
                SuggestedPattern = "KROGER", SampleDescription = "KROGER ALPHARETTA GA", SampleDate = new DateOnly(2026, 7, 12),
                TransactionIds = [30], TotalAmount = -25m, AccountName = "Wells Fargo Checking"
            },
            new PendingTransactionGroup
            {
                SuggestedPattern = "TRADER JOE S", SampleDescription = "TRADER JOE S #123", SampleDate = new DateOnly(2026, 7, 11),
                TransactionIds = [40, 41], TotalAmount = -35m, AccountName = "Amex"
            }
        ],
        AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "Qunol Ultra CoQ10", ItemTitle = "Qunol Ultra CoQ10", SampleDate = new DateOnly(2026, 7, 10),
                ItemIds = [20, 21], TotalPrice = 62m
            },
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "Fish Oil", ItemTitle = "Fish Oil", SampleDate = new DateOnly(2026, 7, 9),
                ItemIds = [22], TotalPrice = 18m
            }
        ]
    };

    [Fact]
    public void ReviewQueue_RendersGroupedRowsWithCountsAndPrefilledPattern()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();

        Assert.Contains("PUBLIX NORCROSS GA", cut.Markup);
        Assert.Contains("3", cut.Markup); // group count
        Assert.Contains("Qunol Ultra CoQ10", cut.Markup);
        Assert.Equal("PUBLIX", cut.Find("#txn-pattern-10").GetAttribute("value"));
        Assert.Equal("Qunol Ultra CoQ10", cut.Find("#item-pattern-20").GetAttribute("value"));
        Assert.Contains("07/13/2026", cut.Markup);
        Assert.Contains("07/10/2026", cut.Markup);
        Assert.Contains("Wells Fargo Checking", cut.Markup);
        Assert.Contains("Amex", cut.Markup);
    }

    [Fact]
    public void SelectingCategoryOnTransactionGroup_ImmediatelyCategorizesUsingTheDefaultPattern_NoExtraClick()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#txn-category-10").Change("1");

        Assert.Equal(10, provider.LastTransactionId); // the group's first (representative) transaction id
        Assert.Equal(1, provider.LastCategoryId);
        Assert.Equal("PUBLIX", provider.LastPattern);
    }

    [Fact]
    public void EditingPatternBeforeSelectingCategory_UsesTheEditedPatternInstead()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#txn-pattern-10").Change("PUBLIX SUPER MARKET");
        cut.Find("#txn-category-10").Change("1");

        Assert.Equal("PUBLIX SUPER MARKET", provider.LastPattern);
    }

    [Fact]
    public void SelectingCategoryOnAmazonItemGroup_ImmediatelyCategorizesUsingTheDefaultPattern()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#item-category-20").Change("1");

        Assert.Equal(20, provider.LastAmazonItemId); // the group's first (representative) item id
        Assert.Equal(1, provider.LastCategoryId);
        Assert.Equal("Qunol Ultra CoQ10", provider.LastPattern);
    }

    [Fact]
    public void ClickingReapplyRulesButton_CallsTheProviderAndShowsHowManyWereRecategorized()
    {
        var provider = MakeProvider();
        provider.NextReapplyResult = new ReapplyRulesResult { TransactionsUpdated = 2, ItemsUpdated = 1 };
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#reapply-rules-btn").Click();

        Assert.Equal(1, provider.ReapplyRulesCallCount);
        Assert.Contains("Re-categorized 3 previously pending row(s)", cut.Markup);
    }

    [Fact]
    public void ClickingReapplyRulesButton_WhenNothingMatched_SaysSo()
    {
        var provider = MakeProvider();
        provider.NextReapplyResult = new ReapplyRulesResult { TransactionsUpdated = 0, ItemsUpdated = 0 };
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#reapply-rules-btn").Click();

        Assert.Contains("Nothing else matched the current rules", cut.Markup);
    }

    [Fact]
    public void CheckingATransactionRow_ShowsOneSelected()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#txn-select-10").Click();

        Assert.Contains("1 selected", cut.Find("#txn-selected-count").TextContent);
    }

    [Fact]
    public void CheckingTwoTransactionRowsIndividually_ShowsTwoSelected()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#txn-select-10").Click();
        cut.Find("#txn-select-40").Click();

        Assert.Contains("2 selected", cut.Find("#txn-selected-count").TextContent);
    }

    [Fact]
    public void ClickingASelectedRowAgain_Deselects()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#txn-select-10").Click();
        cut.Find("#txn-select-10").Click();

        Assert.Contains("0 selected", cut.Find("#txn-selected-count").TextContent);
    }

    [Fact]
    public void SelectAllCheckbox_SelectsEveryVisibleTransactionGroup()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#txn-select-all").Click();

        Assert.Contains("3 selected", cut.Find("#txn-selected-count").TextContent); // Publix, Kroger, Trader Joe's
    }

    [Fact]
    public void SelectAllCheckbox_ClickedAgain_DeselectsEverything()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#txn-select-all").Click();
        cut.Find("#txn-select-all").Click();

        Assert.Contains("0 selected", cut.Find("#txn-selected-count").TextContent);
    }

    [Fact]
    public void ShiftClickingARow_SelectsTheRangeFromTheLastClickedRow()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#txn-select-10").Click(); // row 0 (Publix)
        cut.Find("#txn-select-40").Click(new Microsoft.AspNetCore.Components.Web.MouseEventArgs { ShiftKey = true }); // row 2 (Trader Joe's) - should select the range 0..2

        Assert.Contains("3 selected", cut.Find("#txn-selected-count").TextContent);
    }

    [Fact]
    public void ApplyingABulkCategory_CategorizesTheUnionOfAllSelectedGroupsTransactionIds_ThenClearsSelection()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#txn-select-10").Click(); // Publix: 10, 11, 12
        cut.Find("#txn-select-30").Click(); // Kroger: 30

        cut.Find("#txn-bulk-category").Change("1");
        cut.Find("#txn-apply-bulk-category-btn").Click();

        Assert.NotNull(provider.LastBulkTransactionIds);
        Assert.Equal([10, 11, 12, 30], provider.LastBulkTransactionIds!.OrderBy(id => id));
        Assert.Equal(1, provider.LastBulkCategoryId);
        Assert.Contains("0 selected", cut.Find("#txn-selected-count").TextContent);
    }

    [Fact]
    public void ApplyingTheSameBulkCategoryToASecondBatch_WorksWithoutReselectingTheDropdown()
    {
        // Real bug report: after applying "Supplements" once, selecting more items and
        // wanting to apply "Supplements" again did nothing, because a plain <select> only
        // fires onchange when its value actually changes. The Apply button - and keeping
        // the dropdown's chosen value after applying - fixes this: the category stays
        // selected, and clicking Apply again for a new batch of checked rows works.
        var provider = MakeProvider();
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#txn-select-10").Click(); // Publix
        cut.Find("#txn-bulk-category").Change("1");
        cut.Find("#txn-apply-bulk-category-btn").Click();

        Assert.Equal([10, 11, 12], provider.LastBulkTransactionIds!.OrderBy(id => id));

        // Select a different group and click Apply again WITHOUT touching the dropdown -
        // the previously-chosen category should still be in effect.
        cut.Find("#txn-select-30").Click(); // Kroger
        cut.Find("#txn-apply-bulk-category-btn").Click();

        Assert.Equal([30], provider.LastBulkTransactionIds!.OrderBy(id => id));
        Assert.Equal(1, provider.LastBulkCategoryId);
    }

    [Fact]
    public void CheckingAnAmazonItemRow_ShowsOneSelected()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#item-select-20").Click();

        Assert.Contains("1 selected", cut.Find("#item-selected-count").TextContent);
    }

    [Fact]
    public void ApplyingABulkCategoryToAmazonItems_CategorizesTheUnionOfSelectedGroupsItemIds()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#item-select-20").Click(); // Qunol: 20, 21
        cut.Find("#item-select-22").Click(); // Fish Oil: 22

        cut.Find("#item-bulk-category").Change("1");
        cut.Find("#item-apply-bulk-category-btn").Click();

        Assert.NotNull(provider.LastBulkItemIds);
        Assert.Equal([20, 21, 22], provider.LastBulkItemIds!.OrderBy(id => id));
        Assert.Equal(1, provider.LastBulkCategoryId);
        Assert.Contains("0 selected", cut.Find("#item-selected-count").TextContent);
    }

    // Note: a real bug was found here in manual browser testing - categorizing one group
    // could make a *different*, unrelated group's dropdown visually show the same category,
    // because Blazor was reusing DOM elements by list position rather than identity when a
    // group got removed. Fixed with @key in ReviewQueue.razor. No automated test for this
    // is included: bUnit's headless rendering doesn't reproduce the underlying issue (a live
    // browser's <select> retaining its own selected-option state across a partial DOM patch)
    // - the same test passed whether @key was present or not, so it verified nothing.
}
