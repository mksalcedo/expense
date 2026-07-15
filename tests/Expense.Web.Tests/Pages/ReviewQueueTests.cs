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
    }

    private static FakeReviewQueueProvider MakeProvider() => new()
    {
        Categories = [new Category { Id = 1, Name = "Groceries" }, new Category { Id = 2, Name = "Restaurants" }],
        TransactionGroups =
        [
            new PendingTransactionGroup
            {
                SuggestedPattern = "PUBLIX", SampleDescription = "PUBLIX NORCROSS GA",
                TransactionIds = [10, 11, 12], TotalAmount = -62m
            }
        ],
        AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "Qunol Ultra CoQ10", ItemTitle = "Qunol Ultra CoQ10",
                ItemIds = [20, 21], TotalPrice = 62m
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

    // Note: a real bug was found here in manual browser testing - categorizing one group
    // could make a *different*, unrelated group's dropdown visually show the same category,
    // because Blazor was reusing DOM elements by list position rather than identity when a
    // group got removed. Fixed with @key in ReviewQueue.razor. No automated test for this
    // is included: bUnit's headless rendering doesn't reproduce the underlying issue (a live
    // browser's <select> retaining its own selected-option state across a partial DOM patch)
    // - the same test passed whether @key was present or not, so it verified nothing.
}
