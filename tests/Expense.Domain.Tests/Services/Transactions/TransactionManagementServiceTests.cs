using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Services.Transactions;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.Transactions;

public class TransactionManagementServiceTests : DatabaseTestBase
{
    private readonly TransactionManagementService _sut = new(new CategorizationService());

    private async Task<Account> CreateAccountAsync() =>
        (await Context.Accounts.AddAsync(new Account { Name = "Amex", Type = AccountType.ActiveSpending })).Entity;

    [Fact]
    public async Task GetTransactionsAsync_MergesBankTransactionsAndAmazonItems_NewestFirst_WithCategoryNames()
    {
        var account = await CreateAccountAsync();
        var groceries = new Category { Name = "Groceries" };
        var supplements = new Category { Name = "Supplements" };
        Context.Categories.AddRange(groceries, supplements);
        await Context.SaveChangesAsync();

        Context.BankTransactions.AddRange(
            new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "PUBLIX", Amount = -40m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow },
            new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 3), Description = "AMAZON MARKETPLACE", Amount = -20m, ImportSource = "Test", IsAmazonMerchant = true, CreatedAt = DateTimeOffset.UtcNow });
        Context.AmazonOrderItems.Add(new AmazonOrderItem
        {
            OrderId = "112-123", OrderDate = new DateOnly(2026, 7, 5), ItemTitle = "Qunol Ultra CoQ10",
            Price = 30m, Quantity = 1, CategoryId = supplements.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var result = await _sut.GetTransactionsAsync(Context, searchText: null, categoryFilter: null);
        var rows = result.Items;

        Assert.Equal(2, rows.Count); // the raw "AMAZON MARKETPLACE" bank_transaction row is excluded - it's superseded by the itemized row
        Assert.Equal(2, result.TotalCount);
        Assert.Equal("Qunol Ultra CoQ10", rows[0].Description); // newest first
        Assert.Equal(TransactionSource.Amazon, rows[0].Source);
        Assert.Equal(-30m, rows[0].Amount); // Amazon amounts are negated to match the bank sign convention
        Assert.Equal(supplements.Id, rows[0].CategoryId);
        Assert.Equal("Supplements", rows[0].CategoryName);
        Assert.Equal("112-123", rows[0].OrderId);
        Assert.Equal(30m, rows[0].Price);
        Assert.Equal(1, rows[0].Quantity);
        Assert.False(rows[0].NeedsReview);

        Assert.Equal("PUBLIX", rows[1].Description);
        Assert.Equal(TransactionSource.Bank, rows[1].Source);
        Assert.Equal(groceries.Id, rows[1].CategoryId);
        Assert.Null(rows[1].OrderId);
    }

    [Fact]
    public async Task GetTransactionsAsync_PopulatesAccountName_ForBankRowsOnly()
    {
        var amex = await CreateAccountAsync();
        await Context.SaveChangesAsync();

        Context.BankTransactions.Add(new BankTransaction { AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "CVS PHARMACY", Amount = -25m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow });
        Context.AmazonOrderItems.Add(new AmazonOrderItem { OrderId = "1", OrderDate = new DateOnly(2026, 7, 2), ItemTitle = "Qunol Ultra CoQ10", Price = 30m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var result = await _sut.GetTransactionsAsync(Context, searchText: null, categoryFilter: null);

        var bankRow = result.Items.Single(r => r.Source == TransactionSource.Bank);
        var amazonRow = result.Items.Single(r => r.Source == TransactionSource.Amazon);
        Assert.Equal("Amex", bankRow.AccountName);
        Assert.Null(amazonRow.AccountName);
    }

    [Fact]
    public async Task GetTransactionsAsync_FilteredBySourceBank_ExcludesAmazonRows()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();

        Context.BankTransactions.Add(new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "PUBLIX", Amount = -40m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow });
        Context.AmazonOrderItems.Add(new AmazonOrderItem { OrderId = "1", OrderDate = new DateOnly(2026, 7, 2), ItemTitle = "Qunol Ultra CoQ10", Price = 30m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var result = await _sut.GetTransactionsAsync(Context, searchText: null, categoryFilter: null, sourceFilter: TransactionSource.Bank);

        var row = Assert.Single(result.Items);
        Assert.Equal("PUBLIX", row.Description);
    }

    [Fact]
    public async Task GetTransactionsAsync_FilteredBySourceAmazon_ExcludesBankRows()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();

        Context.BankTransactions.Add(new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "PUBLIX", Amount = -40m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow });
        Context.AmazonOrderItems.Add(new AmazonOrderItem { OrderId = "1", OrderDate = new DateOnly(2026, 7, 2), ItemTitle = "Qunol Ultra CoQ10", Price = 30m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var result = await _sut.GetTransactionsAsync(Context, searchText: null, categoryFilter: null, sourceFilter: TransactionSource.Amazon);

        var row = Assert.Single(result.Items);
        Assert.Equal("Qunol Ultra CoQ10", row.Description);
    }

    [Fact]
    public async Task GetTransactionsAsync_FilteredByAccount_ReturnsOnlyThatAccountsBankRows_AndExcludesAmazon()
    {
        var amex = await CreateAccountAsync();
        var discover = new Account { Name = "Discover", Type = AccountType.Debt };
        Context.Accounts.Add(discover);
        await Context.SaveChangesAsync();

        Context.BankTransactions.AddRange(
            new BankTransaction { AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "CVS PHARMACY", Amount = -25m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow },
            new BankTransaction { AccountId = discover.Id, TransactionDate = new DateOnly(2026, 7, 2), Description = "PUBLIX", Amount = -40m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow });
        Context.AmazonOrderItems.Add(new AmazonOrderItem { OrderId = "1", OrderDate = new DateOnly(2026, 7, 3), ItemTitle = "Qunol Ultra CoQ10", Price = 30m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var result = await _sut.GetTransactionsAsync(Context, searchText: null, categoryFilter: null, accountFilter: amex.Id);

        var row = Assert.Single(result.Items);
        Assert.Equal("CVS PHARMACY", row.Description);
        Assert.Equal("Amex", row.AccountName);
    }

    [Fact]
    public async Task GetTransactionsAsync_WithSearchText_FiltersByDescription_AcrossBothSources()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();
        Context.BankTransactions.Add(new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "TRUIST MORTG PAYMENT", Amount = -2681.22m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow });
        Context.AmazonOrderItems.Add(new AmazonOrderItem { OrderId = "1", OrderDate = new DateOnly(2026, 7, 2), ItemTitle = "Truist-branded mug", Price = 10m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var result = await _sut.GetTransactionsAsync(Context, searchText: "truist", categoryFilter: null);

        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task GetTransactionsAsync_FilteredByCategoryId_ReturnsOnlyThatCategoryAcrossBothSources()
    {
        var account = await CreateAccountAsync();
        var groceries = new Category { Name = "Groceries" };
        var supplements = new Category { Name = "Supplements" };
        Context.Categories.AddRange(groceries, supplements);
        await Context.SaveChangesAsync();

        Context.BankTransactions.Add(new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "PUBLIX", Amount = -40m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow });
        Context.AmazonOrderItems.Add(new AmazonOrderItem { OrderId = "1", OrderDate = new DateOnly(2026, 7, 2), ItemTitle = "Qunol Ultra CoQ10", Price = 30m, Quantity = 1, CategoryId = supplements.Id, CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var result = await _sut.GetTransactionsAsync(Context, searchText: null, categoryFilter: supplements.Id);

        var row = Assert.Single(result.Items);
        Assert.Equal("Qunol Ultra CoQ10", row.Description);
    }

    [Fact]
    public async Task GetTransactionsAsync_FilteredByUncategorizedSentinel_ReturnsOnlyUncategorizedAcrossBothSources()
    {
        var account = await CreateAccountAsync();
        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();

        Context.BankTransactions.Add(new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "PUBLIX", Amount = -40m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow });
        Context.AmazonOrderItems.Add(new AmazonOrderItem { OrderId = "1", OrderDate = new DateOnly(2026, 7, 2), ItemTitle = "Mystery Item", Price = 30m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var result = await _sut.GetTransactionsAsync(Context, searchText: null, categoryFilter: TransactionManagementService.UncategorizedFilterValue);

        var row = Assert.Single(result.Items);
        Assert.Equal("Mystery Item", row.Description);
    }

    [Fact]
    public async Task GetTransactionsAsync_NeedsReviewOnly_ReturnsOnlyFlaggedAmazonPlaceholders()
    {
        Context.AmazonOrderItems.AddRange(
            new AmazonOrderItem
            {
                OrderId = "1", OrderDate = new DateOnly(2026, 7, 1),
                ItemTitle = "(Item details unavailable in email - check Amazon order page)",
                Price = 22m, Quantity = 1, NeedsReview = true, CreatedAt = DateTimeOffset.UtcNow
            },
            new AmazonOrderItem
            {
                OrderId = "2", OrderDate = new DateOnly(2026, 7, 2), ItemTitle = "Qunol Ultra CoQ10",
                Price = 30m, Quantity = 1, NeedsReview = false, CreatedAt = DateTimeOffset.UtcNow
            });
        await Context.SaveChangesAsync();

        var result = await _sut.GetTransactionsAsync(Context, searchText: null, categoryFilter: null, needsReviewOnly: true);

        var row = Assert.Single(result.Items);
        Assert.Contains("unavailable in email", row.Description);
        Assert.True(row.NeedsReview);
    }

    [Fact]
    public async Task GetTransactionsAsync_PageSizeLimitsRowsReturned_ButTotalCountReflectsTheFullFilteredSet()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();
        for (var i = 1; i <= 5; i++)
        {
            Context.BankTransactions.Add(new BankTransaction
            {
                AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, i), Description = $"MERCHANT {i}",
                Amount = -10m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow
            });
        }
        await Context.SaveChangesAsync();

        var result = await _sut.GetTransactionsAsync(Context, searchText: null, categoryFilter: null, page: 1, pageSize: 2);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal("MERCHANT 5", result.Items[0].Description); // newest-first still applies within the page
        Assert.Equal("MERCHANT 4", result.Items[1].Description);
    }

    [Fact]
    public async Task GetTransactionsAsync_SecondPage_ReturnsTheNextSliceInTheSameOrder()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();
        for (var i = 1; i <= 5; i++)
        {
            Context.BankTransactions.Add(new BankTransaction
            {
                AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, i), Description = $"MERCHANT {i}",
                Amount = -10m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow
            });
        }
        await Context.SaveChangesAsync();

        var result = await _sut.GetTransactionsAsync(Context, searchText: null, categoryFilter: null, page: 2, pageSize: 2);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal("MERCHANT 3", result.Items[0].Description);
        Assert.Equal("MERCHANT 2", result.Items[1].Description);
    }

    [Fact]
    public async Task UpdateCategoryAsync_ForABankTransaction_ChangesItsCategory()
    {
        var account = await CreateAccountAsync();
        var wrong = new Category { Name = "Restaurants" };
        var correct = new Category { Name = "Groceries" };
        Context.Categories.AddRange(wrong, correct);
        await Context.SaveChangesAsync();

        var transaction = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "PUBLIX",
            Amount = -40m, ImportSource = "Test", CategoryId = wrong.Id, CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(transaction);
        await Context.SaveChangesAsync();

        await _sut.UpdateCategoryAsync(Context, TransactionSource.Bank, transaction.Id, correct.Id);

        var reloaded = await Context.BankTransactions.SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(correct.Id, reloaded.CategoryId);
    }

    [Fact]
    public async Task UpdateCategoryAsync_ForAnAmazonItem_ChangesItsCategory()
    {
        var wrong = new Category { Name = "Off-Budget/Misc" };
        var correct = new Category { Name = "Supplements" };
        Context.Categories.AddRange(wrong, correct);
        await Context.SaveChangesAsync();

        var item = new AmazonOrderItem { OrderId = "1", OrderDate = new DateOnly(2026, 7, 1), ItemTitle = "Qunol Ultra CoQ10", Price = 30m, Quantity = 1, CategoryId = wrong.Id, CreatedAt = DateTimeOffset.UtcNow };
        Context.AmazonOrderItems.Add(item);
        await Context.SaveChangesAsync();

        await _sut.UpdateCategoryAsync(Context, TransactionSource.Amazon, item.Id, correct.Id);

        var reloaded = await Context.AmazonOrderItems.SingleAsync(i => i.Id == item.Id);
        Assert.Equal(correct.Id, reloaded.CategoryId);
    }

    [Fact]
    public async Task UpdateCategoryAsync_CanSetBackToUncategorized()
    {
        var account = await CreateAccountAsync();
        var category = new Category { Name = "Groceries" };
        Context.Categories.Add(category);
        await Context.SaveChangesAsync();

        var transaction = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "PUBLIX",
            Amount = -40m, ImportSource = "Test", CategoryId = category.Id, CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(transaction);
        await Context.SaveChangesAsync();

        await _sut.UpdateCategoryAsync(Context, TransactionSource.Bank, transaction.Id, null);

        var reloaded = await Context.BankTransactions.SingleAsync(t => t.Id == transaction.Id);
        Assert.Null(reloaded.CategoryId);
    }

    [Fact]
    public async Task BulkCategorizeAsync_AppliesOneCategoryAcrossBothBankAndAmazonSelections()
    {
        var account = await CreateAccountAsync();
        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();

        var bankTxn = new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "PUBLIX", Amount = -40m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow };
        var amazonItem = new AmazonOrderItem { OrderId = "1", OrderDate = new DateOnly(2026, 7, 2), ItemTitle = "Organic Bananas", Price = 5m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow };
        Context.BankTransactions.Add(bankTxn);
        Context.AmazonOrderItems.Add(amazonItem);
        await Context.SaveChangesAsync();

        var updatedCount = await _sut.BulkCategorizeAsync(Context, [bankTxn.Id], [amazonItem.Id], groceries.Id);

        Assert.Equal(2, updatedCount);
        Assert.Equal(groceries.Id, (await Context.BankTransactions.SingleAsync(t => t.Id == bankTxn.Id)).CategoryId);
        Assert.Equal(groceries.Id, (await Context.AmazonOrderItems.SingleAsync(i => i.Id == amazonItem.Id)).CategoryId);
    }

    [Fact]
    public async Task UpdateAmazonItemDetailsAsync_FixesAPlaceholderItemFromAnUnparsableEmail()
    {
        var item = new AmazonOrderItem
        {
            OrderId = "113-1132648-3403446", OrderDate = new DateOnly(2026, 7, 1),
            ItemTitle = "(Item details unavailable in email - check Amazon order page)",
            Price = 22.00m, Quantity = 1, NeedsReview = true, CreatedAt = DateTimeOffset.UtcNow
        };
        Context.AmazonOrderItems.Add(item);
        await Context.SaveChangesAsync();

        await _sut.UpdateAmazonItemDetailsAsync(Context, item.Id, "Celestial Seasonings Wild Berry Zinger Tea", 21.99m, 2);

        var reloaded = await Context.AmazonOrderItems.SingleAsync(i => i.Id == item.Id);
        Assert.Equal("Celestial Seasonings Wild Berry Zinger Tea", reloaded.ItemTitle);
        Assert.Equal(21.99m, reloaded.Price);
        Assert.Equal(2, reloaded.Quantity);
        Assert.False(reloaded.NeedsReview); // fixed now - no longer needs a human to look at it
    }
}
