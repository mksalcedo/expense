using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class ReviewQueueTests : BunitContext
{
    private class FakeReviewQueueProvider(ReviewQueueData data) : IReviewQueueProvider
    {
        public int? LastTransactionId { get; private set; }
        public int? LastAmazonItemId { get; private set; }
        public int? LastCategoryId { get; private set; }
        public string? LastPattern { get; private set; }

        public Task<ReviewQueueData> GetReviewQueueAsync(CancellationToken cancellationToken = default) => Task.FromResult(data);

        public Task<int> CategorizeTransactionAsync(int transactionId, int categoryId, string? merchantPatternToCreate, CancellationToken cancellationToken = default)
        {
            LastTransactionId = transactionId;
            LastCategoryId = categoryId;
            LastPattern = merchantPatternToCreate;
            return Task.FromResult(0);
        }

        public Task<int> CategorizeAmazonItemAsync(int itemId, int categoryId, string? productPatternToCreate, CancellationToken cancellationToken = default)
        {
            LastAmazonItemId = itemId;
            LastCategoryId = categoryId;
            LastPattern = productPatternToCreate;
            return Task.FromResult(0);
        }
    }

    private static ReviewQueueData MakeData() => new()
    {
        Categories = [new Category { Id = 1, Name = "Groceries", IsBudgeted = true }],
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
        var provider = new FakeReviewQueueProvider(MakeData());
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
        var provider = new FakeReviewQueueProvider(MakeData());
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
        var provider = new FakeReviewQueueProvider(MakeData());
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#txn-pattern-10").Change("PUBLIX SUPER MARKET");
        cut.Find("#txn-category-10").Change("1");

        Assert.Equal("PUBLIX SUPER MARKET", provider.LastPattern);
    }

    [Fact]
    public void SelectingCategoryOnAmazonItemGroup_ImmediatelyCategorizesUsingTheDefaultPattern()
    {
        var provider = new FakeReviewQueueProvider(MakeData());
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#item-category-20").Change("1");

        Assert.Equal(20, provider.LastAmazonItemId); // the group's first (representative) item id
        Assert.Equal(1, provider.LastCategoryId);
        Assert.Equal("Qunol Ultra CoQ10", provider.LastPattern);
    }
}
