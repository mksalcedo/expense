using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Accounts;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class AccountsTests : BunitContext
{
    // Mutates Rows on write, like a real backing store, so a post-save re-fetch (which the
    // component always does) reflects the change - needed to test the new "stay open and show
    // the saved data" behavior, not just "was the right method called with the right args".
    private class FakeAccountsPageProvider : IAccountsPageProvider
    {
        public List<AccountRow> Rows { get; set; } = [];
        public int NextCreatedId { get; set; } = 100;

        public string? LastCreatedName { get; private set; }
        public AccountType? LastCreatedType { get; private set; }
        public decimal? LastCreatedMinPayment { get; private set; }
        public decimal? LastCreatedExtraPayment { get; private set; }
        public int? LastCreatedPaymentDueDay { get; private set; }
        public int? LastCreatedStatementCloseDay { get; private set; }
        public decimal? LastCreatedApr { get; private set; }
        public DateOnly? LastCreatedPaymentStartDate { get; private set; }

        public int? LastUpdatedId { get; private set; }
        public string? LastUpdatedName { get; private set; }
        public decimal? LastUpdatedMinPayment { get; private set; }
        public decimal? LastUpdatedExtraPayment { get; private set; }
        public int? LastUpdatedPaymentDueDay { get; private set; }
        public int? LastUpdatedStatementCloseDay { get; private set; }
        public decimal? LastUpdatedApr { get; private set; }
        public DateOnly? LastUpdatedPaymentStartDate { get; private set; }

        public int? LastDeactivatedId { get; private set; }
        public int? LastReactivatedId { get; private set; }

        public int? LastBalanceAccountId { get; private set; }
        public DateOnly? LastBalanceAsOfDate { get; private set; }
        public decimal? LastBalanceAmount { get; private set; }

        public Task<AccountsPageData> GetAccountsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccountsPageData { Accounts = Rows });

        public Task<int> CreateAccountAsync(string name, AccountType type, decimal? minPayment, decimal? extraPayment,
            int? paymentDueDay, int? statementCloseDay, decimal? apr, DateOnly? paymentStartDate = null, CancellationToken cancellationToken = default)
        {
            LastCreatedName = name;
            LastCreatedType = type;
            LastCreatedMinPayment = minPayment;
            LastCreatedExtraPayment = extraPayment;
            LastCreatedPaymentDueDay = paymentDueDay;
            LastCreatedStatementCloseDay = statementCloseDay;
            LastCreatedApr = apr;
            LastCreatedPaymentStartDate = paymentStartDate;

            var id = NextCreatedId;
            Rows.Add(new AccountRow
            {
                Id = id, Name = name, Type = type, IsActive = true,
                MinPayment = minPayment, ExtraPayment = extraPayment,
                PaymentDueDay = paymentDueDay, StatementCloseDay = statementCloseDay, Apr = apr, PaymentStartDate = paymentStartDate
            });
            return Task.FromResult(id);
        }

        public Task UpdateAccountAsync(int accountId, string name, decimal? minPayment, decimal? extraPayment,
            int? paymentDueDay, int? statementCloseDay, decimal? apr, DateOnly? paymentStartDate = null, CancellationToken cancellationToken = default)
        {
            LastUpdatedId = accountId;
            LastUpdatedName = name;
            LastUpdatedMinPayment = minPayment;
            LastUpdatedExtraPayment = extraPayment;
            LastUpdatedPaymentDueDay = paymentDueDay;
            LastUpdatedStatementCloseDay = statementCloseDay;
            LastUpdatedApr = apr;
            LastUpdatedPaymentStartDate = paymentStartDate;

            var row = Rows.Single(r => r.Id == accountId);
            row.Name = name;
            row.MinPayment = minPayment;
            row.ExtraPayment = extraPayment;
            row.PaymentDueDay = paymentDueDay;
            row.StatementCloseDay = statementCloseDay;
            row.Apr = apr;
            row.PaymentStartDate = paymentStartDate;
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

        public Task UpdateBalanceAsync(int accountId, DateOnly asOfDate, decimal balance, CancellationToken cancellationToken = default)
        {
            LastBalanceAccountId = accountId;
            LastBalanceAsOfDate = asOfDate;
            LastBalanceAmount = balance;

            var row = Rows.Single(r => r.Id == accountId);
            row.LatestBalance = balance;
            row.LatestBalanceAsOfDate = asOfDate;
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
                // Stored negative = amount owed, matching the accounting/SimpleFin convention -
                // the UI displays this as a positive magnitude (see FormatBalance).
                Id = 3, Name = "Discover", Type = AccountType.Debt, IsActive = true,
                MinPayment = 173m, PaymentDueDay = 3, Apr = 24.99m,
                LatestBalance = -5452.10m, LatestBalanceAsOfDate = new DateOnly(2026, 7, 15)
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
    public void ClickingADebtAccountRow_PopulatesAprAndShowsTheLatestBalance()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#account-row-3").Click();

        Assert.Equal("24.99", cut.Find("#detail-apr").GetAttribute("value"));
        Assert.Contains("5,452.10", cut.Markup);
        Assert.Contains("07/15/2026", cut.Markup);
    }

    [Fact]
    public void ClickingAnAccountWithNoBalanceRecordedYet_ShowsAFriendlyMessageInstead()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#account-row-2").Click(); // Amex - no LatestBalance set in MakeProvider

        Assert.Contains("No balance recorded yet", cut.Markup);
    }

    [Fact]
    public void ClickingSave_WithAPlainBalanceEntered_StoresItAsNegative_MeaningOwed()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#account-row-3").Click();
        cut.Find("#detail-balance-date").Change("2026-07-30");
        cut.Find("#detail-balance-amount").Change("5000.25");
        cut.Find("#detail-save").Click();

        Assert.Equal(3, provider.LastBalanceAccountId);
        Assert.Equal(new DateOnly(2026, 7, 30), provider.LastBalanceAsOfDate);
        Assert.Equal(-5000.25m, provider.LastBalanceAmount);
    }

    [Fact]
    public void ClickingSave_WithCreditBalanceChecked_StoresItAsPositive()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#account-row-3").Click();
        cut.Find("#detail-balance-amount").Change("50.00");
        cut.Find("#detail-balance-is-credit").Change(true);
        cut.Find("#detail-save").Click();

        Assert.Equal(50.00m, provider.LastBalanceAmount);
    }

    [Fact]
    public void ClickingADebtAccountRow_PrefillsTheBalanceFieldAsAPositiveAmount_EvenThoughItIsStoredNegative()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#account-row-3").Click(); // Discover: LatestBalance = -5452.10m in the fixture

        Assert.Equal("5452.10", cut.Find("#detail-balance-amount").GetAttribute("value"));
        Assert.False(cut.Find("#detail-balance-is-credit").HasAttribute("checked"));
    }

    [Fact]
    public void AccountList_ShowsACreditBalance_WithAPositiveAmountAndACrIndicator()
    {
        var provider = new FakeAccountsPageProvider
        {
            Rows = [new AccountRow { Id = 5, Name = "Overpaid Card", Type = AccountType.Debt, IsActive = true, LatestBalance = 50.00m }]
        };
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();

        var cells = cut.Find("#account-row-5").QuerySelectorAll("td").Select(td => td.TextContent.Trim()).ToList();
        Assert.Equal("50.00 CR", cells[6]);
    }

    [Fact]
    public void ClickingSave_KeepsTheFormOpenAndShowsASavedConfirmation()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#account-row-3").Click();
        cut.Find("#detail-min-payment").Change("180");
        cut.Find("#detail-save").Click();

        Assert.Equal("Discover", cut.Find("#detail-name").GetAttribute("value"));
        Assert.Equal("180", cut.Find("#detail-min-payment").GetAttribute("value"));
        Assert.NotEmpty(cut.FindAll("#save-confirmation"));
    }

    [Fact]
    public void SelectingADifferentRow_ClearsAnyPreviousSavedConfirmation()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#account-row-3").Click();
        cut.Find("#detail-save").Click();
        Assert.NotEmpty(cut.FindAll("#save-confirmation"));

        cut.Find("#account-row-1").Click();

        Assert.Empty(cut.FindAll("#save-confirmation"));
    }

    [Fact]
    public void CreatingANewAccount_SwitchesToEditingItAfterSaving()
    {
        var provider = MakeProvider();
        provider.NextCreatedId = 50;
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#new-account-button").Click();
        cut.Find("#detail-name").Change("Capital One");
        cut.Find("#detail-type").Change(nameof(AccountType.Debt));
        cut.Find("#detail-save").Click();

        // Editing mode shows Type as read-only text, not the Creating-only <select> - its
        // absence is how we know the form switched from Creating to Editing after the save.
        Assert.Empty(cut.FindAll("#detail-type"));
        Assert.Equal("Capital One", cut.Find("#detail-name").GetAttribute("value"));
        Assert.NotEmpty(cut.FindAll("#save-confirmation"));
    }

    [Fact]
    public void ClickingAmex_ShowsStatementCloseDayAndMinPayment()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#account-row-2").Click();

        Assert.Equal("26", cut.Find("#detail-statement-close-day").GetAttribute("value"));
        Assert.Equal("1100", cut.Find("#detail-extra-payment").GetAttribute("value"));
        // Amex doesn't drive the forecast off MinPayment (that's its own statement-cycle rule),
        // but it's still a real card with a real minimum payment worth just keeping on file.
        Assert.NotEmpty(cut.FindAll("#detail-min-payment"));
        Assert.NotEmpty(cut.FindAll("#detail-apr"));
    }

    [Fact]
    public void ClickingChecking_ShowsNoPaymentOrCreditFieldsAtAll()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#account-row-1").Click();

        Assert.Empty(cut.FindAll("#detail-min-payment"));
        Assert.Empty(cut.FindAll("#detail-extra-payment"));
        Assert.Empty(cut.FindAll("#detail-payment-due-day"));
        Assert.Empty(cut.FindAll("#detail-statement-close-day"));
        Assert.Empty(cut.FindAll("#detail-apr"));
        Assert.Empty(cut.FindAll("#detail-balance-amount"));
    }

    // Savings has no payment/APR concept at all (like Checking) but, unlike Checking, still
    // needs a balance field - it's a manually-tracked asset, not machinery-free like Checking
    // which gets its balance from live sync instead.
    [Fact]
    public void ClickingASavingsAccountRow_ShowsOnlyABalanceField_NoPaymentOrAprFields()
    {
        var provider = new FakeAccountsPageProvider
        {
            Rows = [new AccountRow
            {
                Id = 6, Name = "Emergency Fund", Type = AccountType.Savings, IsActive = true,
                LatestBalance = 1500m, LatestBalanceAsOfDate = new DateOnly(2026, 8, 1)
            }]
        };
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#account-row-6").Click();

        Assert.Empty(cut.FindAll("#detail-min-payment"));
        Assert.Empty(cut.FindAll("#detail-extra-payment"));
        Assert.Empty(cut.FindAll("#detail-payment-due-day"));
        Assert.Empty(cut.FindAll("#detail-statement-close-day"));
        Assert.Empty(cut.FindAll("#detail-apr"));
        Assert.Equal("1500", cut.Find("#detail-balance-amount").GetAttribute("value"));
    }

    // Unlike Debt, a savings balance is always a plain asset amount - there's no "owed vs
    // credit" ambiguity, so no checkbox and no sign flip on save.
    [Fact]
    public void ClickingSaveOnASavingsAccount_StoresTheBalanceAsEnteredWithNoSignFlip()
    {
        var provider = new FakeAccountsPageProvider
        {
            Rows = [new AccountRow { Id = 6, Name = "Emergency Fund", Type = AccountType.Savings, IsActive = true }]
        };
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#account-row-6").Click();
        Assert.Empty(cut.FindAll("#detail-balance-is-credit"));
        cut.Find("#detail-balance-date").Change("2026-08-03");
        cut.Find("#detail-balance-amount").Change("1500.00");
        cut.Find("#detail-save").Click();

        Assert.Equal(6, provider.LastBalanceAccountId);
        Assert.Equal(1500.00m, provider.LastBalanceAmount);
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
        cut.Find("#detail-apr").Change("22.49");
        cut.Find("#detail-save").Click();

        Assert.Equal(3, provider.LastUpdatedId);
        Assert.Equal("Discover", provider.LastUpdatedName);
        Assert.Equal(180m, provider.LastUpdatedMinPayment);
        Assert.Equal(5, provider.LastUpdatedPaymentDueDay);
        Assert.Equal(22.49m, provider.LastUpdatedApr);
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
        cut.Find("#detail-apr").Change("19.99");
        cut.Find("#detail-save").Click();

        Assert.Equal("Capital One", provider.LastCreatedName);
        Assert.Equal(AccountType.Debt, provider.LastCreatedType);
        Assert.Equal(100m, provider.LastCreatedMinPayment);
        Assert.Equal(15, provider.LastCreatedPaymentDueDay);
        Assert.Equal(19.99m, provider.LastCreatedApr);
    }

    [Fact]
    public void CreatingANewDebtAccount_WithAPaymentStartDate_PassesItThrough()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#new-account-button").Click();
        cut.Find("#detail-name").Change("BMG");
        cut.Find("#detail-type").Change(nameof(AccountType.Debt));
        cut.Find("#detail-min-payment").Change("2334.99");
        cut.Find("#detail-payment-due-day").Change("15");
        cut.Find("#detail-payment-start-date").Change("2026-09-15");
        cut.Find("#detail-save").Click();

        Assert.Equal(new DateOnly(2026, 9, 15), provider.LastCreatedPaymentStartDate);
    }

    [Fact]
    public void CreatingANewDebtAccount_WithNoPaymentStartDate_PassesNull()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#new-account-button").Click();
        cut.Find("#detail-name").Change("Ordinary Card");
        cut.Find("#detail-type").Change(nameof(AccountType.Debt));
        cut.Find("#detail-save").Click();

        Assert.Null(provider.LastCreatedPaymentStartDate);
    }

    [Fact]
    public void SelectingADebtAccount_WithAPaymentStartDate_ShowsItInTheForm()
    {
        var provider = MakeProvider();
        provider.Rows.Single(r => r.Id == 3).PaymentStartDate = new DateOnly(2026, 9, 15);
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#account-row-3").Click();

        Assert.Equal("2026-09-15", cut.Find("#detail-payment-start-date").GetAttribute("value"));
    }

    [Fact]
    public void PaymentStartDateField_IsNotShownForActiveSpendingAccounts()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        cut.Find("#new-account-button").Click();
        cut.Find("#detail-type").Change(nameof(AccountType.ActiveSpending));

        Assert.Empty(cut.FindAll("#detail-payment-start-date"));
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

    [Fact]
    public void AccountList_ShowsMinPaymentPaymentDueDayAprBalanceAndAsOfColumns()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        var cells = cut.Find("#account-row-3").QuerySelectorAll("td").Select(td => td.TextContent.Trim()).ToList();

        Assert.Equal("173.00", cells[3]);
        Assert.Equal("3", cells[4]);
        Assert.Equal("24.99%", cells[5]);
        Assert.Equal("5,452.10", cells[6]);
        Assert.Equal("07/15/2026", cells[7]);
    }

    [Fact]
    public void AccountList_ShowsDashesForAccountsWithoutCreditFields()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();
        var cells = cut.Find("#account-row-1").QuerySelectorAll("td").Select(td => td.TextContent.Trim()).ToList();

        Assert.Equal(["-", "-", "-", "-", "-"], cells.Skip(3));
    }

    [Fact]
    public void AccountList_ShowsTotalsForBalanceAndMinPayment_ExcludingInactiveAccounts()
    {
        var provider = MakeProvider();
        Services.AddSingleton<IAccountsPageProvider>(provider);

        var cut = Render<Accounts>();

        // Active accounts only: Discover (MinPayment 173, Balance 5452.10). SoFi (Paid Off) is
        // inactive - its 1084.53 MinPayment must not be counted.
        Assert.Contains("173.00", cut.Find("#total-min-payment").TextContent);
        Assert.Contains("5,452.10", cut.Find("#total-balance").TextContent);
    }
}
