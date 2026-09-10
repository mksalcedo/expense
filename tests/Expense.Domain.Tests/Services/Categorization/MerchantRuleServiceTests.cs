using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.Categorization;

public class MerchantRuleServiceTests : DatabaseTestBase
{
    private readonly MerchantRuleService _sut = new();

    private async Task<(Account Account, Category Piano, Category VenmoPayment)> SeedAsync()
    {
        var account = new Account { Name = "Checking", Type = AccountType.ActiveSpending };
        var piano = new Category { Name = "Piano" };
        var venmoPayment = new Category { Name = "Venmo Credit Card Payment" };
        Context.Accounts.Add(account);
        Context.Categories.AddRange(piano, venmoPayment);
        await Context.SaveChangesAsync();
        return (account, piano, venmoPayment);
    }

    private BankTransaction Pending(int accountId, decimal amount) => new()
    {
        AccountId = accountId, TransactionDate = new DateOnly(2026, 9, 8),
        Description = "Venmo", Amount = amount, ImportSource = "Plaid", CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Create_AddsRule_AndReappliesToMatchingPendingTransactions()
    {
        var (account, piano, _) = await SeedAsync();
        var income = Pending(account.Id, 200m);
        Context.BankTransactions.Add(income);
        await Context.SaveChangesAsync();

        var reapplied = await _sut.CreateAsync(Context, "Venmo", Direction.Income, piano.Id);

        Assert.Equal(1, reapplied);
        Assert.Equal(piano.Id, income.CategoryId);

        var rule = Assert.Single(await _sut.GetAllAsync(Context));
        Assert.Equal("Venmo", rule.MerchantPattern);
        Assert.Equal(Direction.Income, rule.Direction);
        Assert.Equal("Piano", rule.Category.Name);
    }

    [Fact]
    public async Task Create_DirectionScopedRule_LeavesOppositeSignPending()
    {
        var (account, piano, _) = await SeedAsync();
        var expense = Pending(account.Id, -50m);
        Context.BankTransactions.Add(expense);
        await Context.SaveChangesAsync();

        var reapplied = await _sut.CreateAsync(Context, "Venmo", Direction.Income, piano.Id);

        Assert.Equal(0, reapplied);
        Assert.Null(expense.CategoryId);
    }

    [Fact]
    public async Task Create_NullDirection_ReappliesToEitherSign()
    {
        var (account, piano, _) = await SeedAsync();
        var income = Pending(account.Id, 200m);
        var expense = Pending(account.Id, -50m);
        Context.BankTransactions.AddRange(income, expense);
        await Context.SaveChangesAsync();

        var reapplied = await _sut.CreateAsync(Context, "Venmo", direction: null, piano.Id);

        Assert.Equal(2, reapplied);
    }

    [Fact]
    public async Task Update_ChangesFields_AndReapplies()
    {
        var (account, piano, venmoPayment) = await SeedAsync();
        var rule = new MerchantRule { MerchantPattern = "Venmo", CategoryId = venmoPayment.Id, Direction = Direction.Expense };
        Context.MerchantRules.Add(rule);
        var income = Pending(account.Id, 200m);
        Context.BankTransactions.Add(income);
        await Context.SaveChangesAsync();

        var reapplied = await _sut.UpdateAsync(Context, rule.Id, "Venmo", Direction.Income, piano.Id);

        Assert.Equal(1, reapplied);
        Assert.Equal(piano.Id, income.CategoryId);

        var reloaded = Assert.Single(await _sut.GetAllAsync(Context));
        Assert.Equal(Direction.Income, reloaded.Direction);
        Assert.Equal(piano.Id, reloaded.CategoryId);
    }

    [Fact]
    public async Task Delete_RemovesRule()
    {
        var (_, piano, _) = await SeedAsync();
        var rule = new MerchantRule { MerchantPattern = "Venmo", CategoryId = piano.Id };
        Context.MerchantRules.Add(rule);
        await Context.SaveChangesAsync();

        await _sut.DeleteAsync(Context, rule.Id);

        Assert.Empty(await _sut.GetAllAsync(Context));
    }
}
