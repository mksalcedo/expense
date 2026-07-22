using Bunit;
using Expense.Domain.Entities;
using Expense.Domain.Services.Dashboard;
using Expense.Domain.Services.Ingestion.Amazon;
using Expense.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Expense.Web.Tests.Pages;

public class SyncNowTests : BunitContext
{
    private class FakeSyncStatusProvider(ImportRun? lastSimpleFinRun = null, ImportRun? lastAmazonRun = null) : ISyncStatusProvider
    {
        public int SimpleFinRunCount { get; private set; }
        public int AmazonGmailRunCount { get; private set; }
        public ImportRun NextSimpleFinRunResult { get; set; } = new() { Source = ImportSource.SimpleFin, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = "ok" };
        public ImportRun NextAmazonGmailRunResult { get; set; } = new() { Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = true, Summary = "ok" };
        public List<SyncIssue> ActiveSyncIssues { get; set; } = [];

        public Task<ImportRun?> GetLastSimpleFinRunAsync(CancellationToken cancellationToken = default) => Task.FromResult(lastSimpleFinRun);
        public Task<ImportRun?> GetLastAmazonGmailRunAsync(CancellationToken cancellationToken = default) => Task.FromResult(lastAmazonRun);

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

    private FakeSyncStatusProvider RegisterFakes(ImportRun? lastSimpleFinRun = null, ImportRun? lastAmazonRun = null, List<SyncIssue>? activeSyncIssues = null)
    {
        var provider = new FakeSyncStatusProvider(lastSimpleFinRun, lastAmazonRun) { ActiveSyncIssues = activeSyncIssues ?? [] };
        Services.AddSingleton<ISyncStatusProvider>(provider);
        return provider;
    }

    [Fact]
    public void AmazonSyncButton_IsClearlyLabeledForAmazonOrders()
    {
        RegisterFakes();

        var cut = Render<SyncNow>();

        var button = cut.Find("#sync-amazon-btn");
        Assert.Contains("Amazon", button.TextContent);
        Assert.Contains("Amazon order/refund emails", cut.Markup);
    }

    [Fact]
    public void WhenNeitherSourceHasEverSynced_ShowsNever()
    {
        RegisterFakes();

        var cut = Render<SyncNow>();

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

        var cut = Render<SyncNow>();

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

        var cut = Render<SyncNow>();

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

        var cut = Render<SyncNow>();
        cut.Find("#sync-simplefin-btn").Click();

        Assert.Equal(1, fake.SimpleFinRunCount);
        Assert.Equal(0, fake.AmazonGmailRunCount);
        Assert.Contains("Last synced:", cut.Find("#sync-simplefin-status").TextContent);
    }

    [Fact]
    public void ClickingAmazonButton_TriggersASyncIndependentlyOfSimpleFin()
    {
        var fake = RegisterFakes();

        var cut = Render<SyncNow>();
        cut.Find("#sync-amazon-btn").Click();

        Assert.Equal(1, fake.AmazonGmailRunCount);
        Assert.Equal(0, fake.SimpleFinRunCount);
    }

    [Fact]
    public void NoModalShownBeforeAnAmazonSyncIsStarted()
    {
        RegisterFakes();

        var cut = Render<SyncNow>();

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
        var cut = Render<SyncNow>();

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
        var cut = Render<SyncNow>();

        cut.Find("#sync-amazon-btn").Click();

        var errorLine = cut.Find("#amazon-sync-modal .sync-progress-error");
        Assert.Contains("FAILED: could not parse", errorLine.TextContent);
    }

    [Fact]
    public void AmazonSyncModal_HasNoCloseButtonWhileTheSyncIsStillRunning()
    {
        var fake = RegisterFakes();
        fake.RunGate = new TaskCompletionSource();
        var cut = Render<SyncNow>();

        cut.Find("#sync-amazon-btn").Click();

        Assert.Empty(cut.FindAll("#close-amazon-sync-modal-btn"));

        fake.RunGate.SetResult();
    }

    [Fact]
    public void AmazonSyncModal_ShowsACloseButton_OnceTheSyncCompletes()
    {
        RegisterFakes();

        var cut = Render<SyncNow>();
        cut.Find("#sync-amazon-btn").Click();

        Assert.NotNull(cut.Find("#close-amazon-sync-modal-btn"));
    }

    [Fact]
    public void ClosingTheAmazonSyncModal_HidesIt()
    {
        RegisterFakes();
        var cut = Render<SyncNow>();
        cut.Find("#sync-amazon-btn").Click();

        cut.Find("#close-amazon-sync-modal-btn").Click();

        Assert.Empty(cut.FindAll("#amazon-sync-modal"));
    }

    [Fact]
    public void ClickingSimpleFinSync_DoesNotOpenTheAmazonModal()
    {
        RegisterFakes();
        var cut = Render<SyncNow>();

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

        var cut = Render<SyncNow>();

        Assert.Contains("2 email(s) failed to parse", cut.Markup);
    }

    [Fact]
    public void WithNoSyncIssues_DoesNotShowTheSyncIssuesSection()
    {
        RegisterFakes();

        var cut = Render<SyncNow>();

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

        var cut = Render<SyncNow>();

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
        var cut = Render<SyncNow>();

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
        var cut = Render<SyncNow>();

        cut.Find("#ignore-not-order-btn-1").Click();

        Assert.Empty(cut.FindAll("#sync-issues-section"));
    }

    [Fact]
    public void AfterAmazonSync_RefreshesTheSyncIssuesList()
    {
        var fake = RegisterFakes();
        var cut = Render<SyncNow>();
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
