using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services;
using Expense.Domain.Services.Accounts;
using Expense.Domain.Services.Dashboard;
using Expense.Domain.Services.Forecast;
using Expense.Domain.Services.Ingestion.Amazon;
using Expense.Domain.Services.SpendingTracker;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class DashboardTests : BunitContext
{
    private readonly DataChangeNotifier _dataChangeNotifier = new();

    public DashboardTests()
    {
        // Dashboard embeds CashFlowChart, which imports its own JS module on first render for
        // hover support - these tests don't exercise hovering, so let unconfigured JS interop
        // calls no-op rather than configuring the module in every single test here.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IDataChangeNotifier>(_dataChangeNotifier);
    }

    private class FakeForecastResultProvider(ForecastResult result) : IForecastResultProvider
    {
        public ForecastResult Result { get; set; } = result;
        public Task<ForecastResult> GetForecastAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result);
        public Task DeferPaymentAsync(int accountId, DateOnly originalDate, DateOnly deferredToDate, string? note, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveDeferralAsync(int deferralId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ConfirmPaymentAsync(int accountId, int? categoryId, DateOnly originalDate, DateOnly effectiveDate, decimal amount, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OverridePaymentAsync(int accountId, int? categoryId, DateOnly originalDate, DateOnly effectiveDate, decimal amount, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveConfirmationAsync(int confirmationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PayPartialAmountAsync(int accountId, DateOnly originalDate, DateOnly paidDate, decimal amount, Direction direction, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemovePartialPaymentAsync(int partialPaymentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AdjustAmountAsync(int accountId, int? categoryId, DateOnly originalDate, decimal amount, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveAmountAdjustmentAsync(int adjustmentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class FakeSpendingTrackerPageProvider(SpendingTrackerPageData data) : ISpendingTrackerPageProvider
    {
        public SpendingTrackerPageData Data { get; set; } = data;
        public DateOnly? LastWeekReferenceDate { get; private set; }
        public DateOnly? LastMonthReferenceDate { get; private set; }

        public Task<SpendingTrackerPageData> GetSpendingTrackerAsync(CancellationToken cancellationToken = default) => Task.FromResult(Data);

        public Task<SpendingTrackerResult> GetWeekAsync(DateOnly referenceDate, CancellationToken cancellationToken = default)
        {
            LastWeekReferenceDate = referenceDate;
            var start = referenceDate.AddDays(-(int)referenceDate.DayOfWeek);
            return Task.FromResult(new SpendingTrackerResult { PeriodStart = start, PeriodEnd = start.AddDays(6), Categories = [], PendingAmount = 0m });
        }

        public Task<SpendingTrackerResult> GetMonthAsync(DateOnly referenceDate, CancellationToken cancellationToken = default)
        {
            LastMonthReferenceDate = referenceDate;
            var start = new DateOnly(referenceDate.Year, referenceDate.Month, 1);
            return Task.FromResult(new SpendingTrackerResult { PeriodStart = start, PeriodEnd = start.AddMonths(1).AddDays(-1), Categories = [], PendingAmount = 0m });
        }

        public Task ResetCarryoverAsync(int categoryId, bool startingNextPeriod, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<List<CategoryTransactionLine>> GetCategoryTransactionsAsync(int categoryId, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default) => Task.FromResult(new List<CategoryTransactionLine>());
    }

    // Only Dashboard.razor's narrow "sum the active Savings accounts' latest balance" read is
    // exercised here - full account management lives on Accounts.razor and is tested there.
    private class FakeAccountsPageProvider(List<AccountRow> rows) : IAccountsPageProvider
    {
        public Task<AccountsPageData> GetAccountsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AccountsPageData { Accounts = rows });
        public Task<int> CreateAccountAsync(string name, AccountType type, decimal? minPayment, decimal? extraPayment, int? paymentDueDay, int? statementCloseDay, decimal? apr, DateOnly? paymentStartDate = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAccountAsync(int accountId, string name, decimal? minPayment, decimal? extraPayment, int? paymentDueDay, int? statementCloseDay, decimal? apr, DateOnly? paymentStartDate = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeactivateAccountAsync(int accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReactivateAccountAsync(int accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateBalanceAsync(int accountId, DateOnly asOfDate, decimal balance, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // Only Dashboard.razor's narrow "did the last sync fail" read is exercised here - the
    // full sync UI (buttons, modal, issues) lives on Import Data and is tested there instead.
    private class FakeSyncStatusProvider(ImportRun? lastSimpleFinRun = null, ImportRun? lastAmazonRun = null, ImportRun? lastPlaidRun = null) : ISyncStatusProvider
    {
        public Task<ImportRun?> GetLastSimpleFinRunAsync(CancellationToken cancellationToken = default) => Task.FromResult(lastSimpleFinRun);
        public Task<ImportRun?> GetLastAmazonGmailRunAsync(CancellationToken cancellationToken = default) => Task.FromResult(lastAmazonRun);
        public Task<ImportRun?> GetLastPlaidRunAsync(CancellationToken cancellationToken = default) => Task.FromResult(lastPlaidRun);
        public Task<ImportRun> RunSimpleFinSyncAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ImportRun { Source = ImportSource.SimpleFin, RanAt = DateTimeOffset.UtcNow, Success = true });
        public Task<ImportRun> RunAmazonGmailSyncAsync(Action<SyncProgressLine>? onProgress = null, CancellationToken cancellationToken = default) => Task.FromResult(new ImportRun { Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = true });
        public Task<ImportRun> RunPlaidImportAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) => Task.FromResult(new ImportRun { Source = ImportSource.Plaid, RanAt = DateTimeOffset.UtcNow, Success = true });
        public Task<ImportRun> RunScheduledPlaidSyncAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ImportRun { Source = ImportSource.Plaid, RanAt = DateTimeOffset.UtcNow, Success = true });
        public Task<RecentRunsPage> GetRecentRunsAsync(ImportSource source, int page, int pageSize, CancellationToken cancellationToken = default) => Task.FromResult(new RecentRunsPage { Runs = [], TotalCount = 0 });
        public Task<List<SyncProgressLine>> GetRunProgressLogAsync(int importRunId, CancellationToken cancellationToken = default) => Task.FromResult(new List<SyncProgressLine>());
        public Task<List<SyncIssue>> GetActiveSyncIssuesAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<SyncIssue>());
        public Task ResolveSyncIssueAsync(int syncIssueId, string orderId, string itemTitle, decimal price, int quantity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task IgnoreSyncIssueAsync(int syncIssueId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static ForecastResult MakeForecast() => new()
    {
        StartingBalance = 6463.02m,
        Rows =
        [
            new ForecastLedgerRow { Date = new DateOnly(2026, 7, 20), Description = "Discover Payment", Amount = -150m, RunningBalance = 6313.02m },
            new ForecastLedgerRow { Date = new DateOnly(2026, 7, 31), Description = "Paycheck", Amount = 2000m, RunningBalance = 8313.02m }
        ]
    };

    private static SpendingTrackerPageData MakeSpendingTracker() => new()
    {
        Week = new SpendingTrackerResult
        {
            PeriodStart = new DateOnly(2026, 7, 12),
            PeriodEnd = new DateOnly(2026, 7, 18),
            Categories = [new CategorySpendingSummary { CategoryId = 1, CategoryName = "Groceries", Budget = 450m, Actual = 120m }],
            PendingAmount = 30m
        },
        Month = new SpendingTrackerResult
        {
            PeriodStart = new DateOnly(2026, 7, 1),
            PeriodEnd = new DateOnly(2026, 7, 31),
            Categories = [new CategorySpendingSummary { CategoryId = 1, CategoryName = "Groceries", Budget = 1956.70m, Actual = 800m }],
            PendingAmount = 60m
        }
    };

    private FakeSpendingTrackerPageProvider RegisterFakes(
        ForecastResult? forecast = null, ImportRun? lastSimpleFinRun = null, ImportRun? lastAmazonRun = null, ImportRun? lastPlaidRun = null,
        List<AccountRow>? savingsAccounts = null)
    {
        Services.AddSingleton<IForecastResultProvider>(new FakeForecastResultProvider(forecast ?? MakeForecast()));
        var spendingTracker = new FakeSpendingTrackerPageProvider(MakeSpendingTracker());
        Services.AddSingleton<ISpendingTrackerPageProvider>(spendingTracker);
        Services.AddSingleton<ISyncStatusProvider>(new FakeSyncStatusProvider(lastSimpleFinRun, lastAmazonRun, lastPlaidRun));
        Services.AddSingleton<IAccountsPageProvider>(new FakeAccountsPageProvider(savingsAccounts ?? []));
        return spendingTracker;
    }

    [Fact]
    public void Dashboard_RendersForecastSummary()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.Contains("6,463.02", cut.Markup);
        Assert.Contains("Discover Payment", cut.Markup);
    }

    // Real gap this guards (2026-08-17): every figure here only ever loaded once, in
    // OnInitializedAsync - a background scheduled sync completing while the user just sits
    // on this page (not navigating anywhere) never used to be reflected without a manual
    // refresh.
    [Fact]
    public void DataChangeNotifier_Firing_RefreshesTheDashboardsFigures_WithoutNavigatingOrReloading()
    {
        var forecastProvider = new FakeForecastResultProvider(MakeForecast());
        Services.AddSingleton<IForecastResultProvider>(forecastProvider);
        Services.AddSingleton<ISpendingTrackerPageProvider>(new FakeSpendingTrackerPageProvider(MakeSpendingTracker()));
        Services.AddSingleton<ISyncStatusProvider>(new FakeSyncStatusProvider());
        Services.AddSingleton<IAccountsPageProvider>(new FakeAccountsPageProvider([]));

        var cut = Render<Dashboard>();
        Assert.Contains("6,463.02", cut.Markup);

        // Simulates a background scheduled sync landing a new starting balance - nothing on
        // this page did anything to cause it.
        forecastProvider.Result = new ForecastResult { StartingBalance = 9999.99m, Rows = [] };
        _dataChangeNotifier.NotifyChanged();

        cut.WaitForAssertion(() => Assert.Contains("9,999.99", cut.Markup));
    }

    [Fact]
    public void CashFlow_ShowsAnExcludedRow_StruckThroughWithItsReason()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow
            {
                Date = new DateOnly(2026, 7, 20), Description = "Chase Amazon Prime Visa Payment", Amount = -357m, RunningBalance = 643m,
                AccountId = 5, OriginalDate = new DateOnly(2026, 7, 20), IsExcluded = true, ExclusionReason = ConfirmationReason.AlreadyPaid, ConfirmationId = 1,
                ResolvedDate = new DateOnly(2026, 7, 18)
            }]
        };
        RegisterFakes(forecast: forecast);

        var cut = Render<Dashboard>();

        var row = cut.FindAll("tbody tr").First(r => r.TextContent.Contains("Chase Amazon Prime Visa Payment"));
        Assert.Contains("line-through", row.GetAttribute("style") ?? "");
        Assert.Contains("AlreadyPaid", row.TextContent);
        Assert.Contains("AlreadyPaid - 07/18/2026", row.TextContent);
    }

    [Fact]
    public void CashFlow_ShowsADeferredRow_HighlightedWithItsOriginalDate()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow
            {
                Date = new DateOnly(2026, 7, 22), Description = "Amex Payment", Amount = -2000m, RunningBalance = -1000m,
                AccountId = 2, OriginalDate = new DateOnly(2026, 7, 20), IsDeferred = true, DeferralId = 1
            }]
        };
        RegisterFakes(forecast: forecast);

        var cut = Render<Dashboard>();

        var row = cut.FindAll("tbody tr").First(r => r.TextContent.Contains("Amex Payment"));
        Assert.Contains("background-color: orange", row.GetAttribute("style") ?? "");
        Assert.Contains("Originally estimated for 07/20/2026", row.TextContent);
    }

    [Fact]
    public void Dashboard_ShowsWhenTheLowestProjectedBalanceOccurs()
    {
        // MakeForecast()'s lowest running balance (6,313.02) is on the Discover Payment
        // row, 2026-07-20 - same "Occurs on" treatment as the Forecast page itself.
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.Contains("Occurs on 07/20/2026", cut.Markup);
    }

    // Purely informational context next to the scariest number on the page - the lowest
    // projected balance can read as alarming on its own when real savings exist as a buffer
    // the forecast never accounts for (savings is deliberately excluded from forecast math).
    [Fact]
    public void Dashboard_ShowsTheSavingsBalance_AlongsideTheLowestProjectedBalance()
    {
        RegisterFakes(savingsAccounts:
        [
            new AccountRow { Id = 6, Name = "Emergency Fund", Type = AccountType.Savings, IsActive = true, LatestBalance = 1500m }
        ]);

        var cut = Render<Dashboard>();

        Assert.Contains("1,500.00", cut.Markup);
        Assert.Contains("savings", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dashboard_SumsMultipleActiveSavingsAccounts_AndExcludesInactiveOnes()
    {
        RegisterFakes(savingsAccounts:
        [
            new AccountRow { Id = 6, Name = "Emergency Fund", Type = AccountType.Savings, IsActive = true, LatestBalance = 1500m },
            new AccountRow { Id = 7, Name = "Vacation Fund", Type = AccountType.Savings, IsActive = true, LatestBalance = 250m },
            new AccountRow { Id = 8, Name = "Old Savings", Type = AccountType.Savings, IsActive = false, LatestBalance = 9999m }
        ]);

        var cut = Render<Dashboard>();

        Assert.Contains("1,750.00", cut.Markup);
        Assert.DoesNotContain("9,999.00", cut.Markup);
    }

    [Fact]
    public void Dashboard_WithNoSavingsAccounts_ShowsNoSavingsLine()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.DoesNotContain("savings", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    // Spreadsheet-style summary table (label column, amount column) rather than loose
    // paragraphs, per the user's direct request - and the new computed row is the actual
    // point of tracking savings at all: what the lowest point looks like once the buffer
    // that isn't part of forecast math gets folded in.
    [Fact]
    public void Dashboard_CashFlowSummary_IsATwoColumnTable_WithALowestBalancePlusSavingsRow()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 4209.21m,
            Rows =
            [
                new ForecastLedgerRow { Date = new DateOnly(2026, 7, 30), Description = "Apple Card Payment", Amount = -25.00m, RunningBalance = 4184.21m },
                new ForecastLedgerRow { Date = new DateOnly(2027, 7, 7), Description = "Water", Amount = -193m, RunningBalance = -109.58m }
            ]
        };
        RegisterFakes(forecast: forecast, savingsAccounts:
        [
            new AccountRow { Id = 6, Name = "Emergency Fund", Type = AccountType.Savings, IsActive = true, LatestBalance = 1545.56m }
        ]);

        var cut = Render<Dashboard>();

        Assert.Equal("4,209.21", cut.Find("#starting-balance-row td:last-child").TextContent.Trim());
        Assert.Equal("-109.58", cut.Find("#lowest-balance-row td:last-child").TextContent.Trim());
        Assert.Equal("1,545.56", cut.Find("#savings-row td:last-child").TextContent.Trim());
        Assert.Equal("1,435.98", cut.Find("#lowest-balance-plus-savings-row td:last-child").TextContent.Trim());
    }

    [Fact]
    public void SpendingTables_ShowDatedTitles_NotGenericOnes()
    {
        // Fixture's Week is 2026-07-12 (Sun) - 2026-07-18 (Sat); Month is July 2026.
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.Contains("Spending for Week Ending 07/18/2026", cut.Markup);
        Assert.Contains("Spending for July, 2026", cut.Markup);
    }

    [Fact]
    public void ClickingPreviousOnWeek_FetchesTheWeekBefore_AndUpdatesTheTitle()
    {
        var fake = RegisterFakes();

        var cut = Render<Dashboard>();
        cut.Find("#spending-week-previous").Click();

        Assert.Equal(new DateOnly(2026, 7, 5), fake.LastWeekReferenceDate);
        Assert.Contains("Spending for Week Ending 07/11/2026", cut.Markup);
    }

    [Fact]
    public void ClickingNextOnWeek_FetchesTheWeekAfter_AndUpdatesTheTitle()
    {
        var fake = RegisterFakes();

        var cut = Render<Dashboard>();
        cut.Find("#spending-week-next").Click();

        Assert.Equal(new DateOnly(2026, 7, 19), fake.LastWeekReferenceDate);
        Assert.Contains("Spending for Week Ending 07/25/2026", cut.Markup);
    }

    [Fact]
    public void ClickingPreviousTwiceThenCurrentOnWeek_ReturnsToTodaysRealWeek()
    {
        var fake = RegisterFakes();

        var cut = Render<Dashboard>();
        cut.Find("#spending-week-previous").Click();
        cut.Find("#spending-week-previous").Click();
        cut.Find("#spending-week-current").Click();

        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), fake.LastWeekReferenceDate);
    }

    [Fact]
    public void ClickingPreviousOnMonth_FetchesTheMonthBefore_AndUpdatesTheTitle()
    {
        var fake = RegisterFakes();

        var cut = Render<Dashboard>();
        cut.Find("#spending-month-previous").Click();

        Assert.Equal(new DateOnly(2026, 6, 1), fake.LastMonthReferenceDate);
        Assert.Contains("Spending for June, 2026", cut.Markup);
    }

    [Fact]
    public void ClickingNextOnMonth_FetchesTheMonthAfter_AndUpdatesTheTitle()
    {
        var fake = RegisterFakes();

        var cut = Render<Dashboard>();
        cut.Find("#spending-month-next").Click();

        Assert.Equal(new DateOnly(2026, 8, 1), fake.LastMonthReferenceDate);
        Assert.Contains("Spending for August, 2026", cut.Markup);
    }

    [Fact]
    public void ClickingCurrentOnMonth_ReturnsToTodaysRealMonth()
    {
        var fake = RegisterFakes();

        var cut = Render<Dashboard>();
        cut.Find("#spending-month-next").Click();
        cut.Find("#spending-month-current").Click();

        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), fake.LastMonthReferenceDate);
    }

    [Fact]
    public void NavigatingWeekAndMonth_AreIndependentOfEachOther()
    {
        var fake = RegisterFakes();

        var cut = Render<Dashboard>();
        cut.Find("#spending-week-next").Click();

        Assert.Null(fake.LastMonthReferenceDate);
    }

    [Fact]
    public void Dashboard_RendersTheCashFlowTrendChart()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.NotEmpty(cut.FindAll("#cash-flow-chart-svg"));
        Assert.NotEmpty(cut.FindAll("#cash-flow-chart-line"));
    }

    [Fact]
    public void Dashboard_RendersThisWeeksSpending()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.Contains("Groceries", cut.Markup);
        Assert.Contains("450.00", cut.Markup);
        Assert.Contains("120.00", cut.Markup);
    }

    [Fact]
    public void Dashboard_RightAlignsAmountColumns()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        var headers = cut.FindAll("th").Select(h => h.TextContent).ToList();
        Assert.All(cut.FindAll("th"), h =>
        {
            if (h.TextContent is "Amount" or "Running balance" or "Budget" or "Actual" or "Remaining")
            {
                Assert.Equal("text-right", h.GetAttribute("class"));
            }
        });
        Assert.Contains(headers, h => h is "Amount" or "Budget"); // sanity check the headers we expect actually rendered
    }

    [Fact]
    public void Dashboard_ThisWeeksSpending_ShowsPendingRowInTheTable()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        var pendingRow = cut.Find("#spending-week-pending-row");
        Assert.Contains("Pending", pendingRow.TextContent);
        Assert.Contains("30.00", pendingRow.TextContent);
    }

    [Fact]
    public void Dashboard_ThisWeeksSpending_ShowsATotalsRow_IncludingPending()
    {
        // Groceries: 450 budget, 120 actual, +30 pending. Budget total = 450.
        // Actual total = 120+30 = 150. Remaining total = 450-150 = 300.
        RegisterFakes();

        var cut = Render<Dashboard>();

        var totalsRow = cut.Find("#spending-week-totals-row");
        Assert.Contains("450.00", totalsRow.TextContent);
        Assert.Contains("150.00", totalsRow.TextContent);
        Assert.Contains("300.00", totalsRow.TextContent);
    }

    [Fact]
    public void Dashboard_RendersThisMonthsSpending_UnderneathThisWeeksSpending_WithItsOwnPendingAndTotals()
    {
        // Month: Groceries 1,956.70 budget, 800 actual, +60 pending. Budget total =
        // 1,956.70. Actual total = 800+60 = 860. Remaining total = 1,956.70-860 = 1,096.70.
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.Contains("Spending for July, 2026", cut.Markup);
        var pendingRow = cut.Find("#spending-month-pending-row");
        Assert.Contains("60.00", pendingRow.TextContent);
        var totalsRow = cut.Find("#spending-month-totals-row");
        Assert.Contains("1,956.70", totalsRow.TextContent);
        Assert.Contains("860.00", totalsRow.TextContent);
        Assert.Contains("1,096.70", totalsRow.TextContent);
    }

    [Fact]
    public void Dashboard_ThisWeeksSpending_RemainingColumn_UsesCarryoverAdjustedRollingBalance_NotPlainRemaining()
    {
        // Real bug found live 2026-08-10: Dashboard's Remaining column (row and Total) were
        // never updated when Spending Tracker carryover shipped - they showed plain
        // Budget-Actual while the Spending Tracker page itself correctly showed the
        // carryover-adjusted RollingBalance. Groceries here: Budget 450, Actual 120 (plain
        // Remaining would be 330.00), but a -60 deficit carried in from last period brings the
        // true rolling balance to 270.00 - that's what must render, not the naive figure.
        var fake = RegisterFakes();
        fake.Data = new SpendingTrackerPageData
        {
            Week = new SpendingTrackerResult
            {
                PeriodStart = new DateOnly(2026, 7, 12), PeriodEnd = new DateOnly(2026, 7, 18),
                Categories =
                [
                    new CategorySpendingSummary
                    {
                        CategoryId = 1, CategoryName = "Groceries", Budget = 450m, Actual = 120m,
                        IsCarryoverTracked = true, CarriedIn = -60m, RollingBalance = 270m
                    }
                ],
                PendingAmount = 0m
            },
            Month = new SpendingTrackerResult { PeriodStart = new DateOnly(2026, 7, 1), PeriodEnd = new DateOnly(2026, 7, 31), Categories = [], PendingAmount = 0m }
        };

        var cut = Render<Dashboard>();

        Assert.DoesNotContain("330.00", cut.Markup);
        var totalsRow = cut.Find("#spending-week-totals-row");
        Assert.Contains("270.00", totalsRow.TextContent);
    }

    [Fact]
    public void Dashboard_LinksToItsOwnDetailPages()
    {
        // Only the pages this dashboard summarizes get their own "drill in" link here -
        // every other page (Categories, Budgets, Accounts, Review Queue, Import Data, etc.) is
        // reachable from the navigation menu now.
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.Contains("href=\"/forecast\"", cut.Markup);
        Assert.Contains("href=\"/spending-tracker\"", cut.Markup);
    }

    [Fact]
    public void Dashboard_DoesNotShowAManageSection_NavigationMenuCoversIt()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.DoesNotContain("Manage", cut.Markup);
    }

    [Fact]
    public void Dashboard_DoesNotShowReviewQueueOrImportData_TheyHaveTheirOwnPagesNow()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.DoesNotContain("Review Queue", cut.Markup);
        Assert.DoesNotContain("Import Data", cut.Markup);
        Assert.Empty(cut.FindAll("#sync-simplefin-btn"));
        Assert.Empty(cut.FindAll("#sync-amazon-btn"));
    }

    [Fact]
    public void Dashboard_ShowsAFailureBanner_WhenTheLastSimpleFinSyncFailed()
    {
        var failedRun = new ImportRun { Source = ImportSource.SimpleFin, RanAt = DateTimeOffset.UtcNow, Success = false, ErrorMessage = "connection timed out" };
        RegisterFakes(lastSimpleFinRun: failedRun);

        var cut = Render<Dashboard>();

        var banner = cut.Find("#sync-failure-banner");
        Assert.Contains("connection timed out", banner.TextContent);
    }

    [Fact]
    public void Dashboard_ShowsAFailureBanner_WhenTheLastAmazonSyncFailed()
    {
        var failedRun = new ImportRun { Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = false, ErrorMessage = "Gmail OAuth token expired" };
        RegisterFakes(lastAmazonRun: failedRun);

        var cut = Render<Dashboard>();

        var banner = cut.Find("#sync-failure-banner");
        Assert.Contains("Gmail OAuth token expired", banner.TextContent);
    }

    [Fact]
    public void Dashboard_ShowsAFailureBanner_WhenTheLastPlaidSyncFailed()
    {
        var failedRun = new ImportRun { Source = ImportSource.Plaid, RanAt = DateTimeOffset.UtcNow, Success = false, ErrorMessage = "plaid-cli exited with code 1" };
        RegisterFakes(lastPlaidRun: failedRun);

        var cut = Render<Dashboard>();

        var banner = cut.Find("#sync-failure-banner");
        Assert.Contains("plaid-cli exited with code 1", banner.TextContent);
    }

    [Fact]
    public void Dashboard_ShowsNoFailureBanner_WhenBothLastSyncsSucceededOrNeverRan()
    {
        var succeededRun = new ImportRun { Source = ImportSource.SimpleFin, RanAt = DateTimeOffset.UtcNow, Success = true };
        RegisterFakes(lastSimpleFinRun: succeededRun, lastAmazonRun: null);

        var cut = Render<Dashboard>();

        Assert.Empty(cut.FindAll("#sync-failure-banner"));
    }

    [Fact]
    public void Dashboard_ShowsAShowResolvedToggle_CheckedByDefault()
    {
        RegisterFakes();

        var cut = Render<Dashboard>();

        Assert.True(cut.Find("#show-resolved-toggle").HasAttribute("checked"));
    }

    [Fact]
    public void UncheckingShowResolved_HidesExcludedRows_ButLeavesNormalRowsVisible()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows =
            [
                new ForecastLedgerRow
                {
                    Date = new DateOnly(2026, 7, 20), Description = "Chase Amazon Prime Visa Payment", Amount = -357m, RunningBalance = 643m,
                    AccountId = 5, OriginalDate = new DateOnly(2026, 7, 20), IsExcluded = true, ExclusionReason = ConfirmationReason.AlreadyPaid, ConfirmationId = 1
                },
                new ForecastLedgerRow { Date = new DateOnly(2026, 7, 31), Description = "Paycheck", Amount = 2000m, RunningBalance = 2643m }
            ]
        };
        RegisterFakes(forecast: forecast);

        var cut = Render<Dashboard>();
        cut.Find("#show-resolved-toggle").Change(false);

        Assert.DoesNotContain("Chase Amazon Prime Visa Payment", cut.Markup);
        Assert.Contains("Paycheck", cut.Markup);
    }

    [Fact]
    public void ReCheckingShowResolved_BringsExcludedRowsBack()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow
            {
                Date = new DateOnly(2026, 7, 20), Description = "Chase Amazon Prime Visa Payment", Amount = -357m, RunningBalance = 643m,
                AccountId = 5, OriginalDate = new DateOnly(2026, 7, 20), IsExcluded = true, ExclusionReason = ConfirmationReason.AlreadyPaid, ConfirmationId = 1
            }]
        };
        RegisterFakes(forecast: forecast);

        var cut = Render<Dashboard>();
        cut.Find("#show-resolved-toggle").Change(false);
        cut.Find("#show-resolved-toggle").Change(true);

        Assert.Contains("Chase Amazon Prime Visa Payment", cut.Markup);
    }

    [Fact]
    public void UncheckingShowResolved_DoesNotHideDeferredRows()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow
            {
                Date = new DateOnly(2026, 7, 22), Description = "Amex Payment", Amount = -2000m, RunningBalance = -1000m,
                AccountId = 2, OriginalDate = new DateOnly(2026, 7, 20), IsDeferred = true, DeferralId = 1
            }]
        };
        RegisterFakes(forecast: forecast);

        var cut = Render<Dashboard>();
        cut.Find("#show-resolved-toggle").Change(false);

        Assert.Contains("Amex Payment", cut.Markup);
    }

    [Fact]
    public void UncheckingShowResolved_SavesThePreferenceToLocalStorage()
    {
        RegisterFakes();
        var setItemCall = JSInterop.SetupVoid("localStorage.setItem", _ => true).SetVoidResult();

        var cut = Render<Dashboard>();
        cut.Find("#show-resolved-toggle").Change(false);

        setItemCall.VerifyInvoke("localStorage.setItem");
    }

    [Fact]
    public void OnLoad_UsesTheSavedShowResolvedPreferenceFromLocalStorage()
    {
        var forecast = new ForecastResult
        {
            StartingBalance = 1000m,
            Rows = [new ForecastLedgerRow
            {
                Date = new DateOnly(2026, 7, 20), Description = "Chase Amazon Prime Visa Payment", Amount = -357m, RunningBalance = 643m,
                AccountId = 5, OriginalDate = new DateOnly(2026, 7, 20), IsExcluded = true, ExclusionReason = ConfirmationReason.AlreadyPaid, ConfirmationId = 1
            }]
        };
        RegisterFakes(forecast: forecast);
        JSInterop.Setup<string?>("localStorage.getItem", _ => true).SetResult("false");

        var cut = Render<Dashboard>();

        Assert.False(cut.Find("#show-resolved-toggle").HasAttribute("checked"));
        Assert.DoesNotContain("Chase Amazon Prime Visa Payment", cut.Markup);
    }

    // User reported 2026-08-07: checking the box on one page showed it checked on the other
    // too - the two pages were sharing a single localStorage key. Confirmed independent now.
    [Fact]
    public void DashboardAndForecast_SaveTheShowResolvedPreference_UnderDifferentLocalStorageKeys()
    {
        // Both pages resolve the same registered IForecastResultProvider - fine, since only
        // the storage key being written is under test here, not either page's own content.
        RegisterFakes(forecast: new ForecastResult { StartingBalance = 0m, Rows = [] });
        var setItemCall = JSInterop.SetupVoid("localStorage.setItem", _ => true).SetVoidResult();

        var dashboardCut = Render<Dashboard>();
        dashboardCut.Find("#show-resolved-toggle").Change(false);
        var dashboardKey = setItemCall.Invocations.Last().Arguments[0];

        var forecastCut = Render<Forecast>();
        forecastCut.Find("#show-resolved-toggle").Change(false);
        var forecastKey = setItemCall.Invocations.Last().Arguments[0];

        Assert.NotEqual(dashboardKey, forecastKey);
    }
}
