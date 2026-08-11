using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Web.Components.Pages;
using Expense.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Expense.Web.Tests.Pages;

public class ReviewQueueTests : BunitContext
{
    public ReviewQueueTests()
    {
        // The real (dependency-free) implementation is fine here - tests that care about
        // NavMenu's badge updating live register their own shared instance instead (see
        // NavMenuTests.cs), which overrides this one (last registration wins).
        Services.AddSingleton<IReviewQueueChangeNotifier>(new ReviewQueueChangeNotifier());

        // Every render now imports screenshotPaste.js for the "Paste screenshot" feature -
        // Loose mode lets tests that don't care about it render without configuring the
        // module explicitly, matching Forecast/Dashboard's own localStorage-interop tests.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

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

        public List<int>? LastDismissedTransactionIds { get; private set; }
        public List<int>? LastDismissedItemIds { get; private set; }

        public Task DismissTransactionsAsync(IReadOnlyList<int> transactionIds, CancellationToken cancellationToken = default)
        {
            LastDismissedTransactionIds = transactionIds.ToList();
            TransactionGroups = TransactionGroups.Where(g => !g.TransactionIds.Any(transactionIds.Contains)).ToList();
            return Task.CompletedTask;
        }

        public Task DismissAmazonItemsAsync(IReadOnlyList<int> itemIds, CancellationToken cancellationToken = default)
        {
            LastDismissedItemIds = itemIds.ToList();
            AmazonItemGroups = AmazonItemGroups.Where(g => !g.ItemIds.Any(itemIds.Contains)).ToList();
            return Task.CompletedTask;
        }

        public int? LastUpdatedItemId { get; private set; }
        public string? LastUpdatedTitle { get; private set; }
        public decimal? LastUpdatedPrice { get; private set; }
        public int? LastUpdatedQuantity { get; private set; }

        public Task UpdateAmazonItemDetailsAsync(int itemId, string itemTitle, decimal price, int quantity, CancellationToken cancellationToken = default)
        {
            LastUpdatedItemId = itemId;
            LastUpdatedTitle = itemTitle;
            LastUpdatedPrice = price;
            LastUpdatedQuantity = quantity;
            return Task.CompletedTask;
        }

        public string? LastAddedOrderId { get; private set; }
        public DateOnly? LastAddedOrderDate { get; private set; }
        public string? LastAddedTitle { get; private set; }
        public decimal? LastAddedPrice { get; private set; }
        public int? LastAddedQuantity { get; private set; }
        public int AddManualAmazonItemCallCount { get; private set; }
        public List<(string OrderId, DateOnly OrderDate, string Title, decimal Price, int Quantity)> AddedItems { get; } = [];

        public Task AddManualAmazonItemAsync(string orderId, DateOnly orderDate, string itemTitle, decimal price, int quantity, CancellationToken cancellationToken = default)
        {
            AddManualAmazonItemCallCount++;
            LastAddedOrderId = orderId;
            LastAddedOrderDate = orderDate;
            LastAddedTitle = itemTitle;
            LastAddedPrice = price;
            LastAddedQuantity = quantity;
            AddedItems.Add((orderId, orderDate, itemTitle, price, quantity));
            return Task.CompletedTask;
        }

        public List<string> NextParsedTitles { get; set; } = [];
        public byte[]? LastParsedImageBytes { get; private set; }
        public string? LastParsedMediaType { get; private set; }
        public int ParseAmazonItemScreenshotCallCount { get; private set; }

        public Task<List<string>> ParseAmazonItemScreenshotAsync(byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default)
        {
            ParseAmazonItemScreenshotCallCount++;
            LastParsedImageBytes = imageBytes;
            LastParsedMediaType = mediaType;
            return Task.FromResult(NextParsedTitles);
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
    public void SelectingCategoryOnTransactionGroup_WithASingleWordPattern_AsksForConfirmationFirst()
    {
        // PUBLIX (the fixture's default pattern for this group) is a single word - must be
        // confirmed before it's actually saved as a new merchant rule.
        var provider = MakeProvider();
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#txn-category-10").Change("1");

        Assert.Null(provider.LastTransactionId); // not yet committed
        Assert.Contains("PUBLIX", cut.Find("#confirm-single-word-modal").TextContent);

        cut.Find("#confirm-single-word-btn").Click();

        Assert.Equal(10, provider.LastTransactionId); // the group's first (representative) transaction id
        Assert.Equal(1, provider.LastCategoryId);
        Assert.Equal("PUBLIX", provider.LastPattern);
        Assert.Empty(cut.FindAll("#confirm-single-word-modal"));
    }

    [Fact]
    public void CancelingTheSingleWordConfirmation_DoesNotCategorize()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#txn-category-10").Change("1");
        cut.Find("#cancel-single-word-btn").Click();

        Assert.Null(provider.LastTransactionId);
        Assert.Empty(cut.FindAll("#confirm-single-word-modal"));
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
    public void SelectingCategoryOnAmazonItemGroup_WithASingleWordPattern_AlsoAsksForConfirmation()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#item-pattern-20").Change("Turmeric");
        cut.Find("#item-category-20").Change("1");

        Assert.Null(provider.LastAmazonItemId);
        Assert.Contains("Turmeric", cut.Find("#confirm-single-word-modal").TextContent);

        cut.Find("#confirm-single-word-btn").Click();

        Assert.Equal(20, provider.LastAmazonItemId);
        Assert.Equal("Turmeric", provider.LastPattern);
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

    [Fact]
    public void SelectedTransactionGroups_CanBeDismissed_WithoutChoosingACategory()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#txn-select-30").Click(); // KROGER: 30
        var dismissBtn = cut.Find("#txn-dismiss-btn");
        Assert.False(dismissBtn.HasAttribute("disabled")); // no category needed to dismiss
        dismissBtn.Click();

        Assert.Equal([30], provider.LastDismissedTransactionIds);
        Assert.DoesNotContain("KROGER ALPHARETTA GA", cut.Markup);
        Assert.Contains("0 selected", cut.Find("#txn-selected-count").TextContent);
    }

    [Fact]
    public void SelectedAmazonItemGroups_CanBeDismissed_WithoutChoosingACategory()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();
        cut.Find("#item-select-22").Click(); // Fish Oil: 22
        cut.Find("#item-dismiss-btn").Click();

        Assert.Equal([22], provider.LastDismissedItemIds);
        Assert.DoesNotContain("Fish Oil", cut.Markup);
    }

    [Fact]
    public void BankTransactionGroups_HaveAViewIndividualItemsLink_ToTheTransactionsPage()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();

        var link = cut.Find("#txn-view-individual-10");
        Assert.Equal("/transactions?search=PUBLIX", link.GetAttribute("href"));
    }

    [Fact]
    public void NeedsReviewAmazonItems_ShowIndividually_WithOrderIdAndHighlight_NotGroupedTogether()
    {
        var provider = MakeProvider();
        provider.AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "(Item details unavailable in email - check Amazon order page)",
                ItemTitle = "(Item details unavailable in email - check Amazon order page)",
                SampleDate = new DateOnly(2025, 7, 17), ItemIds = [315], TotalPrice = 22.00m,
                NeedsReview = true, OrderId = "113-1132648-3403446"
            },
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "(Item details unavailable in email - check Amazon order page)",
                ItemTitle = "(Item details unavailable in email - check Amazon order page)",
                SampleDate = new DateOnly(2025, 6, 16), ItemIds = [316], TotalPrice = 25.78m,
                NeedsReview = true, OrderId = "112-9103180-2234648"
            }
        ];
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();

        Assert.Contains("113-1132648-3403446", cut.Markup);
        Assert.Contains("112-9103180-2234648", cut.Markup);
        Assert.Contains("22.00", cut.Markup);
        Assert.Contains("25.78", cut.Markup);
        var rows = cut.FindAll("tbody tr");
        Assert.Contains(rows, r => (r.GetAttribute("style") ?? "").Contains("background-color: yellow"));
    }

    [Fact]
    public void NeedsReviewItem_ViewOnAmazonLink_UsesTheCapturedOrderDetailsUrl()
    {
        var provider = MakeProvider();
        provider.AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "x", ItemTitle = "x", SampleDate = new DateOnly(2026, 7, 22), ItemIds = [400], TotalPrice = 51.40m,
                NeedsReview = true, OrderId = "113-4355508-6173055",
                OrderDetailsUrl = "https://www.amazon.com/gp/css/order-details?orderId=113-4355508-6173055"
            }
        ];
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();

        var link = cut.Find("#item-view-on-amazon-400");
        Assert.Equal("https://www.amazon.com/gp/css/order-details?orderId=113-4355508-6173055", link.GetAttribute("href"));
    }

    [Fact]
    public void NeedsReviewItem_ViewOnAmazonLink_FallsBackToTheGeneralOrdersPage_WhenNoUrlWasCaptured()
    {
        var provider = MakeProvider();
        provider.AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "x", ItemTitle = "x", SampleDate = new DateOnly(2026, 7, 22), ItemIds = [401], TotalPrice = 10.00m,
                NeedsReview = true, OrderId = "113-0000000-0000000", OrderDetailsUrl = null
            }
        ];
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();

        var link = cut.Find("#item-view-on-amazon-401");
        Assert.Equal("https://www.amazon.com/gp/css/order-history", link.GetAttribute("href"));
    }

    [Fact]
    public void NeedsReviewItem_InlineEditingTitleAndPrice_CallsUpdateAmazonItemDetails()
    {
        var provider = MakeProvider();
        provider.AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "x", ItemTitle = "(Item details unavailable in email - check Amazon order page)",
                SampleDate = new DateOnly(2026, 7, 22), ItemIds = [402], TotalPrice = 51.40m,
                NeedsReview = true, OrderId = "113-4355508-6173055"
            }
        ];
        Services.AddSingleton<IReviewQueueProvider>(provider);
        var cut = Render<ReviewQueue>();

        cut.Find("#item-title-402").Change("Levoit Core 300-P Air Purifier Filter");
        cut.Find("#item-price-402").Change("25.99");

        Assert.Equal(402, provider.LastUpdatedItemId);
        Assert.Equal("Levoit Core 300-P Air Purifier Filter", provider.LastUpdatedTitle);
        Assert.Equal(25.99m, provider.LastUpdatedPrice);
    }

    private class FakeJSStreamReference(byte[] bytes) : IJSStreamReference
    {
        public long Length => bytes.Length;

        public ValueTask<Stream> OpenReadStreamAsync(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(new MemoryStream(bytes));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public void NeedsReviewItem_ShowsAPasteScreenshotButton()
    {
        var provider = MakeProvider();
        provider.AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "x", ItemTitle = "(Item details unavailable in email - check Amazon order page)",
                SampleDate = new DateOnly(2026, 7, 22), ItemIds = [500], TotalPrice = 19.99m,
                NeedsReview = true, OrderId = "113-0140431-5777821"
            }
        ];
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();

        Assert.NotEmpty(cut.FindAll("#item-paste-screenshot-500"));
    }

    [Fact]
    public void ClickingPasteScreenshot_ShowsThePasteTarget()
    {
        var provider = MakeProvider();
        provider.AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "x", ItemTitle = "(Item details unavailable in email - check Amazon order page)",
                SampleDate = new DateOnly(2026, 7, 22), ItemIds = [500], TotalPrice = 19.99m,
                NeedsReview = true, OrderId = "113-0140431-5777821"
            }
        ];
        Services.AddSingleton<IReviewQueueProvider>(provider);
        var cut = Render<ReviewQueue>();

        cut.Find("#item-paste-screenshot-500").Click();

        Assert.NotEmpty(cut.FindAll("#item-paste-target-500"));
    }

    [Fact]
    public void ClickingCancelOnThePasteTarget_HidesItAgain()
    {
        var provider = MakeProvider();
        provider.AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "x", ItemTitle = "(Item details unavailable in email - check Amazon order page)",
                SampleDate = new DateOnly(2026, 7, 22), ItemIds = [500], TotalPrice = 19.99m,
                NeedsReview = true, OrderId = "113-0140431-5777821"
            }
        ];
        Services.AddSingleton<IReviewQueueProvider>(provider);
        var cut = Render<ReviewQueue>();
        cut.Find("#item-paste-screenshot-500").Click();

        cut.Find("#item-cancel-paste-500").Click();

        Assert.Empty(cut.FindAll("#item-paste-target-500"));
    }

    [Fact]
    public async Task PastingAScreenshot_AppliesTheParsedTitle_AndClosesThePasteTarget()
    {
        var provider = MakeProvider();
        provider.AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "x", ItemTitle = "(Item details unavailable in email - check Amazon order page)",
                SampleDate = new DateOnly(2026, 7, 22), ItemIds = [500], TotalPrice = 19.99m,
                NeedsReview = true, OrderId = "113-0140431-5777821"
            }
        ];
        provider.NextParsedTitles = ["THORNE Vitamin C"];
        Services.AddSingleton<IReviewQueueProvider>(provider);
        var cut = Render<ReviewQueue>();
        cut.Find("#item-paste-screenshot-500").Click();

        var pageInstance = cut.Instance;
        await cut.InvokeAsync(() => pageInstance.OnImagePasted(new FakeJSStreamReference([1, 2, 3]), "image/png"));

        Assert.Equal(500, provider.LastUpdatedItemId);
        Assert.Equal("THORNE Vitamin C", provider.LastUpdatedTitle);
        Assert.Equal("THORNE Vitamin C", cut.Find("#item-title-500").GetAttribute("value"));
        Assert.Empty(cut.FindAll("#item-paste-target-500"));
    }

    [Fact]
    public async Task PastingAScreenshot_PassesTheImageBytesAndMediaTypeToTheParser()
    {
        var provider = MakeProvider();
        provider.AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "x", ItemTitle = "x", SampleDate = new DateOnly(2026, 7, 22), ItemIds = [500], TotalPrice = 19.99m,
                NeedsReview = true, OrderId = "113-0140431-5777821"
            }
        ];
        provider.NextParsedTitles = ["THORNE Vitamin C"];
        Services.AddSingleton<IReviewQueueProvider>(provider);
        var cut = Render<ReviewQueue>();
        cut.Find("#item-paste-screenshot-500").Click();

        var pageInstance = cut.Instance;
        await cut.InvokeAsync(() => pageInstance.OnImagePasted(new FakeJSStreamReference([9, 8, 7]), "image/png"));

        Assert.Equal(1, provider.ParseAmazonItemScreenshotCallCount);
        Assert.Equal(new byte[] { 9, 8, 7 }, provider.LastParsedImageBytes);
        Assert.Equal("image/png", provider.LastParsedMediaType);
    }

    [Fact]
    public async Task PastingAScreenshot_WithNoItemsFound_ShowsAnErrorAndLeavesThePasteTargetOpen()
    {
        var provider = MakeProvider();
        provider.AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "x", ItemTitle = "x", SampleDate = new DateOnly(2026, 7, 22), ItemIds = [500], TotalPrice = 19.99m,
                NeedsReview = true, OrderId = "113-0140431-5777821"
            }
        ];
        provider.NextParsedTitles = [];
        Services.AddSingleton<IReviewQueueProvider>(provider);
        var cut = Render<ReviewQueue>();
        cut.Find("#item-paste-screenshot-500").Click();

        var pageInstance = cut.Instance;
        await cut.InvokeAsync(() => pageInstance.OnImagePasted(new FakeJSStreamReference([1, 2, 3]), "image/png"));

        Assert.Null(provider.LastUpdatedItemId);
        Assert.NotEmpty(cut.FindAll("#item-paste-target-500"));
        Assert.Contains("Couldn't find an item name", cut.Find("#item-paste-error-500").TextContent);
    }

    [Fact]
    public async Task PastingOrderData_WithASingleItem_AppliesItsExactTitlePriceAndQuantity()
    {
        var provider = MakeProvider();
        provider.AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "x", ItemTitle = "(Item details unavailable in email - check Amazon order page)",
                SampleDate = new DateOnly(2026, 7, 22), ItemIds = [500], TotalPrice = 19.99m,
                NeedsReview = true, OrderId = "113-0140431-5777821"
            }
        ];
        Services.AddSingleton<IReviewQueueProvider>(provider);
        var cut = Render<ReviewQueue>();
        cut.Find("#item-paste-screenshot-500").Click();

        var pageInstance = cut.Instance;
        const string json = """{"orderId": "113-0140431-5777821", "items": [{"title": "THORNE Vitamin C", "price": 24.99, "quantity": 2}]}""";
        await cut.InvokeAsync(() => pageInstance.OnOrderDataPasted(json));

        Assert.Equal(500, provider.LastUpdatedItemId);
        Assert.Equal("THORNE Vitamin C", provider.LastUpdatedTitle);
        Assert.Equal(24.99m, provider.LastUpdatedPrice);
        Assert.Equal(2, provider.LastUpdatedQuantity);
        Assert.Empty(provider.AddedItems);
        Assert.Empty(cut.FindAll("#item-paste-target-500"));
    }

    [Fact]
    public async Task PastingOrderData_WithMultipleItems_AppliesTheFirstAndAddsTheRestAsNewItems()
    {
        var provider = MakeProvider();
        provider.AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "x", ItemTitle = "x", SampleDate = new DateOnly(2026, 7, 22), ItemIds = [500], TotalPrice = 19.99m,
                NeedsReview = true, OrderId = "113-0140431-5777821"
            }
        ];
        Services.AddSingleton<IReviewQueueProvider>(provider);
        var cut = Render<ReviewQueue>();
        cut.Find("#item-paste-screenshot-500").Click();

        var pageInstance = cut.Instance;
        const string json = """
            {
              "orderId": "113-0140431-5777821",
              "items": [
                {"title": "THORNE Vitamin C", "price": 24.99, "quantity": 1},
                {"title": "NeoCell Collagen Peptides", "price": 32.50, "quantity": 2}
              ]
            }
            """;
        await cut.InvokeAsync(() => pageInstance.OnOrderDataPasted(json));

        Assert.Equal(500, provider.LastUpdatedItemId);
        Assert.Equal("THORNE Vitamin C", provider.LastUpdatedTitle);
        var added = Assert.Single(provider.AddedItems);
        Assert.Equal("113-0140431-5777821", added.OrderId);
        Assert.Equal(new DateOnly(2026, 7, 22), added.OrderDate);
        Assert.Equal("NeoCell Collagen Peptides", added.Title);
        Assert.Equal(32.50m, added.Price);
        Assert.Equal(2, added.Quantity);
    }

    [Fact]
    public async Task PastingOrderData_ThatIsNotValidJson_ShowsAnErrorAndLeavesThePasteTargetOpen()
    {
        var provider = MakeProvider();
        provider.AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "x", ItemTitle = "x", SampleDate = new DateOnly(2026, 7, 22), ItemIds = [500], TotalPrice = 19.99m,
                NeedsReview = true, OrderId = "113-0140431-5777821"
            }
        ];
        Services.AddSingleton<IReviewQueueProvider>(provider);
        var cut = Render<ReviewQueue>();
        cut.Find("#item-paste-screenshot-500").Click();

        var pageInstance = cut.Instance;
        await cut.InvokeAsync(() => pageInstance.OnOrderDataPasted("{not valid json"));

        Assert.Null(provider.LastUpdatedItemId);
        Assert.Empty(provider.AddedItems);
        Assert.NotEmpty(cut.FindAll("#item-paste-target-500"));
        Assert.Contains("Couldn't read that as order data", cut.Find("#item-paste-error-500").TextContent);
    }

    [Fact]
    public async Task PastingOrderData_ForADifferentOrder_ShowsAnErrorAndAppliesNothing()
    {
        var provider = MakeProvider();
        provider.AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "x", ItemTitle = "x", SampleDate = new DateOnly(2026, 7, 22), ItemIds = [500], TotalPrice = 19.99m,
                NeedsReview = true, OrderId = "113-0140431-5777821"
            }
        ];
        Services.AddSingleton<IReviewQueueProvider>(provider);
        var cut = Render<ReviewQueue>();
        cut.Find("#item-paste-screenshot-500").Click();

        var pageInstance = cut.Instance;
        const string json = """{"orderId": "999-9999999-9999999", "items": [{"title": "Wrong Order Item", "price": 5.00, "quantity": 1}]}""";
        await cut.InvokeAsync(() => pageInstance.OnOrderDataPasted(json));

        Assert.Null(provider.LastUpdatedItemId);
        Assert.Empty(provider.AddedItems);
        Assert.NotEmpty(cut.FindAll("#item-paste-target-500"));
        Assert.Contains("different order", cut.Find("#item-paste-error-500").TextContent);
    }

    [Fact]
    public void NeedsReviewItem_WhyFlaggedToggle_ShowsTheReasonAndRawEmailBody_WhenExpanded()
    {
        var provider = MakeProvider();
        provider.AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "x", ItemTitle = "x", SampleDate = new DateOnly(2026, 7, 22), ItemIds = [403], TotalPrice = 51.40m,
                NeedsReview = true, OrderId = "113-4355508-6173055",
                NeedsReviewReason = "No item detail in confirmation email",
                RawEmailBody = "Order #\n113-4355508-6173055\n\nGrand Total:\n51.4 USD"
            }
        ];
        Services.AddSingleton<IReviewQueueProvider>(provider);
        var cut = Render<ReviewQueue>();

        Assert.Empty(cut.FindAll("#item-review-context-403"));

        cut.Find("#item-why-flagged-403").Click();

        var context = cut.Find("#item-review-context-403");
        Assert.Contains("No item detail in confirmation email", context.TextContent);
        Assert.Contains("113-4355508-6173055", context.TextContent);
    }

    [Fact]
    public void AlreadyCorrectedSingleItemGroup_StillShowsAddAnotherItem_ButNotWhyFlagged()
    {
        // Real scenario this guards: correcting a NeedsReview placeholder's title/price
        // clears NeedsReview - "Add another item" must keep working afterward (e.g. after a
        // page refresh), even though the row is no longer highlighted/flagged.
        var provider = MakeProvider();
        provider.AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "Levoit Core 300-P Air Purifier Filter", ItemTitle = "Levoit Core 300-P Air Purifier Filter",
                SampleDate = new DateOnly(2026, 7, 22), ItemIds = [326], TotalPrice = 25.99m,
                NeedsReview = false, OrderId = "113-4355508-6173055"
            }
        ];
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();

        Assert.NotNull(cut.Find("#item-add-another-326"));
        Assert.Empty(cut.FindAll("#item-why-flagged-326"));
    }

    [Fact]
    public void NeedsReviewItem_AddAnotherItemForm_IsHiddenUntilToggled()
    {
        var provider = MakeProvider();
        provider.AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "x", ItemTitle = "x", SampleDate = new DateOnly(2026, 7, 22), ItemIds = [404], TotalPrice = 51.40m,
                NeedsReview = true, OrderId = "113-4355508-6173055"
            }
        ];
        Services.AddSingleton<IReviewQueueProvider>(provider);

        var cut = Render<ReviewQueue>();

        Assert.NotNull(cut.Find("#item-add-another-404"));
        Assert.Empty(cut.FindAll("#item-new-title-404"));
    }

    [Fact]
    public void NeedsReviewItem_AddingAnotherItem_CallsAddManualAmazonItem_WithTheOrdersIdAndDate()
    {
        var provider = MakeProvider();
        provider.AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "x", ItemTitle = "x", SampleDate = new DateOnly(2026, 7, 22), ItemIds = [405], TotalPrice = 51.40m,
                NeedsReview = true, OrderId = "113-4355508-6173055"
            }
        ];
        Services.AddSingleton<IReviewQueueProvider>(provider);
        var cut = Render<ReviewQueue>();

        cut.Find("#item-add-another-405").Click();
        cut.Find("#item-new-title-405").Change("Pure Encapsulations B12 Folate");
        cut.Find("#item-new-price-405").Change("22.50");
        cut.Find("#item-new-quantity-405").Change("1");
        cut.Find("#item-add-submit-405").Click();

        Assert.Equal(1, provider.AddManualAmazonItemCallCount);
        Assert.Equal("113-4355508-6173055", provider.LastAddedOrderId);
        Assert.Equal(new DateOnly(2026, 7, 22), provider.LastAddedOrderDate);
        Assert.Equal("Pure Encapsulations B12 Folate", provider.LastAddedTitle);
        Assert.Equal(22.50m, provider.LastAddedPrice);
        Assert.Equal(1, provider.LastAddedQuantity);
    }

    [Fact]
    public void NeedsReviewItem_AfterAddingAnotherItem_RefreshesTheQueueAndClosesTheForm()
    {
        var provider = MakeProvider();
        provider.AmazonItemGroups =
        [
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "x", ItemTitle = "x", SampleDate = new DateOnly(2026, 7, 22), ItemIds = [406], TotalPrice = 51.40m,
                NeedsReview = true, OrderId = "113-4355508-6173055"
            }
        ];
        Services.AddSingleton<IReviewQueueProvider>(provider);
        var cut = Render<ReviewQueue>();
        cut.Find("#item-add-another-406").Click();
        cut.Find("#item-new-title-406").Change("Pure Encapsulations B12 Folate");
        cut.Find("#item-new-price-406").Change("22.50");

        // Simulate the new item now showing up as its own separate group, same as the real
        // backend/GetPendingAmazonItemGroupsAsync would do after the item is added.
        provider.AmazonItemGroups =
        [
            .. provider.AmazonItemGroups,
            new PendingAmazonItemGroup
            {
                SuggestedPattern = "Pure Encapsulations B12 Folate", ItemTitle = "Pure Encapsulations B12 Folate",
                SampleDate = new DateOnly(2026, 7, 22), ItemIds = [407], TotalPrice = 22.50m
            }
        ];
        cut.Find("#item-add-submit-406").Click();

        Assert.Empty(cut.FindAll("#item-new-title-406")); // form closed after a successful add
        Assert.Contains("Pure Encapsulations B12 Folate", cut.Markup);
    }
}
