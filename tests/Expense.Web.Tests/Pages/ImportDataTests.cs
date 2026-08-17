using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services;
using Expense.Domain.Services.Dashboard;
using Expense.Domain.Services.Ingestion.Amazon;
using Expense.Domain.Settings;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Expense.Web.Tests.Pages;

public class ImportDataTests : BunitContext
{
    private readonly DataChangeNotifier _dataChangeNotifier = new();

    public ImportDataTests()
    {
        Services.AddSingleton<IDataChangeNotifier>(_dataChangeNotifier);
    }

    private class FakeSyncStatusProvider(ImportRun? lastSimpleFinRun = null, ImportRun? lastAmazonRun = null, ImportRun? lastPlaidRun = null) : ISyncStatusProvider
    {
        public int SimpleFinRunCount { get; private set; }
        public int AmazonGmailRunCount { get; private set; }
        public ImportRun NextSimpleFinRunResult { get; set; } = new() { Source = ImportSource.SimpleFin, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = "ok" };
        public ImportRun NextAmazonGmailRunResult { get; set; } = new() { Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = "ok" };
        public List<SyncIssue> ActiveSyncIssues { get; set; } = [];

        public Task<ImportRun?> GetLastSimpleFinRunAsync(CancellationToken cancellationToken = default) => Task.FromResult(lastSimpleFinRun);
        public Task<ImportRun?> GetLastAmazonGmailRunAsync(CancellationToken cancellationToken = default) => Task.FromResult(lastAmazonRun);
        public Task<ImportRun?> GetLastPlaidRunAsync(CancellationToken cancellationToken = default) => Task.FromResult(lastPlaidRun);

        public Task<ImportRun> RunSimpleFinSyncAsync(CancellationToken cancellationToken = default)
        {
            SimpleFinRunCount++;
            return Task.FromResult(NextSimpleFinRunResult);
        }

        public List<SyncProgressLine> ProgressLinesToReport { get; set; } = [];
        public TaskCompletionSource? RunGate { get; set; }

        public async Task<ImportRun> RunAmazonGmailSyncAsync(Action<SyncProgressLine>? onProgress = null, CancellationToken cancellationToken = default)
        {
            AmazonGmailRunCount++;
            foreach (var line in ProgressLinesToReport)
            {
                onProgress?.Invoke(line);
            }
            if (RunGate is not null)
            {
                await RunGate.Task;
            }
            return NextAmazonGmailRunResult;
        }

        public int PlaidImportCount { get; private set; }
        public DateOnly? LastPlaidStartDate { get; private set; }
        public DateOnly? LastPlaidEndDate { get; private set; }
        public ImportRun NextPlaidImportRun { get; set; } = new() { Source = ImportSource.Plaid, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = "Transactions added: 0, duplicates skipped: 0, pending transactions updated: 0, balance snapshots added: 1" };

        public Task<ImportRun> RunPlaidImportAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
        {
            PlaidImportCount++;
            LastPlaidStartDate = startDate;
            LastPlaidEndDate = endDate;
            return Task.FromResult(NextPlaidImportRun);
        }

        public int ScheduledPlaidRunCount { get; private set; }
        public ImportRun NextScheduledPlaidRunResult { get; set; } = new() { Source = ImportSource.Plaid, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = "ok" };

        public Task<ImportRun> RunScheduledPlaidSyncAsync(CancellationToken cancellationToken = default)
        {
            ScheduledPlaidRunCount++;
            return Task.FromResult(NextScheduledPlaidRunResult);
        }

        public Dictionary<ImportSource, List<ImportRun>> RecentRuns { get; set; } = new()
        {
            [ImportSource.SimpleFin] = [],
            [ImportSource.AmazonGmail] = [],
            [ImportSource.Plaid] = []
        };
        public Dictionary<int, List<SyncProgressLine>> ProgressLogsByRunId { get; set; } = [];

        public Task<RecentRunsPage> GetRecentRunsAsync(ImportSource source, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var all = RecentRuns.TryGetValue(source, out var runs) ? runs : [];
            var pageOfRuns = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(new RecentRunsPage { Runs = pageOfRuns, TotalCount = all.Count });
        }

        public Task<List<SyncProgressLine>> GetRunProgressLogAsync(int importRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProgressLogsByRunId.TryGetValue(importRunId, out var lines) ? lines : []);

        public Task<List<SyncIssue>> GetActiveSyncIssuesAsync(CancellationToken cancellationToken = default) => Task.FromResult(ActiveSyncIssues);

        public string? LastResolvedOrderId { get; private set; }
        public string? LastResolvedItemTitle { get; private set; }
        public decimal? LastResolvedPrice { get; private set; }
        public int? LastResolvedQuantity { get; private set; }

        public Task ResolveSyncIssueAsync(int syncIssueId, string orderId, string itemTitle, decimal price, int quantity, CancellationToken cancellationToken = default)
        {
            LastResolvedOrderId = orderId;
            LastResolvedItemTitle = itemTitle;
            LastResolvedPrice = price;
            LastResolvedQuantity = quantity;
            ActiveSyncIssues = ActiveSyncIssues.Where(i => i.Id != syncIssueId).ToList();
            return Task.CompletedTask;
        }

        public Task IgnoreSyncIssueAsync(int syncIssueId, CancellationToken cancellationToken = default)
        {
            ActiveSyncIssues = ActiveSyncIssues.Where(i => i.Id != syncIssueId).ToList();
            return Task.CompletedTask;
        }
    }

    private FakeSyncStatusProvider RegisterFakes(
        ImportRun? lastSimpleFinRun = null, ImportRun? lastAmazonRun = null, ImportRun? lastPlaidRun = null,
        List<SyncIssue>? activeSyncIssues = null, bool simpleFinEnabled = true)
    {
        var provider = new FakeSyncStatusProvider(lastSimpleFinRun, lastAmazonRun, lastPlaidRun) { ActiveSyncIssues = activeSyncIssues ?? [] };
        Services.AddSingleton<ISyncStatusProvider>(provider);
        Services.AddSingleton<IOptions<AppSettings>>(Options.Create(new AppSettings { SimpleFinEnabled = simpleFinEnabled }));
        return provider;
    }

    // With no modal open and no resolve form started, there's nothing a reload could
    // disrupt - refreshes the sync issue list silently.
    [Fact]
    public void DataChangeNotifier_Firing_WithNothingInProgress_SilentlyRefreshes()
    {
        var provider = RegisterFakes();

        var cut = Render<ImportData>();
        Assert.DoesNotContain("Mystery Order", cut.Markup);

        provider.ActiveSyncIssues.Add(new SyncIssue { Id = 5, MessageId = "m5", Subject = "Mystery Order", Reason = "Couldn't parse", ReceivedDate = new DateOnly(2026, 8, 17) });
        _dataChangeNotifier.NotifyChanged();

        cut.WaitForAssertion(() => Assert.Contains("Mystery Order", cut.Markup));
        Assert.Empty(cut.FindAll("#new-data-banner"));
    }

    // With the resolve form started for a sync issue, silently reloading the issue list
    // could swap out the very issue being resolved - shows the banner instead, leaving the
    // in-progress form untouched.
    [Fact]
    public void DataChangeNotifier_Firing_WithAResolveFormStarted_ShowsTheBanner_WithoutDisturbingTheForm()
    {
        var provider = RegisterFakes(activeSyncIssues:
        [
            new SyncIssue { Id = 5, MessageId = "m5", Subject = "Mystery Order", Reason = "Couldn't parse", ReceivedDate = new DateOnly(2026, 8, 17) }
        ]);

        var cut = Render<ImportData>();
        cut.Find("#resolve-order-id-5").Change("112-9999");

        provider.ActiveSyncIssues.Add(new SyncIssue { Id = 6, MessageId = "m6", Subject = "Another One", Reason = "Couldn't parse", ReceivedDate = new DateOnly(2026, 8, 17) });
        _dataChangeNotifier.NotifyChanged();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#new-data-banner")));
        Assert.NotEmpty(cut.FindAll("#resolve-order-id-5"));
    }

    [Fact]
    public void SimpleFinSection_IsShown_WhenSimpleFinIsEnabled()
    {
        RegisterFakes(simpleFinEnabled: true);

        var cut = Render<ImportData>();

        Assert.NotEmpty(cut.FindAll("#sync-simplefin-btn"));
    }

    [Fact]
    public void SimpleFinSection_IsHidden_WhenSimpleFinIsDisabled()
    {
        RegisterFakes(simpleFinEnabled: false);

        var cut = Render<ImportData>();

        Assert.Empty(cut.FindAll("#sync-simplefin-btn"));
        Assert.Empty(cut.FindAll("#sync-simplefin-status"));
        Assert.Empty(cut.FindAll("#simplefin-recent-runs"));
    }

    [Fact]
    public void AmazonAndPlaidSections_StillShow_WhenSimpleFinIsDisabled()
    {
        RegisterFakes(simpleFinEnabled: false);

        var cut = Render<ImportData>();

        Assert.NotEmpty(cut.FindAll("#sync-amazon-btn"));
        Assert.NotEmpty(cut.FindAll("#run-plaid-import-btn"));
    }

    [Fact]
    public void AmazonSyncButton_IsClearlyLabeledForAmazonOrders()
    {
        RegisterFakes();

        var cut = Render<ImportData>();

        var button = cut.Find("#sync-amazon-btn");
        Assert.Contains("Amazon", button.TextContent);
        Assert.Contains("Amazon order/refund emails", cut.Markup);
    }

    [Fact]
    public void WhenNeitherSourceHasEverSynced_ShowsNever()
    {
        RegisterFakes();

        var cut = Render<ImportData>();

        Assert.Contains("Last synced: never", cut.Find("#sync-simplefin-status").TextContent);
        Assert.Contains("Last synced: never", cut.Find("#sync-amazon-status").TextContent);
    }

    [Fact]
    public void ShowsTheLastSuccessfulSyncTime()
    {
        var lastRun = new ImportRun
        {
            Source = ImportSource.SimpleFin, RanAt = new DateTimeOffset(2026, 7, 16, 8, 30, 0, TimeSpan.Zero), Success = true, Summary = "ok"
        };
        RegisterFakes(lastSimpleFinRun: lastRun);

        var cut = Render<ImportData>();

        Assert.Contains("Last synced:", cut.Find("#sync-simplefin-status").TextContent);
        Assert.DoesNotContain("FAILED", cut.Find("#sync-simplefin-status").TextContent);
    }

    [Fact]
    public void ShowsTheErrorWhenTheLastSyncFailed()
    {
        var failedRun = new ImportRun
        {
            Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = false, ErrorMessage = "Gmail OAuth token expired"
        };
        RegisterFakes(lastAmazonRun: failedRun);

        var cut = Render<ImportData>();

        Assert.Contains("FAILED: Gmail OAuth token expired", cut.Find("#sync-amazon-status").TextContent);
    }

    [Fact]
    public void ClickingSimpleFinButton_TriggersASyncAndUpdatesTheDisplayedStatus()
    {
        var fake = RegisterFakes();
        fake.NextSimpleFinRunResult = new ImportRun
        {
            Source = ImportSource.SimpleFin, RanAt = new DateTimeOffset(2026, 7, 16, 9, 0, 0, TimeSpan.Zero), Success = true,
            Summary = "Transactions added: 5, duplicates skipped: 0, balance snapshots added: 2"
        };

        var cut = Render<ImportData>();
        cut.Find("#sync-simplefin-btn").Click();

        Assert.Equal(1, fake.SimpleFinRunCount);
        Assert.Equal(0, fake.AmazonGmailRunCount);
        Assert.Contains("Last synced:", cut.Find("#sync-simplefin-status").TextContent);
    }

    [Fact]
    public void ClickingAmazonButton_TriggersASyncIndependentlyOfSimpleFin()
    {
        var fake = RegisterFakes();

        var cut = Render<ImportData>();
        cut.Find("#sync-amazon-btn").Click();

        Assert.Equal(1, fake.AmazonGmailRunCount);
        Assert.Equal(0, fake.SimpleFinRunCount);
    }

    [Fact]
    public void NoModalShownBeforeAnAmazonSyncIsStarted()
    {
        RegisterFakes();

        var cut = Render<ImportData>();

        Assert.Empty(cut.FindAll("#amazon-sync-modal"));
    }

    [Fact]
    public void ClickingAmazonSync_OpensAModal_ShowingEachProgressLineAsItStreamsIn()
    {
        var fake = RegisterFakes();
        fake.ProgressLinesToReport =
        [
            new SyncProgressLine("Found 1 order confirmation email(s) to check."),
            new SyncProgressLine("[2026-07-18] \"Your order\"\n--- Email body ---\nOrder #\n113-TEST\n--- Result ---\nAdded: Widget - $9.99 x1")
        ];
        var cut = Render<ImportData>();

        cut.Find("#sync-amazon-btn").Click();

        var modal = cut.Find("#amazon-sync-modal");
        Assert.Contains("Found 1 order confirmation email(s)", modal.TextContent);
        Assert.Contains("Order #", modal.TextContent);
        Assert.Contains("113-TEST", modal.TextContent);
        Assert.Contains("Added: Widget", modal.TextContent);
    }

    [Fact]
    public void AmazonSyncModal_MarksErrorLinesDistinctly()
    {
        var fake = RegisterFakes();
        fake.ProgressLinesToReport = [new SyncProgressLine("FAILED: could not parse", IsError: true)];
        var cut = Render<ImportData>();

        cut.Find("#sync-amazon-btn").Click();

        var errorLine = cut.Find("#amazon-sync-modal .sync-progress-error");
        Assert.Contains("FAILED: could not parse", errorLine.TextContent);
    }

    [Fact]
    public void AmazonSyncModal_HasNoCloseButtonWhileTheSyncIsStillRunning()
    {
        var fake = RegisterFakes();
        fake.RunGate = new TaskCompletionSource();
        var cut = Render<ImportData>();

        cut.Find("#sync-amazon-btn").Click();

        Assert.Empty(cut.FindAll("#close-amazon-sync-modal-btn"));

        fake.RunGate.SetResult();
    }

    [Fact]
    public void AmazonSyncModal_ShowsACloseButton_OnceTheSyncCompletes()
    {
        RegisterFakes();

        var cut = Render<ImportData>();
        cut.Find("#sync-amazon-btn").Click();

        Assert.NotNull(cut.Find("#close-amazon-sync-modal-btn"));
    }

    [Fact]
    public void ClosingTheAmazonSyncModal_HidesIt()
    {
        RegisterFakes();
        var cut = Render<ImportData>();
        cut.Find("#sync-amazon-btn").Click();

        cut.Find("#close-amazon-sync-modal-btn").Click();

        Assert.Empty(cut.FindAll("#amazon-sync-modal"));
    }

    [Fact]
    public void ClickingSimpleFinSync_DoesNotOpenTheAmazonModal()
    {
        RegisterFakes();
        var cut = Render<ImportData>();

        cut.Find("#sync-simplefin-btn").Click();

        Assert.Empty(cut.FindAll("#amazon-sync-modal"));
    }

    [Fact]
    public void ShowsTheLastRunsSummary_NotJustTheTimestamp()
    {
        var lastAmazonRun = new ImportRun
        {
            Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = true,
            Summary = "Order items added: 3, duplicates skipped: 319, refunds applied: 0; 2 email(s) failed to parse"
        };
        RegisterFakes(lastAmazonRun: lastAmazonRun);

        var cut = Render<ImportData>();

        Assert.Contains("2 email(s) failed to parse", cut.Markup);
    }

    [Fact]
    public void WithNoSyncIssues_DoesNotShowTheSyncIssuesSection()
    {
        RegisterFakes();

        var cut = Render<ImportData>();

        Assert.Empty(cut.FindAll("#sync-issues-section"));
    }

    [Fact]
    public void WithActiveSyncIssues_ShowsThemForReview_IncludingTheRawEmailBody()
    {
        var issues = new List<SyncIssue>
        {
            new()
            {
                Id = 1, Source = ImportSource.AmazonGmail, MessageId = "msg-1", Subject = "Ordered: 2 Nutrition items",
                Reason = "could not find any items in the email body", ReceivedDate = new DateOnly(2026, 7, 18),
                Body = "Order #\n113-3763507-4662613\n\nGrand Total:\n56.17 USD", CreatedAt = DateTimeOffset.UtcNow
            }
        };
        RegisterFakes(activeSyncIssues: issues);

        var cut = Render<ImportData>();

        var section = cut.Find("#sync-issues-section");
        Assert.Contains("1", section.TextContent);
        Assert.Contains("Ordered: 2 Nutrition items", section.TextContent);
        Assert.Contains("could not find any items in the email body", section.TextContent);
        Assert.Contains("07/18/2026", section.TextContent);
        Assert.Contains("56.17 USD", section.TextContent); // the raw body, so Gmail never needs to be opened
    }

    [Fact]
    public void ResolvingASyncIssue_SubmitsTheEnteredDetails_AndRemovesItFromTheList()
    {
        var issues = new List<SyncIssue>
        {
            new() { Id = 1, Source = ImportSource.AmazonGmail, MessageId = "msg-1", Subject = "Ordered: 2 Nutrition items", Reason = "could not find any items", ReceivedDate = new DateOnly(2026, 7, 18), CreatedAt = DateTimeOffset.UtcNow }
        };
        var fake = RegisterFakes(activeSyncIssues: issues);
        var cut = Render<ImportData>();

        cut.Find("#resolve-order-id-1").Change("113-3763507-4662613");
        cut.Find("#resolve-item-title-1").Change("Some Supplement");
        cut.Find("#resolve-price-1").Change("56.17");
        cut.Find("#resolve-quantity-1").Change("2");
        cut.Find("#resolve-btn-1").Click();

        Assert.Equal("113-3763507-4662613", fake.LastResolvedOrderId);
        Assert.Equal("Some Supplement", fake.LastResolvedItemTitle);
        Assert.Equal(56.17m, fake.LastResolvedPrice);
        Assert.Equal(2, fake.LastResolvedQuantity);
        Assert.Empty(cut.FindAll("#sync-issues-section"));
    }

    [Fact]
    public void IgnoringASyncIssueAsNotAnOrder_RemovesItFromTheList()
    {
        var issues = new List<SyncIssue>
        {
            new() { Id = 1, Source = ImportSource.AmazonGmail, MessageId = "msg-1", Subject = "An Amazon Gift Card you sent was received", Reason = "could not find an 'Order #' line", ReceivedDate = new DateOnly(2026, 7, 18), CreatedAt = DateTimeOffset.UtcNow }
        };
        RegisterFakes(activeSyncIssues: issues);
        var cut = Render<ImportData>();

        cut.Find("#ignore-not-order-btn-1").Click();

        Assert.Empty(cut.FindAll("#sync-issues-section"));
    }

    [Fact]
    public void RecentRunsSection_ListsPastRunsForEachSource_NewestFirst()
    {
        var fake = RegisterFakes();
        fake.RecentRuns[ImportSource.SimpleFin] =
        [
            new ImportRun { Id = 2, Source = ImportSource.SimpleFin, RanAt = new DateTimeOffset(2026, 7, 22, 15, 0, 0, TimeSpan.Zero), Success = true, Summary = "3pm run" },
            new ImportRun { Id = 1, Source = ImportSource.SimpleFin, RanAt = new DateTimeOffset(2026, 7, 22, 6, 0, 0, TimeSpan.Zero), Success = true, Summary = "6am run" }
        ];

        var cut = Render<ImportData>();

        var section = cut.Find("#simplefin-recent-runs");
        var rows = section.QuerySelectorAll("tbody tr");
        Assert.Equal(2, rows.Length);
        Assert.Contains("3pm run", rows[0].TextContent);
        Assert.Contains("6am run", rows[1].TextContent);
    }

    [Fact]
    public void RecentRunsSection_ShowsSuccessAndFailureStatusPerRun()
    {
        var fake = RegisterFakes();
        fake.RecentRuns[ImportSource.AmazonGmail] =
        [
            new ImportRun { Id = 3, Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = false, ErrorMessage = "Gmail OAuth token expired" }
        ];

        var cut = Render<ImportData>();

        var section = cut.Find("#amazon-recent-runs");
        Assert.Contains("FAILED", section.TextContent);
        Assert.Contains("Gmail OAuth token expired", section.TextContent);
    }

    [Fact]
    public void RecentRunsSection_ListsPastPlaidRuns_ScheduledAndManualAlike()
    {
        var fake = RegisterFakes();
        fake.RecentRuns[ImportSource.Plaid] =
        [
            new ImportRun { Id = 5, Source = ImportSource.Plaid, RanAt = new DateTimeOffset(2026, 7, 29, 15, 0, 0, TimeSpan.Zero), Success = true, Summary = "scheduled run" },
            new ImportRun { Id = 4, Source = ImportSource.Plaid, RanAt = new DateTimeOffset(2026, 7, 29, 6, 0, 0, TimeSpan.Zero), Success = false, ErrorMessage = "plaid-cli exited with code 1" }
        ];

        var cut = Render<ImportData>();

        var section = cut.Find("#plaid-recent-runs");
        var rows = section.QuerySelectorAll("tbody tr");
        Assert.Equal(2, rows.Length);
        Assert.Contains("scheduled run", rows[0].TextContent);
        Assert.Contains("FAILED", rows[1].TextContent);
        Assert.Contains("plaid-cli exited with code 1", rows[1].TextContent);
    }

    [Fact]
    public void ClickingViewDetails_OpensTheDetailModal_WithThatRunsPersistedProgressLog()
    {
        var fake = RegisterFakes();
        fake.RecentRuns[ImportSource.AmazonGmail] =
        [
            new ImportRun { Id = 5, Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = "ok" }
        ];
        fake.ProgressLogsByRunId[5] =
        [
            new SyncProgressLine("Found 1 order confirmation email(s) to check."),
            new SyncProgressLine("[2026-07-22] \"Your order\"\n--- Email body ---\nOrder #\n113-TEST\n--- Result ---\nAdded: Widget - $9.99 x1")
        ];
        var cut = Render<ImportData>();

        cut.Find("#view-run-details-5").Click();

        var modal = cut.Find("#run-detail-modal");
        Assert.Contains("Found 1 order confirmation email(s)", modal.TextContent);
        Assert.Contains("113-TEST", modal.TextContent);
        Assert.Contains("Added: Widget", modal.TextContent);
    }

    [Fact]
    public void ClickingViewDetails_ShowsTheRawResponse_BelowTheParsedProgressLog_WhenOneWasCaptured()
    {
        var fake = RegisterFakes();
        fake.RecentRuns[ImportSource.Plaid] =
        [
            new ImportRun { Id = 9, Source = ImportSource.Plaid, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = "ok", RawResponse = """{"transactions":[{"transaction_id":"txn-raw-check"}]}""" }
        ];
        fake.ProgressLogsByRunId[9] = [new SyncProgressLine("Netflix -26.99 (07/28/2026) - added")];
        var cut = Render<ImportData>();

        cut.Find("#view-run-details-9").Click();

        var modal = cut.Find("#run-detail-modal");
        Assert.Contains("Netflix", modal.TextContent);
        Assert.Contains("txn-raw-check", modal.TextContent);
    }

    [Fact]
    public void ClickingViewDetails_RendersPlaidStyleLines_AsATable_WithOneRowPerTransaction()
    {
        var fake = RegisterFakes();
        fake.RecentRuns[ImportSource.Plaid] =
        [
            new ImportRun { Id = 9, Source = ImportSource.Plaid, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = "ok" }
        ];
        fake.ProgressLogsByRunId[9] =
        [
            new SyncProgressLine("Netflix -26.99 (07/28/2026) - added"),
            new SyncProgressLine("Chipotle Mexican Grill -33.87 (07/27/2026) - duplicate, already imported"),
            new SyncProgressLine("Costco -55.00 (07/28/2026) - unmapped account plaid-checking-9, skipped", IsError: true),
            new SyncProgressLine("Done - transactions added: 1, duplicates skipped: 1, pending transactions updated: 0, balance snapshots added: 0")
        ];
        var cut = Render<ImportData>();

        cut.Find("#view-run-details-9").Click();

        var modal = cut.Find("#run-detail-modal");
        var rows = modal.QuerySelectorAll("table tbody tr");
        Assert.Equal(3, rows.Length);
        Assert.Contains("Netflix", rows[0].TextContent);
        Assert.Contains("-26.99", rows[0].TextContent);
        Assert.Contains("07/28/2026", rows[0].TextContent);
        Assert.Contains("added", rows[0].TextContent);
        Assert.Contains("duplicate, already imported", rows[1].TextContent);
        Assert.Contains("unmapped account", rows[2].TextContent);
        // The final "Done" summary doesn't fit the per-transaction shape - it renders
        // outside the table instead of as a broken row.
        Assert.Contains("Done - transactions added: 1", modal.TextContent);
        Assert.DoesNotContain("Done - transactions added: 1", modal.QuerySelector("table")!.TextContent);
    }

    [Fact]
    public void ClickingViewDetails_IncludesLinesWithThousandsSeparatorAmounts_InTheTable()
    {
        // Real bug found live 2026-07-30: N2 formatting inserts a thousands-separator
        // comma for amounts >= $1,000 (e.g. "-3,852.27") - the parsing regex didn't
        // account for it, so every large-amount line silently fell out of the table and
        // rendered as a stray line below it instead.
        var fake = RegisterFakes();
        fake.RecentRuns[ImportSource.Plaid] =
        [
            new ImportRun { Id = 9, Source = ImportSource.Plaid, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = "ok" }
        ];
        fake.ProgressLogsByRunId[9] =
        [
            new SyncProgressLine("Netflix -26.99 (07/28/2026) - added"),
            new SyncProgressLine("AMERICAN EXPRESS ACH PMT 260724 W8172 MARK SALCEDO -3,852.27 (07/24/2026) - duplicate, already imported"),
            new SyncProgressLine("OASISBATCH PAYROLL 260724 G1923022160 MARK SALCEDO 4,492.86 (07/24/2026) - duplicate, already imported")
        ];
        var cut = Render<ImportData>();

        cut.Find("#view-run-details-9").Click();

        var modal = cut.Find("#run-detail-modal");
        var rows = modal.QuerySelectorAll("table tbody tr");
        Assert.Equal(3, rows.Length);
        Assert.Contains(rows, r => r.TextContent.Contains("-3,852.27"));
        Assert.Contains(rows, r => r.TextContent.Contains("4,492.86"));
    }

    [Fact]
    public void ClickingViewDetails_DoesNotRenderATable_ForNonPlaidStyleLines()
    {
        var fake = RegisterFakes();
        fake.RecentRuns[ImportSource.AmazonGmail] =
        [
            new ImportRun { Id = 5, Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = "ok" }
        ];
        fake.ProgressLogsByRunId[5] = [new SyncProgressLine("Found 1 order confirmation email(s) to check.")];
        var cut = Render<ImportData>();

        cut.Find("#view-run-details-5").Click();

        Assert.Empty(cut.Find("#run-detail-modal").QuerySelectorAll("table"));
    }

    [Fact]
    public void ClickingViewDetails_ShowsNoRawResponseSection_WhenNoneWasCaptured()
    {
        var fake = RegisterFakes();
        fake.RecentRuns[ImportSource.AmazonGmail] =
        [
            new ImportRun { Id = 5, Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = "ok" }
        ];
        var cut = Render<ImportData>();

        cut.Find("#view-run-details-5").Click();

        Assert.Empty(cut.FindAll("#run-detail-raw-response"));
    }

    [Fact]
    public void RecentRunsSection_ShowsOnlyAPageWorthOfRuns_SoALongHistoryDoesNotLengthenThePage()
    {
        var fake = RegisterFakes();
        fake.RecentRuns[ImportSource.SimpleFin] =
            Enumerable.Range(1, 12).Select(i => new ImportRun { Id = i, Source = ImportSource.SimpleFin, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = $"run {i}" }).ToList();

        var cut = Render<ImportData>();

        var section = cut.Find("#simplefin-recent-runs");
        Assert.Equal(5, section.QuerySelectorAll("tbody tr").Length);
        Assert.Contains("Page 1 of 3", cut.Find("#simplefin-recent-runs-page-indicator").TextContent);
    }

    [Fact]
    public void RecentRunsSection_PrevPageButton_IsDisabledOnTheFirstPage()
    {
        var fake = RegisterFakes();
        fake.RecentRuns[ImportSource.SimpleFin] =
            Enumerable.Range(1, 12).Select(i => new ImportRun { Id = i, Source = ImportSource.SimpleFin, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = $"run {i}" }).ToList();

        var cut = Render<ImportData>();

        Assert.True(cut.Find("#simplefin-recent-runs-prev-page-btn").HasAttribute("disabled"));
        Assert.False(cut.Find("#simplefin-recent-runs-next-page-btn").HasAttribute("disabled"));
    }

    [Fact]
    public void ClickingNextPage_LoadsTheNextPageOfRuns_AndDisablesNextOnTheLastPage()
    {
        var fake = RegisterFakes();
        fake.RecentRuns[ImportSource.AmazonGmail] =
            Enumerable.Range(1, 7).Select(i => new ImportRun { Id = i, Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = $"run {i}" }).ToList();
        var cut = Render<ImportData>();

        cut.Find("#amazon-recent-runs-next-page-btn").Click();

        Assert.Contains("Page 2 of 2", cut.Find("#amazon-recent-runs-page-indicator").TextContent);
        Assert.Equal(2, cut.Find("#amazon-recent-runs").QuerySelectorAll("tbody tr").Length);
        Assert.True(cut.Find("#amazon-recent-runs-next-page-btn").HasAttribute("disabled"));
    }

    [Fact]
    public void ClickingPreviousPage_GoesBackToTheEarlierPage()
    {
        var fake = RegisterFakes();
        fake.RecentRuns[ImportSource.AmazonGmail] =
            Enumerable.Range(1, 7).Select(i => new ImportRun { Id = i, Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = $"run {i}" }).ToList();
        var cut = Render<ImportData>();
        cut.Find("#amazon-recent-runs-next-page-btn").Click();

        cut.Find("#amazon-recent-runs-prev-page-btn").Click();

        Assert.Contains("Page 1 of 2", cut.Find("#amazon-recent-runs-page-indicator").TextContent);
    }

    [Fact]
    public void ClosingTheHistoryDetailModal_HidesIt()
    {
        var fake = RegisterFakes();
        fake.RecentRuns[ImportSource.AmazonGmail] =
        [
            new ImportRun { Id = 5, Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = "ok" }
        ];
        var cut = Render<ImportData>();
        cut.Find("#view-run-details-5").Click();

        cut.Find("#close-run-detail-modal-btn").Click();

        Assert.Empty(cut.FindAll("#run-detail-modal"));
    }

    [Fact]
    public void PlaidImportSection_IsNotLabeledAsCheckingOnly_NowThatItCanCoverMultipleAccounts()
    {
        RegisterFakes();

        var cut = Render<ImportData>();

        Assert.DoesNotContain("(Checking)", cut.Markup);
    }

    [Fact]
    public void PlaidImport_DefaultsToTheLastSevenDaysThroughToday()
    {
        RegisterFakes();

        var cut = Render<ImportData>();

        var expectedStart = DateOnly.FromDateTime(DateTime.Today).AddDays(-7).ToString("yyyy-MM-dd");
        var expectedEnd = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        Assert.Equal(expectedStart, cut.Find("#plaid-start-date").GetAttribute("value"));
        Assert.Equal(expectedEnd, cut.Find("#plaid-end-date").GetAttribute("value"));
    }

    [Fact]
    public void ClickingRunPlaidImport_PassesTheEnteredDatesToTheProvider()
    {
        var fake = RegisterFakes();
        var cut = Render<ImportData>();

        cut.Find("#plaid-start-date").Change("2026-07-15");
        cut.Find("#plaid-end-date").Change("2026-07-20");
        cut.Find("#run-plaid-import-btn").Click();

        Assert.Equal(1, fake.PlaidImportCount);
        Assert.Equal(new DateOnly(2026, 7, 15), fake.LastPlaidStartDate);
        Assert.Equal(new DateOnly(2026, 7, 20), fake.LastPlaidEndDate);
    }

    [Fact]
    public void PlaidImport_OnSuccess_ShowsTheResultSummary()
    {
        var fake = RegisterFakes();
        fake.NextPlaidImportRun = new ImportRun
        {
            Source = ImportSource.Plaid, RanAt = DateTimeOffset.UtcNow, Success = true,
            Summary = "Transactions added: 3, duplicates skipped: 12, pending transactions updated: 0, balance snapshots added: 1"
        };
        var cut = Render<ImportData>();

        cut.Find("#run-plaid-import-btn").Click();

        Assert.DoesNotContain("FAILED", cut.Find("#sync-plaid-status").TextContent);
        Assert.Contains("Transactions added: 3", cut.Find("#sync-plaid-summary").TextContent);
    }

    [Fact]
    public void PlaidImport_OnFailure_ShowsTheErrorClearly()
    {
        var fake = RegisterFakes();
        fake.NextPlaidImportRun = new ImportRun
        {
            Source = ImportSource.Plaid, RanAt = DateTimeOffset.UtcNow, Success = false,
            ErrorMessage = "plaid-cli not found at /home/user/bin/plaid-cli."
        };
        var cut = Render<ImportData>();

        cut.Find("#run-plaid-import-btn").Click();

        Assert.Contains("FAILED: plaid-cli not found", cut.Find("#sync-plaid-status").TextContent);
    }

    [Fact]
    public void ClickingRunPlaidImport_DoesNotAffectSimpleFinOrAmazonRunCounts()
    {
        var fake = RegisterFakes();
        var cut = Render<ImportData>();

        cut.Find("#run-plaid-import-btn").Click();

        Assert.Equal(0, fake.SimpleFinRunCount);
        Assert.Equal(0, fake.AmazonGmailRunCount);
    }

    [Fact]
    public void AfterAmazonSync_RefreshesTheSyncIssuesList()
    {
        var fake = RegisterFakes();
        var cut = Render<ImportData>();
        Assert.Empty(cut.FindAll("#sync-issues-section"));

        fake.ActiveSyncIssues =
        [
            new SyncIssue { Id = 2, Source = ImportSource.AmazonGmail, MessageId = "msg-2", Subject = "New failure", Reason = "bad format", CreatedAt = DateTimeOffset.UtcNow }
        ];
        cut.Find("#sync-amazon-btn").Click();

        Assert.NotNull(cut.Find("#sync-issues-section"));
        Assert.Contains("New failure", cut.Markup);
    }
}
