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
        PendingTransactions =
        [
            new BankTransaction { Id = 10, AccountId = 1, TransactionDate = new DateOnly(2026, 7, 1), Description = "TRADER JOE S", Amount = -40m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow }
        ],
        PendingAmazonItems =
        [
            new AmazonOrderItem { Id = 20, OrderId = "1", OrderDate = new DateOnly(2026, 7, 2), ItemTitle = "Qunol Ultra CoQ10", Price = 30m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow }
        ]
    };

    [Fact]
    public void ReviewQueue_RendersPendingTransactionsAndAmazonItems()
    {
        var provider = new FakeReviewQueueProvider(MakeData());
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();

        Assert.Contains("TRADER JOE S", cut.Markup);
        Assert.Contains("Qunol Ultra CoQ10", cut.Markup);
    }

    [Fact]
    public void CategorizeTransaction_SelectingCategoryAndPatternThenClicking_CallsProviderWithBothValues()
    {
        var provider = new FakeReviewQueueProvider(MakeData());
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();

        cut.Find("#txn-category-10").Change("1");
        cut.Find("#txn-pattern-10").Change("%TRADER JOE%");
        cut.Find("#txn-categorize-10").Click();

        Assert.Equal(10, provider.LastTransactionId);
        Assert.Equal(1, provider.LastCategoryId);
        Assert.Equal("%TRADER JOE%", provider.LastPattern);
    }

    [Fact]
    public void CategorizeAmazonItem_SelectingCategoryOnlyThenClicking_CallsProviderWithNullPattern()
    {
        var provider = new FakeReviewQueueProvider(MakeData());
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();

        cut.Find("#item-category-20").Change("1");
        cut.Find("#item-categorize-20").Click();

        Assert.Equal(20, provider.LastAmazonItemId);
        Assert.Equal(1, provider.LastCategoryId);
        Assert.Null(provider.LastPattern);
    }
}
