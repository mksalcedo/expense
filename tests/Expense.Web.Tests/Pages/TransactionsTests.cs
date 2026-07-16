using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Transactions;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class TransactionsTests : BunitContext
{
    private class FakeTransactionsPageProvider : ITransactionsPageProvider
    {
        public List<TransactionRow> Transactions { get; set; } = [];
        public List<Category> Categories { get; set; } = [];
        public string? LastSearchText { get; private set; }
        public int? LastUpdatedTransactionId { get; private set; }
        public int? LastUpdatedCategoryId { get; private set; }

        public Task<TransactionsPageData> GetTransactionsAsync(string? searchText, CancellationToken cancellationToken = default)
        {
            LastSearchText = searchText;
            var filtered = string.IsNullOrWhiteSpace(searchText)
                ? Transactions
                : Transactions.Where(t => t.Description.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();
            return Task.FromResult(new TransactionsPageData { Transactions = filtered, Categories = Categories });
        }

        public Task UpdateCategoryAsync(int transactionId, int? categoryId, CancellationToken cancellationToken = default)
        {
            LastUpdatedTransactionId = transactionId;
            LastUpdatedCategoryId = categoryId;
            return Task.CompletedTask;
        }
    }

    private static FakeTransactionsPageProvider MakeProvider() => new()
    {
        Categories = [new Category { Id = 1, Name = "Groceries" }, new Category { Id = 2, Name = "Restaurants" }],
        Transactions =
        [
            new TransactionRow { Id = 100, Date = new DateOnly(2026, 7, 1), Description = "PUBLIX NORCROSS GA", Amount = -40m, CategoryId = 1, CategoryName = "Groceries" },
            new TransactionRow { Id = 101, Date = new DateOnly(2026, 7, 5), Description = "TRUIST MORTG PAYMENT", Amount = -2681.22m, CategoryId = null, CategoryName = null }
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
    }

    [Fact]
    public void Transactions_CategoryDropdown_IsPrefilledWithTheCurrentCategory()
    {
        Services.AddSingleton<ITransactionsPageProvider>(MakeProvider());

        var cut = Render<Transactions>();

        var selectedOption = cut.Find("#category-100 option[selected]");
        Assert.Equal("1", selectedOption.GetAttribute("value"));
    }

    [Fact]
    public void Transactions_UncategorizedRow_ShowsUncategorizedSelected()
    {
        Services.AddSingleton<ITransactionsPageProvider>(MakeProvider());

        var cut = Render<Transactions>();

        var selectedOption = cut.Find("#category-101 option[selected]");
        Assert.Equal("0", selectedOption.GetAttribute("value"));
    }

    [Fact]
    public void ChangingCategoryDropdown_CallsUpdateCategoryImmediately()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ITransactionsPageProvider>(provider);

        var cut = Render<Transactions>();
        cut.Find("#category-101").Change("2");

        Assert.Equal(101, provider.LastUpdatedTransactionId);
        Assert.Equal(2, provider.LastUpdatedCategoryId);
    }

    [Fact]
    public void ChangingCategoryDropdown_ToUncategorized_PassesNull()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ITransactionsPageProvider>(provider);

        var cut = Render<Transactions>();
        cut.Find("#category-100").Change("0");

        Assert.Equal(100, provider.LastUpdatedTransactionId);
        Assert.Null(provider.LastUpdatedCategoryId);
    }

    [Fact]
    public void TypingInSearchBox_FiltersTransactionsByDescription()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ITransactionsPageProvider>(provider);

        var cut = Render<Transactions>();
        cut.Find("#search-box").Change("truist");

        Assert.Equal("truist", provider.LastSearchText);
        Assert.Contains("TRUIST MORTG PAYMENT", cut.Markup);
        Assert.DoesNotContain("PUBLIX", cut.Markup);
    }
}
