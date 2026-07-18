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
    public async Task GetPendingAmazonOrderItemsAsync_ExcludesItemsWithAProductAndCategory()
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
    public async Task GetPendingAmazonOrderItemsAsync_ExcludesItemsThatHaveACategoryButNoProduct()
    {
        // Real bug: BulkCategorizeAmazonItemsAsync deliberately sets CategoryId only, never
        // ProductId (a bulk selection has no single pattern to build a product from) - but
        // this query used to filter on ProductId == null, so a bulk-categorized item kept
        // showing up as "pending" forever even though it had a real category.
        var supplements = new Category { Name = "Supplements" };
        Context.Categories.Add(supplements);
        await Context.SaveChangesAsync();

        var bulkCategorized = new AmazonOrderItem
        {
            OrderId = "1", OrderDate = new DateOnly(2026, 7, 1), ItemTitle = "Vitamin C",
            Price = 15m, Quantity = 1, CategoryId = supplements.Id, CreatedAt = DateTimeOffset.UtcNow
        };
        var stillPending = new AmazonOrderItem
        {
            OrderId = "2", OrderDate = new DateOnly(2026, 7, 2), ItemTitle = "Random Gadget",
            Price = 12m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow
        };
        Context.AmazonOrderItems.AddRange(bulkCategorized, stillPending);
        await Context.SaveChangesAsync();

        var pending = await _sut.GetPendingAmazonOrderItemsAsync(Context);

        var pendingItem = Assert.Single(pending);
        Assert.Equal("Random Gadget", pendingItem.ItemTitle);
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
    public async Task BulkCategorizeTransactionsAsync_SetsTheSameCategoryOnEveryGivenTransaction_RegardlessOfPattern()
    {
        // Bulk selection can span multiple different merchants/patterns (e.g. 35 different
        // grocery stores) with no single common pattern to build a rule from - it just
        // directly sets the category on exactly the given rows.
        var account = await CreateAccountAsync();
        var groceries = new Category { Name = "Groceries" };
        Context.Categories.Add(groceries);
        await Context.SaveChangesAsync();

        var publix = new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "PUBLIX", Amount = -40m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow };
        var kroger = new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 2), Description = "KROGER", Amount = -30m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow };
        var untouched = new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 3), Description = "SHELL GAS", Amount = -25m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow };
        Context.BankTransactions.AddRange(publix, kroger, untouched);
        await Context.SaveChangesAsync();

        var updatedCount = await _sut.BulkCategorizeTransactionsAsync(Context, [publix.Id, kroger.Id], groceries.Id);

        Assert.Equal(2, updatedCount);
        Assert.Equal(groceries.Id, publix.CategoryId);
        Assert.Equal(groceries.Id, kroger.CategoryId);
        Assert.Null(untouched.CategoryId);
    }

    [Fact]
    public async Task BulkCategorizeAmazonItemsAsync_SetsTheSameCategoryOnEveryGivenItem()
    {
        var supplements = new Category { Name = "Supplements" };
        Context.Categories.Add(supplements);
        await Context.SaveChangesAsync();

        var item1 = new AmazonOrderItem { OrderId = "1", OrderDate = new DateOnly(2026, 7, 1), ItemTitle = "Vitamin C", Price = 15m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow };
        var item2 = new AmazonOrderItem { OrderId = "2", OrderDate = new DateOnly(2026, 7, 2), ItemTitle = "Fish Oil", Price = 20m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow };
        var untouched = new AmazonOrderItem { OrderId = "3", OrderDate = new DateOnly(2026, 7, 3), ItemTitle = "Random Gadget", Price = 12m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow };
        Context.AmazonOrderItems.AddRange(item1, item2, untouched);
        await Context.SaveChangesAsync();

        var updatedCount = await _sut.BulkCategorizeAmazonItemsAsync(Context, [item1.Id, item2.Id], supplements.Id);

        Assert.Equal(2, updatedCount);
        Assert.Equal(supplements.Id, item1.CategoryId);
        Assert.Equal(supplements.Id, item2.CategoryId);
        Assert.Null(untouched.CategoryId);
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
        Assert.Equal("Amex", publix.AccountName);
        Assert.Equal("Amex", traderJoes.AccountName);
    }

    [Fact]
    public async Task GetPendingTransactionGroupsAsync_SameMerchantAcrossAccounts_ShowsBothAccountNames()
    {
        var amex = await CreateAccountAsync();
        var checking = (await Context.Accounts.AddAsync(new Account { Name = "Wells Fargo Checking", Type = AccountType.Checking })).Entity;
        await Context.SaveChangesAsync();
        Context.BankTransactions.AddRange(
            new BankTransaction { AccountId = amex.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "APPLE.COM/BILL", Amount = -0.99m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow },
            new BankTransaction { AccountId = checking.Id, TransactionDate = new DateOnly(2026, 7, 2), Description = "APPLE.COM/BILL", Amount = -9.99m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var groups = await _sut.GetPendingTransactionGroupsAsync(Context);

        var apple = groups.Single(g => g.SuggestedPattern == "APPLE.COM/BILL");
        Assert.Equal("Wells Fargo Checking, Amex", apple.AccountName);
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

    [Fact]
    public async Task GetPendingAmazonItemGroupsAsync_NeedsReviewItems_AreNeverGroupedTogether()
    {
        // Real bug this guards against: three unrelated orders all fell back to the exact
        // same "(Item details unavailable...)" placeholder title, and title-based grouping
        // collapsed them into one row with a combined total - hiding that they're three
        // different real orders with three different real amounts and dates.
        const string placeholder = "(Item details unavailable in email - check Amazon order page)";
        Context.AmazonOrderItems.AddRange(
            new AmazonOrderItem { OrderId = "113-1132648-3403446", OrderDate = new DateOnly(2025, 7, 17), ItemTitle = placeholder, Price = 22.00m, Quantity = 1, NeedsReview = true, CreatedAt = DateTimeOffset.UtcNow },
            new AmazonOrderItem { OrderId = "112-9103180-2234648", OrderDate = new DateOnly(2025, 6, 16), ItemTitle = placeholder, Price = 25.78m, Quantity = 1, NeedsReview = true, CreatedAt = DateTimeOffset.UtcNow },
            new AmazonOrderItem { OrderId = "112-5728819-3317013", OrderDate = new DateOnly(2025, 7, 12), ItemTitle = placeholder, Price = 0.00m, Quantity = 1, NeedsReview = true, CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var groups = await _sut.GetPendingAmazonItemGroupsAsync(Context);

        Assert.Equal(3, groups.Count); // never collapsed into one
        Assert.All(groups, g => Assert.Single(g.ItemIds));
        Assert.All(groups, g => Assert.True(g.NeedsReview));
        var found22 = groups.Single(g => g.TotalPrice == 22.00m);
        Assert.Equal("113-1132648-3403446", found22.OrderId);
        Assert.Equal(new DateOnly(2025, 7, 17), found22.SampleDate);
    }

    [Fact]
    public async Task GetPendingAmazonItemGroupsAsync_NonNeedsReviewItems_StillGroupByTitle()
    {
        Context.AmazonOrderItems.AddRange(
            new AmazonOrderItem { OrderId = "1", OrderDate = new DateOnly(2026, 7, 1), ItemTitle = "Qunol Ultra CoQ10", Price = 30m, Quantity = 1, NeedsReview = false, CreatedAt = DateTimeOffset.UtcNow },
            new AmazonOrderItem { OrderId = "2", OrderDate = new DateOnly(2026, 7, 5), ItemTitle = "Qunol Ultra CoQ10", Price = 32m, Quantity = 1, NeedsReview = false, CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var groups = await _sut.GetPendingAmazonItemGroupsAsync(Context);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.ItemIds.Count);
        Assert.False(group.NeedsReview);
        Assert.Null(group.OrderId); // more than one real order in the group - no single order id applies
    }

    [Fact]
    public async Task GetPendingTransactionGroupsAsync_ExcludesDismissedTransactions()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();
        Context.BankTransactions.AddRange(
            new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "PAYMENTUS-SERVICE", Amount = -1.99m, ImportSource = "Test", Dismissed = true, CreatedAt = DateTimeOffset.UtcNow },
            new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 2), Description = "PUBLIX", Amount = -40m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var groups = await _sut.GetPendingTransactionGroupsAsync(Context);

        var group = Assert.Single(groups);
        Assert.Equal("PUBLIX", group.SuggestedPattern);
    }

    [Fact]
    public async Task GetPendingAmazonItemGroupsAsync_ExcludesDismissedItems()
    {
        Context.AmazonOrderItems.AddRange(
            new AmazonOrderItem { OrderId = "1", OrderDate = new DateOnly(2026, 7, 1), ItemTitle = "Dismissed Thing", Price = 12m, Quantity = 1, Dismissed = true, CreatedAt = DateTimeOffset.UtcNow },
            new AmazonOrderItem { OrderId = "2", OrderDate = new DateOnly(2026, 7, 2), ItemTitle = "Visible Thing", Price = 8m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow });
        await Context.SaveChangesAsync();

        var groups = await _sut.GetPendingAmazonItemGroupsAsync(Context);

        var group = Assert.Single(groups);
        Assert.Equal("Visible Thing", group.ItemTitle);
    }

    [Fact]
    public async Task DismissTransactionsAsync_MarksThemDismissed_WithoutCategorizingThem()
    {
        var account = await CreateAccountAsync();
        await Context.SaveChangesAsync();
        var transaction = new BankTransaction { AccountId = account.Id, TransactionDate = new DateOnly(2026, 7, 1), Description = "PAYMENTUS-SERVICE", Amount = -1.99m, ImportSource = "Test", CreatedAt = DateTimeOffset.UtcNow };
        Context.BankTransactions.Add(transaction);
        await Context.SaveChangesAsync();

        await _sut.DismissTransactionsAsync(Context, [transaction.Id]);

        Assert.True(transaction.Dismissed);
        Assert.Null(transaction.CategoryId);
    }

    [Fact]
    public async Task DismissAmazonItemsAsync_MarksThemDismissed_WithoutCategorizingThem()
    {
        var item = new AmazonOrderItem { OrderId = "1", OrderDate = new DateOnly(2026, 7, 1), ItemTitle = "Mystery Item", Price = 12m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow };
        Context.AmazonOrderItems.Add(item);
        await Context.SaveChangesAsync();

        await _sut.DismissAmazonItemsAsync(Context, [item.Id]);

        Assert.True(item.Dismissed);
        Assert.Null(item.CategoryId);
    }
}
