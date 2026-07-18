using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Transactions;
using Expense.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class TransactionsTests : BunitContext
{
    private class FakeTransactionsPageProvider : ITransactionsPageProvider
    {
        public List<TransactionRow> Transactions { get; set; } = [];
        public List<Category> Categories { get; set; } = [];
        public string? LastSearchText { get; private set; }
        public int? LastCategoryFilter { get; private set; }
        public bool LastCategoryFilterWasSet { get; private set; }
        public bool LastNeedsReviewOnly { get; private set; }
        public TransactionSource? LastUpdatedSource { get; private set; }
        public int? LastUpdatedId { get; private set; }
        public int? LastUpdatedCategoryId { get; private set; }
        public List<int>? LastBulkBankIds { get; private set; }
        public List<int>? LastBulkAmazonIds { get; private set; }
        public int? LastBulkCategoryId { get; private set; }
        public int? LastAmazonItemDetailsId { get; private set; }
        public string? LastAmazonItemTitle { get; private set; }
        public decimal? LastAmazonItemPrice { get; private set; }
        public int? LastAmazonItemQuantity { get; private set; }

        public Task<TransactionsPageData> GetTransactionsAsync(string? searchText, int? categoryFilter, bool needsReviewOnly = false, CancellationToken cancellationToken = default)
        {
            LastSearchText = searchText;
            LastCategoryFilter = categoryFilter;
            LastCategoryFilterWasSet = true;
            LastNeedsReviewOnly = needsReviewOnly;
            var filtered = Transactions.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                filtered = filtered.Where(t => t.Description.Contains(searchText, StringComparison.OrdinalIgnoreCase));
            }
            if (categoryFilter == 0)
            {
                filtered = filtered.Where(t => t.CategoryId == null);
            }
            else if (categoryFilter is { } catId)
            {
                filtered = filtered.Where(t => t.CategoryId == catId);
            }
            if (needsReviewOnly)
            {
                filtered = filtered.Where(t => t.NeedsReview);
            }
            return Task.FromResult(new TransactionsPageData { Transactions = filtered.ToList(), Categories = Categories });
        }

        public Task UpdateCategoryAsync(TransactionSource source, int id, int? categoryId, CancellationToken cancellationToken = default)
        {
            LastUpdatedSource = source;
            LastUpdatedId = id;
            LastUpdatedCategoryId = categoryId;
            return Task.CompletedTask;
        }

        public Task<int> BulkCategorizeAsync(IReadOnlyList<int> bankTransactionIds, IReadOnlyList<int> amazonItemIds, int categoryId, CancellationToken cancellationToken = default)
        {
            LastBulkBankIds = bankTransactionIds.ToList();
            LastBulkAmazonIds = amazonItemIds.ToList();
            LastBulkCategoryId = categoryId;
            return Task.FromResult(bankTransactionIds.Count + amazonItemIds.Count);
        }

        public Task UpdateAmazonItemDetailsAsync(int itemId, string itemTitle, decimal price, int quantity, CancellationToken cancellationToken = default)
        {
            LastAmazonItemDetailsId = itemId;
            LastAmazonItemTitle = itemTitle;
            LastAmazonItemPrice = price;
            LastAmazonItemQuantity = quantity;
            return Task.CompletedTask;
        }
    }

    private static FakeTransactionsPageProvider MakeProvider() => new()
    {
        Categories = [new Category { Id = 1, Name = "Groceries" }, new Category { Id = 2, Name = "Supplements" }],
        Transactions =
        [
            new TransactionRow { Source = TransactionSource.Bank, Id = 100, Date = new DateOnly(2026, 7, 1), Description = "PUBLIX NORCROSS GA", Amount = -40m, CategoryId = 1, CategoryName = "Groceries" },
            new TransactionRow { Source = TransactionSource.Bank, Id = 101, Date = new DateOnly(2026, 7, 5), Description = "TRUIST MORTG PAYMENT", Amount = -2681.22m, CategoryId = null, CategoryName = null },
            new TransactionRow { Source = TransactionSource.Amazon, Id = 200, Date = new DateOnly(2026, 7, 3), Description = "Qunol Ultra CoQ10", Amount = -30m, CategoryId = 2, CategoryName = "Supplements", OrderId = "112-123", Price = 30m, Quantity = 1 },
            new TransactionRow { Source = TransactionSource.Amazon, Id = 201, Date = new DateOnly(2026, 7, 4), Description = "(Item details unavailable in email - check Amazon order page)", Amount = -22m, CategoryId = null, CategoryName = null, OrderId = "113-456", Price = 22m, Quantity = 1, NeedsReview = true }
        ]
    };

    [Fact]
    public void Transactions_RendersEveryRowWithDateDescriptionAmountAndCategory()
    {
        Services.AddSingleton<ITransactionsPageProvider>(MakeProvider());

        var cut = Render<Transactions>();

        Assert.Contains("07/01/2026", cut.Markup);
        Assert.Contains("PUBLIX NORCROSS GA", cut.Markup);
        Assert.Contains("40.00", cut.Markup);
        Assert.Contains("TRUIST MORTG PAYMENT", cut.Markup);
        Assert.Contains("Qunol Ultra CoQ10", cut.Markup);
    }

    [Fact]
    public void Transactions_ShowsSourceColumn_BankOrAmazon()
    {
        Services.AddSingleton<ITransactionsPageProvider>(MakeProvider());

        var cut = Render<Transactions>();

        var bankRow = cut.Find("#select-bank-100").Closest("tr")!;
        var amazonRow = cut.Find("#select-amazon-200").Closest("tr")!;
        Assert.Contains("Bank", bankRow.TextContent);
        Assert.Contains("Amazon", amazonRow.TextContent);
    }

    [Fact]
    public void Transactions_AmazonRow_ShowsOrderId()
    {
        Services.AddSingleton<ITransactionsPageProvider>(MakeProvider());

        var cut = Render<Transactions>();

        Assert.Contains("112-123", cut.Markup);
    }

    [Fact]
    public void Transactions_CategoryDropdown_IsPrefilledWithTheCurrentCategory()
    {
        Services.AddSingleton<ITransactionsPageProvider>(MakeProvider());

        var cut = Render<Transactions>();

        var selectedOption = cut.Find("#category-bank-100 option[selected]");
        Assert.Equal("1", selectedOption.GetAttribute("value"));
    }

    [Fact]
    public void Transactions_UncategorizedRow_ShowsUncategorizedSelected()
    {
        Services.AddSingleton<ITransactionsPageProvider>(MakeProvider());

        var cut = Render<Transactions>();

        var selectedOption = cut.Find("#category-bank-101 option[selected]");
        Assert.Equal("0", selectedOption.GetAttribute("value"));
    }

    [Fact]
    public void ChangingCategoryDropdown_ForABankRow_CallsUpdateCategoryWithBankSource()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ITransactionsPageProvider>(provider);

        var cut = Render<Transactions>();
        cut.Find("#category-bank-101").Change("2");

        Assert.Equal(TransactionSource.Bank, provider.LastUpdatedSource);
        Assert.Equal(101, provider.LastUpdatedId);
        Assert.Equal(2, provider.LastUpdatedCategoryId);
    }

    [Fact]
    public void ChangingCategoryDropdown_ForAnAmazonRow_CallsUpdateCategoryWithAmazonSource()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ITransactionsPageProvider>(provider);

        var cut = Render<Transactions>();
        cut.Find("#category-amazon-201").Change("2");

        Assert.Equal(TransactionSource.Amazon, provider.LastUpdatedSource);
        Assert.Equal(201, provider.LastUpdatedId);
        Assert.Equal(2, provider.LastUpdatedCategoryId);
    }

    [Fact]
    public void ChangingCategoryDropdown_ToUncategorized_PassesNull()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ITransactionsPageProvider>(provider);

        var cut = Render<Transactions>();
        cut.Find("#category-bank-100").Change("0");

        Assert.Null(provider.LastUpdatedCategoryId);
    }

    [Fact]
    public void TypingInSearchBox_PassesSearchTextToTheProvider()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ITransactionsPageProvider>(provider);

        var cut = Render<Transactions>();
        cut.Find("#search-box").Change("truist");

        Assert.Equal("truist", provider.LastSearchText);
    }

    [Fact]
    public void SelectingACategoryFilter_PassesItToTheProvider()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ITransactionsPageProvider>(provider);

        var cut = Render<Transactions>();
        cut.Find("#category-filter").Change("2");

        Assert.Equal(2, provider.LastCategoryFilter);
    }

    [Fact]
    public void SelectingUncategorizedFilter_PassesTheSentinelValue()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ITransactionsPageProvider>(provider);

        var cut = Render<Transactions>();
        cut.Find("#category-filter").Change("0");

        Assert.Equal(0, provider.LastCategoryFilter);
    }

    [Fact]
    public void SelectingAllCategoriesFilter_PassesNull()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ITransactionsPageProvider>(provider);

        var cut = Render<Transactions>();
        cut.Find("#category-filter").Change("-1"); // "-- all categories --"

        Assert.Null(provider.LastCategoryFilter);
    }

    [Fact]
    public void ShiftClickingARow_SelectsTheRangeFromTheLastClickedRow_AcrossBothSources()
    {
        // Rendering order here is bank-100 (index 0), bank-101 (index 1), amazon-200 (index 2),
        // amazon-201 (index 3). Shift-clicking amazon-200 after plain-clicking bank-100 should
        // select the whole visual range in between, regardless of source.
        var provider = MakeProvider();
        Services.AddSingleton<ITransactionsPageProvider>(provider);

        var cut = Render<Transactions>();
        cut.Find("#select-bank-100").Click();
        cut.Find("#select-amazon-200").Click(new Microsoft.AspNetCore.Components.Web.MouseEventArgs { ShiftKey = true });

        Assert.Contains("3 selected", cut.Find("#selected-count").TextContent);
    }

    [Fact]
    public void CheckingRowsAcrossBothSourcesAndApplyingACategory_BulkCategorizesBoth()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ITransactionsPageProvider>(provider);

        var cut = Render<Transactions>();
        cut.Find("#select-bank-101").Click();
        cut.Find("#select-amazon-201").Click();
        cut.Find("#bulk-category").Change("2");
        cut.Find("#apply-bulk-category-btn").Click();

        Assert.Equal([101], provider.LastBulkBankIds);
        Assert.Equal([201], provider.LastBulkAmazonIds);
        Assert.Equal(2, provider.LastBulkCategoryId);
    }

    [Fact]
    public void EditingAnAmazonRowsDescriptionPriceAndQuantity_SavesTheDetails()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ITransactionsPageProvider>(provider);

        var cut = Render<Transactions>();
        cut.Find("#amazon-title-201").Change("Celestial Seasonings Wild Berry Zinger Tea");
        cut.Find("#amazon-price-201").Change("21.99");
        cut.Find("#amazon-quantity-201").Change("2");

        Assert.Equal(201, provider.LastAmazonItemDetailsId);
        Assert.Equal("Celestial Seasonings Wild Berry Zinger Tea", provider.LastAmazonItemTitle);
        Assert.Equal(21.99m, provider.LastAmazonItemPrice);
        Assert.Equal(2, provider.LastAmazonItemQuantity);
    }

    [Fact]
    public void BankRowsDescription_IsNotEditable()
    {
        Services.AddSingleton<ITransactionsPageProvider>(MakeProvider());

        var cut = Render<Transactions>();

        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find("#amazon-title-100"));
    }

    [Fact]
    public void RowNeedingReview_IsHighlighted()
    {
        Services.AddSingleton<ITransactionsPageProvider>(MakeProvider());

        var cut = Render<Transactions>();

        var row = cut.Find("#select-amazon-201").Closest("tr")!;
        Assert.Contains("background-color: yellow", row.GetAttribute("style"));
    }

    [Fact]
    public void RowNotNeedingReview_IsNotHighlighted()
    {
        Services.AddSingleton<ITransactionsPageProvider>(MakeProvider());

        var cut = Render<Transactions>();

        var row = cut.Find("#select-amazon-200").Closest("tr")!;
        Assert.DoesNotContain("background-color: yellow", row.GetAttribute("style") ?? "");
    }

    [Fact]
    public void CheckingNeedsReviewFilter_PassesItToTheProvider()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ITransactionsPageProvider>(provider);

        var cut = Render<Transactions>();
        cut.Find("#needs-review-filter").Change(true);

        Assert.True(provider.LastNeedsReviewOnly);
    }

    [Fact]
    public void SearchQueryParameter_PrefillsTheSearchBox_ForDeepLinksFromTheReviewQueue()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ITransactionsPageProvider>(provider);

        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("search", "PUBLIX"));
        var cut = Render<Transactions>();

        Assert.Equal("PUBLIX", cut.Find("#search-box").GetAttribute("value"));
        Assert.Equal("PUBLIX", provider.LastSearchText);
        Assert.Contains("PUBLIX NORCROSS GA", cut.Markup);
        Assert.DoesNotContain("TRUIST MORTG PAYMENT", cut.Markup);
    }
}
