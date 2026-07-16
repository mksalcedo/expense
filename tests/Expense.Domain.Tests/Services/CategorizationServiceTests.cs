using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services;

public class CategorizationServiceTests : DatabaseTestBase
{
    private readonly CategorizationService _sut = new();

    private async Task<Account> CreateAccountAsync() =>
        (await Context.Accounts.AddAsync(new Account { Name = "Amex", Type = AccountType.ActiveSpending })).Entity;

    [Fact]
    public async Task Transaction_MatchingAMerchantRule_GetsCategorizedOnImport()
    {
        var account = await CreateAccountAsync();
        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();
        Context.MerchantRules.Add(new MerchantRule { MerchantPattern = "%INGLES%", CategoryId = groceries.Id });
        await Context.SaveChangesAsync();

        var transaction = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 13),
            Description = "INGLES #123 NORCROSS GA", Amount = -50m, ImportSource = "SimpleFin",
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _sut.ApplyMerchantRuleAsync(Context, transaction);

        Assert.Equal(groceries.Id, transaction.CategoryId);
    }

    [Fact]
    public async Task Transaction_WithNoMatchingRule_StaysPendingCategorization()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();

        var transaction = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 13),
            Description = "SOME BRAND NEW MERCHANT", Amount = -20m, ImportSource = "SimpleFin",
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _sut.ApplyMerchantRuleAsync(Context, transaction);

        Assert.Null(transaction.CategoryId);
    }

    [Fact]
    public async Task AmazonMerchantTransaction_NeverGetsCategorized_EvenIfAPatternWouldMatch()
    {
        var account = await CreateAccountAsync();
        var misc = new Category { Name = "Off-Budget/Misc" };
        Context.Categories.Add(misc);
        await Context.SaveChangesAsync();
        Context.MerchantRules.Add(new MerchantRule { MerchantPattern = "%AMAZON%", CategoryId = misc.Id });
        await Context.SaveChangesAsync();

        var transaction = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 13),
            Description = "AMAZON MARKETPLACE", Amount = -30.73m, ImportSource = "SimpleFin",
            IsAmazonMerchant = true, CreatedAt = DateTimeOffset.UtcNow
        };

        await _sut.ApplyMerchantRuleAsync(Context, transaction);

        Assert.Null(transaction.CategoryId);
    }

    [Fact]
    public async Task MerchantRuleMatching_IsCaseInsensitive()
    {
        var account = await CreateAccountAsync();
        var restaurants = new Category { Name = "Restaurants" };
        Context.Categories.Add(restaurants);
        await Context.SaveChangesAsync();
        Context.MerchantRules.Add(new MerchantRule { MerchantPattern = "%chipotle%", CategoryId = restaurants.Id });
        await Context.SaveChangesAsync();

        var transaction = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 13),
            Description = "CHIPOTLE 1652 NORCROSS GA", Amount = -25.39m, ImportSource = "SimpleFin",
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _sut.ApplyMerchantRuleAsync(Context, transaction);

        Assert.Equal(restaurants.Id, transaction.CategoryId);
    }

    [Fact]
    public async Task GetPendingBankTransactionsAsync_ExcludesCategorizedAndAmazonRows()
    {
        var account = await CreateAccountAsync();
        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();

        Context.BankTransactions.AddRange(
            new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "PENDING ONE", Amount = -10m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow },
            new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 2), Description = "ALREADY CATEGORIZED", Amount = -20m, ImportSource = "Test", CategoryId = groceries.Id, CreatedAt = DateTimeOffset.UtcNow },
            new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 3), Description = "AMAZON MARKETPLACE", Amount = -30m, ImportSource = "Test", IsAmazonMerchant = true, CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var pending = await _sut.GetPendingBankTransactionsAsync(Context);

        var pendingOne = Assert.Single(pending);
        Assert.Equal("PENDING ONE", pendingOne.Description);
    }

    [Fact]
    public async Task GetPendingAmazonOrderItemsAsync_ExcludesItemsWithAProduct()
    {
        var supplements = new Category { Name = "Supplements" };
        Context.Categories.Add(supplements);
        await Context.SaveChangesAsync();
        var product = new Product { ProductPattern = "%KNOWN%", CategoryId = supplements.Id };
        Context.Products.Add(product);
        await Context.SaveChangesAsync();

        Context.AmazonOrderItems.AddRange(
            new AmazonOrderItem { OrderId = "1", OrderDate = new DateOnly(2026, 7, 1), ItemTitle = "Unknown Product", Price = 10m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow },
            new AmazonOrderItem { OrderId = "2", OrderDate = new DateOnly(2026, 7, 2), ItemTitle = "Known Product", Price = 20m, Quantity = 1, ProductId = product.Id, CategoryId = supplements.Id, CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var pending = await _sut.GetPendingAmazonOrderItemsAsync(Context);

        var pendingItem = Assert.Single(pending);
        Assert.Equal("Unknown Product", pendingItem.ItemTitle);
    }

    [Fact]
    public async Task CategorizeTransactionAsync_WithoutCreatingARule_OnlySetsThatOneTransaction()
    {
        var account = await CreateAccountAsync();
        var misc = new Category { Name = "Off-Budget/Misc" };
        Context.Categories.Add(misc);
        await Context.SaveChangesAsync();

        var transaction = new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "ONE OFF MERCHANT", Amount = -15m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow };
        var otherPending = new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 2), Description = "ONE OFF MERCHANT AGAIN", Amount = -15m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow };
        Context.BankTransactions.AddRange(transaction, otherPending);
        await Context.SaveChangesAsync();

        await _sut.CategorizeTransactionAsync(Context, transaction.Id, misc.Id, merchantPatternToCreate: null);

        Assert.Equal(misc.Id, transaction.CategoryId);
        Assert.Null(otherPending.CategoryId); // no rule created, so the similarly-named one is untouched
        Assert.Empty(await Context.MerchantRules.ToListAsync());
    }

    [Fact]
    public async Task CategorizeTransactionAsync_CreatingARule_AppliesRetroactivelyToOtherPendingMatches()
    {
        var account = await CreateAccountAsync();
        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();

        var transaction = new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "TRADER JOE S #123", Amount = -40m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow };
        var otherPending = new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 5), Description = "TRADER JOE S #456", Amount = -22m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow };
        var unrelated = new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 6), Description = "SHELL GAS STATION", Amount = -35m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow };
        Context.BankTransactions.AddRange(transaction, otherPending, unrelated);
        await Context.SaveChangesAsync();

        var retroactiveCount = await _sut.CategorizeTransactionAsync(Context, transaction.Id, groceries.Id, merchantPatternToCreate: "%TRADER JOE%");

        Assert.Equal(groceries.Id, transaction.CategoryId);
        Assert.Equal(groceries.Id, otherPending.CategoryId);
        Assert.Null(unrelated.CategoryId);
        Assert.Equal(1, retroactiveCount); // just the one other match, not counting the transaction itself
    }

    [Fact]
    public async Task CategorizeTransactionAsync_CreatingARule_MatchesOtherRowsDespiteDifferentInternalWhitespacePadding()
    {
        // Real bank exports pad heavily and inconsistently (e.g. Truist's own mortgage
        // description varies its internal spacing statement to statement). The rule's
        // pattern is derived from a whitespace-collapsed description, so matching against
        // other rows' raw (uncollapsed) text must also collapse whitespace first, or a
        // pattern like "TRUIST MORTG OLB MTGPMT" never matches "TRUIST MORTG     OLB MTGPMT".
        var account = await CreateAccountAsync();
        var mortgage = new Category { Name = "Truist" };
        Context.Categories.Add(mortgage);
        await Context.SaveChangesAsync();

        var transaction = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 8),
            Description = "TRUIST MORTG     OLB MTGPMT 260706 3001469588      MARK SALCEDO",
            Amount = -2681.22m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow
        };
        var otherPending = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 6, 8),
            Description = "TRUIST MORTG     OLB MTGPMT 260604 3001469588      MARK SALCEDO",
            Amount = -2681.22m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.AddRange(transaction, otherPending);
        await Context.SaveChangesAsync();

        var retroactiveCount = await _sut.CategorizeTransactionAsync(
            Context, transaction.Id, mortgage.Id, merchantPatternToCreate: "TRUIST MORTG OLB MTGPMT");

        Assert.Equal(mortgage.Id, transaction.CategoryId);
        Assert.Equal(mortgage.Id, otherPending.CategoryId);
        Assert.Equal(1, retroactiveCount);
    }

    [Fact]
    public async Task ApplyMerchantRuleAsync_MatchesDespiteDifferentInternalWhitespacePadding()
    {
        var account = await CreateAccountAsync();
        var mortgage = new Category { Name = "Truist" };
        Context.Categories.Add(mortgage);
        await Context.SaveChangesAsync();
        Context.MerchantRules.Add(new MerchantRule { MerchantPattern = "TRUIST MORTG OLB MTGPMT", CategoryId = mortgage.Id });
        await Context.SaveChangesAsync();

        var transaction = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 8, 8),
            Description = "TRUIST MORTG     OLB MTGPMT 260806 3001469588      MARK SALCEDO",
            Amount = -2681.22m, ImportSource = "SimpleFin", CreatedAt = DateTimeOffset.UtcNow
        };

        await _sut.ApplyMerchantRuleAsync(Context, transaction);

        Assert.Equal(mortgage.Id, transaction.CategoryId);
    }

    [Fact]
    public async Task CategorizeAmazonItemAsync_WithoutCreatingAProduct_OnlySetsThatOneItem()
    {
        var misc = new Category { Name = "Off-Budget/Misc" };
        Context.Categories.Add(misc);
        await Context.SaveChangesAsync();

        var item = new AmazonOrderItem { OrderId = "1", OrderDate = new DateOnly(2026, 7, 1), ItemTitle = "One Off Gadget", Price = 10m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow };
        var otherPending = new AmazonOrderItem { OrderId = "2", OrderDate = new DateOnly(2026, 7, 2), ItemTitle = "One Off Gadget Refill", Price = 8m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow };
        Context.AmazonOrderItems.AddRange(item, otherPending);
        await Context.SaveChangesAsync();

        await _sut.CategorizeAmazonItemAsync(Context, item.Id, misc.Id, productPatternToCreate: null);

        Assert.Equal(misc.Id, item.CategoryId);
        Assert.Null(item.ProductId);
        Assert.Null(otherPending.CategoryId);
        Assert.Empty(await Context.Products.ToListAsync());
    }

    [Fact]
    public async Task CategorizeAmazonItemAsync_CreatingAProduct_AppliesRetroactivelyToOtherPendingMatches()
    {
        var supplements = new Category { Name = "Supplements" };
        Context.Categories.Add(supplements);
        await Context.SaveChangesAsync();

        var item = new AmazonOrderItem { OrderId = "1", OrderDate = new DateOnly(2026, 7, 1), ItemTitle = "Qunol Ultra CoQ10 100mg", Price = 30m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow };
        var otherPending = new AmazonOrderItem { OrderId = "2", OrderDate = new DateOnly(2026, 7, 5), ItemTitle = "Qunol Ultra CoQ10 200mg", Price = 45m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow };
        var unrelated = new AmazonOrderItem { OrderId = "3", OrderDate = new DateOnly(2026, 7, 6), ItemTitle = "Random Kitchen Gadget", Price = 12m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow };
        Context.AmazonOrderItems.AddRange(item, otherPending, unrelated);
        await Context.SaveChangesAsync();

        var retroactiveCount = await _sut.CategorizeAmazonItemAsync(Context, item.Id, supplements.Id, productPatternToCreate: "%QUNOL%");

        Assert.Equal(supplements.Id, item.CategoryId);
        Assert.NotNull(item.ProductId);
        Assert.Equal(supplements.Id, otherPending.CategoryId);
        Assert.NotNull(otherPending.ProductId);
        Assert.Null(unrelated.CategoryId);
        Assert.Equal(1, retroactiveCount);
    }

    [Fact]
    public async Task ReapplyRulesToPendingAsync_CategorizesAPendingTransactionThatNowMatchesAnExistingRule()
    {
        // Simulates the real Truist bug: a rule already exists, but a pending transaction
        // didn't match it at the time (e.g. a matching bug since fixed, or the rule was
        // created after this row became pending) - re-running the check should catch it.
        var account = await CreateAccountAsync();
        var mortgage = new Category { Name = "Truist" };
        Context.Categories.Add(mortgage);
        await Context.SaveChangesAsync();
        Context.MerchantRules.Add(new MerchantRule { MerchantPattern = "TRUIST MORTG OLB MTGPMT", CategoryId = mortgage.Id });

        var stillPending = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 6, 8),
            Description = "TRUIST MORTG     OLB MTGPMT 260604 3001469588      MARK SALCEDO",
            Amount = -2681.22m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(stillPending);
        await Context.SaveChangesAsync();

        var result = await _sut.ReapplyRulesToPendingAsync(Context);

        Assert.Equal(1, result.TransactionsUpdated);
        Assert.Equal(mortgage.Id, stillPending.CategoryId);
    }

    [Fact]
    public async Task ReapplyRulesToPendingAsync_CategorizesAPendingAmazonItemThatNowMatchesAnExistingProduct()
    {
        var supplements = new Category { Name = "Supplements" };
        Context.Categories.Add(supplements);
        await Context.SaveChangesAsync();
        Context.Products.Add(new Product { ProductPattern = "%QUNOL%", CategoryId = supplements.Id });

        var stillPending = new AmazonOrderItem
        {
            OrderId = "1", OrderDate = new DateOnly(2026, 7, 1), ItemTitle = "Qunol Ultra CoQ10 100mg",
            Price = 30m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow
        };
        Context.AmazonOrderItems.Add(stillPending);
        await Context.SaveChangesAsync();

        var result = await _sut.ReapplyRulesToPendingAsync(Context);

        Assert.Equal(1, result.ItemsUpdated);
        Assert.Equal(supplements.Id, stillPending.CategoryId);
        Assert.NotNull(stillPending.ProductId);
    }

    [Fact]
    public async Task ReapplyRulesToPendingAsync_LeavesTrulyUnmatchedRowsPending()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();

        var unmatched = new BankTransaction
        {
            AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1),
            Description = "BRAND NEW MERCHANT NOBODY HAS A RULE FOR", Amount = -10m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow
        };
        Context.BankTransactions.Add(unmatched);
        await Context.SaveChangesAsync();

        var result = await _sut.ReapplyRulesToPendingAsync(Context);

        Assert.Equal(0, result.TransactionsUpdated);
        Assert.Null(unmatched.CategoryId);
    }

    [Fact]
    public async Task GetPendingTransactionGroupsAsync_GroupsRepeatedMerchantsIntoOneRow()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();
        Context.BankTransactions.AddRange(
            new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "PUBLIX NORCROSS GA", Amount = -40m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow },
            new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 5), Description = "PUBLIX NORCROSS GA", Amount = -22m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow },
            new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 6), Description = "TRADER JOE S #123", Amount = -15m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var groups = await _sut.GetPendingTransactionGroupsAsync(Context);

        Assert.Equal(2, groups.Count);
        var publix = groups.Single(g => g.SuggestedPattern == "PUBLIX NORCROSS GA");
        Assert.Equal(2, publix.TransactionIds.Count);
        Assert.Equal(-62m, publix.TotalAmount);
        Assert.Equal(new DateOnly(2026, 7, 5), publix.SampleDate); // most recent of the two, since pending rows are ordered by date descending
        var traderJoes = groups.Single(g => g.SuggestedPattern == "TRADER JOE S");
        Assert.Single(traderJoes.TransactionIds);
    }

    [Fact]
    public async Task GetPendingAmazonItemGroupsAsync_GroupsByExactTitleIntoOneRow()
    {
        Context.AmazonOrderItems.AddRange(
            new AmazonOrderItem { OrderId = "1", OrderDate = new DateOnly(2026, 7, 1), ItemTitle = "Qunol Ultra CoQ10", Price = 30m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow },
            new AmazonOrderItem { OrderId = "2", OrderDate = new DateOnly(2026, 7, 5), ItemTitle = "Qunol Ultra CoQ10", Price = 32m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow },
            new AmazonOrderItem { OrderId = "3", OrderDate = new DateOnly(2026, 7, 6), ItemTitle = "Random Kitchen Gadget", Price = 12m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var groups = await _sut.GetPendingAmazonItemGroupsAsync(Context);

        Assert.Equal(2, groups.Count);
        var qunol = groups.Single(g => g.ItemTitle == "Qunol Ultra CoQ10");
        Assert.Equal(2, qunol.ItemIds.Count);
        Assert.Equal(62m, qunol.TotalPrice);
        Assert.Equal("Qunol Ultra CoQ10", qunol.SuggestedPattern);
        Assert.Equal(new DateOnly(2026, 7, 5), qunol.SampleDate); // most recent of the two, since pending rows are ordered by date descending
    }
}
