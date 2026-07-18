using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categories;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class CategoriesTests : BunitContext
{
    private class FakeCategoriesPageProvider : ICategoriesPageProvider
    {
        public List<CategoryRow> Rows { get; set; } = [];
        public List<AccountOption> Accounts { get; set; } = [];

        public string? LastCreatedName { get; private set; }
        public string? LastCreatedFundingStrategy { get; private set; }
        public BudgetInput? LastCreatedBudget { get; private set; }
        public AccountPaymentInput? LastCreatedAccountPayment { get; private set; }

        public int? LastUpdatedId { get; private set; }
        public string? LastUpdatedName { get; private set; }
        public string? LastUpdatedFundingStrategy { get; private set; }
        public BudgetInput? LastUpdatedBudget { get; private set; }
        public AccountPaymentInput? LastUpdatedAccountPayment { get; private set; }

        public int? LastDeactivatedId { get; private set; }
        public int? LastReactivatedId { get; private set; }

        public Task<CategoriesPageData> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CategoriesPageData { Categories = Rows, Accounts = Accounts });

        public Task CreateCategoryAsync(string name, string fundingStrategy, BudgetInput? budget = null, AccountPaymentInput? accountPayment = null, CancellationToken cancellationToken = default)
        {
            LastCreatedName = name;
            LastCreatedFundingStrategy = fundingStrategy;
            LastCreatedBudget = budget;
            LastCreatedAccountPayment = accountPayment;
            return Task.CompletedTask;
        }

        public Task UpdateCategoryAsync(int categoryId, string name, string fundingStrategy, BudgetInput? budget = null, AccountPaymentInput? accountPayment = null, CancellationToken cancellationToken = default)
        {
            LastUpdatedId = categoryId;
            LastUpdatedName = name;
            LastUpdatedFundingStrategy = fundingStrategy;
            LastUpdatedBudget = budget;
            LastUpdatedAccountPayment = accountPayment;
            return Task.CompletedTask;
        }

        public Task DeactivateCategoryAsync(int categoryId, CancellationToken cancellationToken = default)
        {
            LastDeactivatedId = categoryId;
            return Task.CompletedTask;
        }

        public Task ReactivateCategoryAsync(int categoryId, CancellationToken cancellationToken = default)
        {
            LastReactivatedId = categoryId;
            return Task.CompletedTask;
        }
    }

    private static FakeCategoriesPageProvider MakeProvider() => new()
    {
        Rows =
        [
            new CategoryRow
            {
                Id = 1, Name = "Groceries", IsActive = true, FundingStrategy = FundingStrategies.PayInFullAmex,
                BudgetAmount = 450m, BudgetFrequency = Frequency.Weekly, BudgetDirection = Direction.Expense
            },
            new CategoryRow { Id = 2, Name = "Off-Budget/Misc", IsActive = true, FundingStrategy = FundingStrategies.None },
            new CategoryRow { Id = 3, Name = "Discontinued Thing", IsActive = false, FundingStrategy = FundingStrategies.None },
            new CategoryRow
            {
                Id = 4, Name = "Truist Mortgage", IsActive = true, FundingStrategy = FundingStrategies.Direct,
                BudgetAmount = 2681.22m, BudgetFrequency = Frequency.Monthly, BudgetDirection = Direction.Expense,
                BudgetAnchor = new DateOnly(2026, 1, 4), BudgetAccountId = 10
            },
            new CategoryRow
            {
                Id = 5, Name = "Discover Payment", IsActive = true, FundingStrategy = FundingStrategies.AccountPayment,
                LinkedAccountId = 20, LinkedAccountType = AccountType.Debt, LinkedAccountMinPayment = 173m, LinkedAccountPaymentDueDay = 3
            },
            new CategoryRow
            {
                Id = 6, Name = "Amex Payment", IsActive = true, FundingStrategy = FundingStrategies.AccountPayment,
                LinkedAccountId = 21, LinkedAccountType = AccountType.ActiveSpending, LinkedAccountExtraPayment = 1100m,
                LinkedAccountPaymentDueDay = 20, LinkedAccountStatementCloseDay = 26
            },
            new CategoryRow { Id = 7, Name = "New Card Payment", IsActive = true, FundingStrategy = FundingStrategies.AccountPayment }
        ],
        Accounts = [new AccountOption { Id = 10, Name = "Wells Fargo Checking" }]
    };

    [Fact]
    public void Categories_RendersListWithoutAnOpenFormInitially()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();

        Assert.Contains("Groceries", cut.Markup);
        Assert.Contains("Off-Budget/Misc", cut.Markup);
        Assert.DoesNotContain("id=\"detail-name\"", cut.Markup);
    }

    [Fact]
    public void Categories_ListShowsDirectionAmountAndFrequency_ForCategoriesThatHaveABudget()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        var groceriesRow = cut.Find("#category-row-1");

        Assert.Contains("Expense", groceriesRow.InnerHtml);
        Assert.Contains("450", groceriesRow.InnerHtml);
        Assert.Contains("Weekly", groceriesRow.InnerHtml);
    }

    [Fact]
    public void Categories_RightAlignsTheAmountColumn()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();

        var amountHeader = cut.FindAll("th").Single(h => h.TextContent == "Amount");
        Assert.Equal("text-right", amountHeader.GetAttribute("class"));
    }

    [Fact]
    public void Categories_ListShowsMinPlusExtraPayment_ForADebtAccountPaymentCategory()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        var discoverRow = cut.Find("#category-row-5"); // Discover Payment: MinPayment=173, no ExtraPayment

        Assert.Contains("Expense", discoverRow.InnerHtml);
        Assert.Contains("173.00", discoverRow.InnerHtml);
        Assert.Contains("Monthly", discoverRow.InnerHtml);
    }

    [Fact]
    public void Categories_ListShowsExtraPaymentOnly_ForAnActiveSpendingAccountPaymentCategory()
    {
        // Never folds in qualifying-spend categories' budgets (e.g. Groceries) - those
        // already have their own rows on this list, so including them here would
        // double-count if this column is ever summed.
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        var amexRow = cut.Find("#category-row-6"); // Amex Payment: no MinPayment, ExtraPayment=1100

        Assert.Contains("Expense", amexRow.InnerHtml);
        Assert.Contains(1100m.ToString("N2"), amexRow.InnerHtml);
        Assert.Contains("Monthly", amexRow.InnerHtml);
    }

    [Fact]
    public void Categories_ListShowsBlankBudgetColumns_ForAnAccountPaymentCategoryWithNoLinkedAccount()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        var newCardRow = cut.Find("#category-row-7"); // New Card Payment: no linked account yet

        Assert.DoesNotContain("Expense", newCardRow.InnerHtml);
        Assert.DoesNotContain("Monthly", newCardRow.InnerHtml);
    }

    [Fact]
    public void Categories_ListShowsBlankBudgetColumns_ForCategoriesWithNoBudget()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        var offBudgetRow = cut.Find("#category-row-2");

        Assert.DoesNotContain("Expense", offBudgetRow.InnerHtml);
        Assert.DoesNotContain("Income", offBudgetRow.InnerHtml);
    }

    [Fact]
    public void ClickingARow_PopulatesTheDetailFormWithThatCategorysValues()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-1").Click();

        Assert.Equal("Groceries", cut.Find("#detail-name").GetAttribute("value"));
        Assert.Equal(FundingStrategies.PayInFullAmex,
            cut.Find("#detail-funding-strategy option[selected]").GetAttribute("value"));
    }

    [Fact]
    public void EditingAndSaving_CallsUpdateWithNameAndFundingStrategyTogether()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-2").Click(); // Off-Budget/Misc: not funded
        cut.Find("#detail-name").Change("Off-Budget & Misc");
        cut.Find("#detail-funding-strategy").Change(FundingStrategies.PayInFullAmex);
        cut.Find("#detail-save").Click();

        Assert.Equal(2, provider.LastUpdatedId);
        Assert.Equal("Off-Budget & Misc", provider.LastUpdatedName);
        Assert.Equal(FundingStrategies.PayInFullAmex, provider.LastUpdatedFundingStrategy);
    }

    [Fact]
    public void NewCategoryButton_OpensABlankFormThatCreatesOnSave()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#new-category-button").Click();
        cut.Find("#detail-name").Change("Home Improvement");
        cut.Find("#detail-save").Click();

        Assert.Equal("Home Improvement", provider.LastCreatedName);
        Assert.Equal(FundingStrategies.None, provider.LastCreatedFundingStrategy); // left at default
    }

    [Fact]
    public void SelectingAnActiveCategory_ShowsDeactivateNotReactivate()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-1").Click();
        cut.Find("#detail-deactivate").Click();

        Assert.Equal(1, provider.LastDeactivatedId);
    }

    [Fact]
    public void SelectingAnInactiveCategory_ShowsReactivateNotDeactivate()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-3").Click();
        cut.Find("#detail-reactivate").Click();

        Assert.Equal(3, provider.LastReactivatedId);
    }

    [Fact]
    public void FilteringByName_HidesNonMatchingRows()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-filter").Input("groc");

        Assert.Contains("Groceries", cut.Markup);
        Assert.DoesNotContain("Off-Budget/Misc", cut.Markup);
    }

    [Fact]
    public void SelectingANoneStrategyCategory_HidesAllBudgetFields()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-2").Click(); // Off-Budget/Misc: None

        Assert.Empty(cut.FindAll("#detail-amount"));
    }

    [Fact]
    public void SelectingADirectCategory_PopulatesTheDirectBudgetFields()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-4").Click(); // Truist Mortgage: Direct

        Assert.Equal("2681.22", cut.Find("#detail-amount").GetAttribute("value"));
        Assert.Equal("2026-01-04", cut.Find("#detail-anchor").GetAttribute("value"));
        Assert.Equal("10", cut.Find("#detail-account option[selected]").GetAttribute("value"));
    }

    [Fact]
    public void SavingADirectCategory_PassesAllFiveBudgetFieldsTogether()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-4").Click();
        cut.Find("#detail-amount").Change("2750.00");
        cut.Find("#detail-save").Click();

        Assert.Equal(4, provider.LastUpdatedId);
        Assert.NotNull(provider.LastUpdatedBudget);
        Assert.Equal(2750.00m, provider.LastUpdatedBudget!.Amount);
        Assert.Equal(Frequency.Monthly, provider.LastUpdatedBudget.Frequency);
        Assert.Equal(Direction.Expense, provider.LastUpdatedBudget.Direction);
        Assert.Equal(new DateOnly(2026, 1, 4), provider.LastUpdatedBudget.Anchor);
        Assert.Equal(10, provider.LastUpdatedBudget.AccountId);
    }

    [Fact]
    public void SavingANoneStrategyCategory_PassesNoBudgetInput()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-2").Click(); // Off-Budget/Misc: None
        cut.Find("#detail-save").Click();

        Assert.Null(provider.LastUpdatedBudget);
    }

    [Fact]
    public void SelectingAnAccountPaymentCategory_ShowsNoEditableAmountField()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-5").Click(); // Discover Payment: account_payment

        Assert.Empty(cut.FindAll("#detail-amount"));
    }

    [Fact]
    public void SelectingADebtAccountPaymentCategory_ShowsMinPaymentButNotStatementCloseDay()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-5").Click(); // Discover Payment: linked to a Debt account

        Assert.Equal("173", cut.Find("#detail-min-payment").GetAttribute("value"));
        Assert.Equal("3", cut.Find("#detail-payment-due-day").GetAttribute("value"));
        Assert.Empty(cut.FindAll("#detail-statement-close-day"));
    }

    [Fact]
    public void SelectingAnActiveSpendingAccountPaymentCategory_ShowsStatementCloseDayButNotMinPayment()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-6").Click(); // Amex Payment: linked to an ActiveSpending account

        Assert.Equal("26", cut.Find("#detail-statement-close-day").GetAttribute("value"));
        Assert.Equal("1100", cut.Find("#detail-extra-payment").GetAttribute("value"));
        Assert.Empty(cut.FindAll("#detail-min-payment"));
    }

    [Fact]
    public void SelectingAnAccountPaymentCategoryWithNoLinkedAccount_ShowsAFallbackMessage()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-7").Click(); // New Card Payment: no linked account yet

        Assert.Empty(cut.FindAll("#detail-min-payment"));
        Assert.Empty(cut.FindAll("#detail-extra-payment"));
        Assert.Contains("no linked account", cut.Markup);
    }

    [Fact]
    public void SavingADebtAccountPaymentCategory_PassesAccountPaymentInputNotBudgetInput()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-5").Click();
        cut.Find("#detail-min-payment").Change("180");
        cut.Find("#detail-payment-due-day").Change("5");
        cut.Find("#detail-save").Click();

        Assert.Null(provider.LastUpdatedBudget);
        Assert.NotNull(provider.LastUpdatedAccountPayment);
        Assert.Equal(180m, provider.LastUpdatedAccountPayment!.MinPayment);
        Assert.Equal(5, provider.LastUpdatedAccountPayment.PaymentDueDay);
        Assert.Null(provider.LastUpdatedAccountPayment.StatementCloseDay);
    }

    [Fact]
    public void ChoosingDirectStrategyOnANewCategory_ShowsTheBudgetFieldsForEntry()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#new-category-button").Click();
        cut.Find("#detail-funding-strategy").Change(FundingStrategies.Direct);

        Assert.NotNull(cut.Find("#detail-amount"));
        Assert.NotNull(cut.Find("#detail-account"));
    }

    [Fact]
    public void SelectingAPayInFullAmexCategory_ShowsAmountAndFrequencyButNotDirectionAnchorOrAccount()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-1").Click(); // Groceries: pay-in-full-amex

        Assert.Equal("450", cut.Find("#detail-amount").GetAttribute("value"));
        Assert.NotNull(cut.Find("#detail-frequency"));
        Assert.Empty(cut.FindAll("#detail-direction"));
        Assert.Empty(cut.FindAll("#detail-anchor"));
        Assert.Empty(cut.FindAll("#detail-account"));
    }

    [Fact]
    public void SavingAPayInFullAmexCategory_PassesOnlyAmountAndFrequency()
    {
        var provider = MakeProvider();
        Services.AddSingleton<ICategoriesPageProvider>(provider);

        var cut = Render<Categories>();
        cut.Find("#category-row-1").Click(); // Groceries
        cut.Find("#detail-amount").Change("500");
        cut.Find("#detail-frequency").Change(nameof(Frequency.Weekly));
        cut.Find("#detail-save").Click();

        Assert.NotNull(provider.LastUpdatedBudget);
        Assert.Equal(500m, provider.LastUpdatedBudget!.Amount);
        Assert.Equal(Frequency.Weekly, provider.LastUpdatedBudget.Frequency);
        Assert.Equal(Direction.Expense, provider.LastUpdatedBudget.Direction);
        Assert.Null(provider.LastUpdatedBudget.Anchor);
        Assert.Null(provider.LastUpdatedBudget.AccountId);
    }
}
