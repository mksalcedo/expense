using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Accounts;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class AccountsTests : BunitContext
{
    private class FakeAccountsPageProvider : IAccountsPageProvider
    {
        public List<AccountRow> Rows { get; set; } = [];

        public string? LastCreatedName { get; private set; }
        public AccountType? LastCreatedType { get; private set; }
        public decimal? LastCreatedMinPayment { get; private set; }
        public decimal? LastCreatedExtraPayment { get; private set; }
        public int? LastCreatedPaymentDueDay { get; private set; }
        public int? LastCreatedStatementCloseDay { get; private set; }

        public int? LastUpdatedId { get; private set; }
        public string? LastUpdatedName { get; private set; }
        public decimal? LastUpdatedMinPayment { get; private set; }
        public decimal? LastUpdatedExtraPayment { get; private set; }
        public int? LastUpdatedPaymentDueDay { get; private set; }
        public int? LastUpdatedStatementCloseDay { get; private set; }

        public int? LastDeactivatedId { get; private set; }
        public int? LastReactivatedId { get; private set; }

        public Task<AccountsPageData> GetAccountsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccountsPageData { Accounts = Rows });

        public Task CreateAccountAsync(string name, AccountType type, decimal? minPayment, decimal? extraPayment,
            int? paymentDueDay, int? statementCloseDay, CancellationToken cancellationToken = default)
        {
            LastCreatedName = name;
            LastCreatedType = type;
            LastCreatedMinPayment = minPayment;
            LastCreatedExtraPayment = extraPayment;
            LastCreatedPaymentDueDay = paymentDueDay;
            LastCreatedStatementCloseDay = statementCloseDay;
            return Task.CompletedTask;
        }

        public Task UpdateAccountAsync(int accountId, string name, decimal? minPayment, decimal? extraPayment,
            int? paymentDueDay, int? statementCloseDay, CancellationToken cancellationToken = default)
        {
            LastUpdatedId = accountId;
            LastUpdatedName = name;
            LastUpdatedMinPayment = minPayment;
            LastUpdatedExtraPayment = extraPayment;
            LastUpdatedPaymentDueDay = paymentDueDay;
            LastUpdatedStatementCloseDay = statementCloseDay;
            return Task.CompletedTask;
        }

        public Task DeactivateAccountAsync(int accountId, CancellationToken cancellationToken = default)
        {
            LastDeactivatedId = accountId;
            return Task.CompletedTask;
        }

        public Task ReactivateAccountAsync(int accountId, CancellationToken cancellationToken = default)
        {
            LastReactivatedId = accountId;
            return Task.CompletedTask;
        }
    }

    private static FakeAccountsPageProvider MakeProvider() => new()
    {
        Rows =
        [
            new AccountRow { Id = 1, Name = "Wells Fargo Checking", Type = AccountType.Checking, IsActive = true },
            new AccountRow
            {
                Id = 2, Name = "Amex", Type = AccountType.ActiveSpending, IsActive = true,
                ExtraPayment = 1100m, PaymentDueDay = 20, StatementCloseDay = 26
            },
            new AccountRow
            {
                Id = 3, Name = "Discover", Type = AccountType.Debt, IsActive = true,
                MinPayment = 173m, PaymentDueDay = 3
            },
            new AccountRow { Id = 4, Name = "SoFi (Paid Off 2026)", Type = AccountType.Debt, IsActive = false, MinPayment = 1084.53m, PaymentDueDay = 20 }
        ]
    };

    [Fact]
    public void Accounts_RendersListWithoutAnOpenFormInitially()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();

        Assert.Contains("Wells Fargo Checking", cut.Markup);
        Assert.Contains("Discover", cut.Markup);
        Assert.DoesNotContain("id=\"detail-name\"", cut.Markup);
    }

    [Fact]
    public void ClickingADebtAccountRow_PopulatesTheDetailFormWithMinPaymentAndPaymentDueDay()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#account-row-3").Click();

        Assert.Equal("Discover", cut.Find("#detail-name").GetAttribute("value"));
        Assert.Equal("173", cut.Find("#detail-min-payment").GetAttribute("value"));
        Assert.Equal("3", cut.Find("#detail-payment-due-day").GetAttribute("value"));
        Assert.Empty(cut.FindAll("#detail-statement-close-day")); // Debt accounts don't show statement close day
    }

    [Fact]
    public void ClickingAmex_ShowsStatementCloseDayButNotMinPayment()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#account-row-2").Click();

        Assert.Equal("26", cut.Find("#detail-statement-close-day").GetAttribute("value"));
        Assert.Equal("1100", cut.Find("#detail-extra-payment").GetAttribute("value"));
        Assert.Empty(cut.FindAll("#detail-min-payment")); // ActiveSpending accounts don't show min payment
    }

    [Fact]
    public void ClickingChecking_ShowsNoPaymentFieldsAtAll()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#account-row-1").Click();

        Assert.Empty(cut.FindAll("#detail-min-payment"));
        Assert.Empty(cut.FindAll("#detail-extra-payment"));
        Assert.Empty(cut.FindAll("#detail-payment-due-day"));
        Assert.Empty(cut.FindAll("#detail-statement-close-day"));
    }

    [Fact]
    public void EditingADebtAccount_SavesNameAndPaymentFieldsTogether()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#account-row-3").Click();
        cut.Find("#detail-min-payment").Change("180");
        cut.Find("#detail-payment-due-day").Change("5");
        cut.Find("#detail-save").Click();

        Assert.Equal(3, provider.LastUpdatedId);
        Assert.Equal("Discover", provider.LastUpdatedName);
        Assert.Equal(180m, provider.LastUpdatedMinPayment);
        Assert.Equal(5, provider.LastUpdatedPaymentDueDay);
    }

    [Fact]
    public void NewAccountButton_OpensABlankFormWithATypeSelector()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#new-account-button").Click();

        Assert.NotNull(cut.Find("#detail-type"));
    }

    [Fact]
    public void CreatingANewDebtAccount_CallsCreateWithTypeAndPaymentFields()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#new-account-button").Click();
        cut.Find("#detail-name").Change("Capital One");
        cut.Find("#detail-type").Change(nameof(AccountType.Debt));
        cut.Find("#detail-min-payment").Change("100");
        cut.Find("#detail-payment-due-day").Change("15");
        cut.Find("#detail-save").Click();

        Assert.Equal("Capital One", provider.LastCreatedName);
        Assert.Equal(AccountType.Debt, provider.LastCreatedType);
        Assert.Equal(100m, provider.LastCreatedMinPayment);
        Assert.Equal(15, provider.LastCreatedPaymentDueDay);
    }

    [Fact]
    public void SelectingAnActiveAccount_ShowsDeactivateNotReactivate()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#account-row-3").Click();
        cut.Find("#detail-deactivate").Click();

        Assert.Equal(3, provider.LastDeactivatedId);
    }

    [Fact]
    public void SelectingAnInactiveAccount_ShowsReactivateNotDeactivate()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#account-row-4").Click();
        cut.Find("#detail-reactivate").Click();

        Assert.Equal(4, provider.LastReactivatedId);
    }

    [Fact]
    public void FilteringByName_HidesNonMatchingRows()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#account-filter").Input("disc");

        Assert.Contains("Discover", cut.Markup);
        Assert.DoesNotContain("Wells Fargo Checking", cut.Markup);
    }
}
