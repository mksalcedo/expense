using Expense.Domain.Entities;
using Expense.Domain.Services.Ingestion;
using Expense.Domain.Services.Ingestion.Amazon;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.Ingestion;

public class SyncIssueServiceTests : DatabaseTestBase
{
    private readonly SyncIssueService _sut = new(new AmazonImportService(new AmazonOrderEmailParser(), new AmazonRefundEmailParser()));

    private SyncIssue MakeIssue(string messageId, SyncIssueResolution resolution = SyncIssueResolution.None) => new()
    {
        Source = ImportSource.AmazonGmail, MessageId = messageId, Subject = "Some order", Reason = "could not parse",
        ReceivedDate = new DateOnly(2026, 7, 18), Resolution = resolution, CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task GetActiveAsync_ReturnsOnlyUnresolvedIssues()
    {
        Context.SyncIssues.AddRange(
            MakeIssue("msg-1"),
            MakeIssue("msg-2", SyncIssueResolution.Resolved),
            MakeIssue("msg-3", SyncIssueResolution.NotAnOrder));
        await Context.SaveChangesAsync();

        var active = await _sut.GetActiveAsync(Context);

        var issue = Assert.Single(active);
        Assert.Equal("msg-1", issue.MessageId);
    }

    [Fact]
    public async Task GetActiveAsync_WhenNoneExist_ReturnsEmpty()
    {
        var active = await _sut.GetActiveAsync(Context);

        Assert.Empty(active);
    }

    [Fact]
    public async Task ResolveAsync_CreatesTheMissingAmazonOrderItem_AndLinksItToTheIssue()
    {
        var issue = MakeIssue("msg-1");
        Context.SyncIssues.Add(issue);
        await Context.SaveChangesAsync();

        await _sut.ResolveAsync(Context, issue.Id, "113-TEST-ORDER", "Some Supplement", 29.99m, 1);

        var reloaded = await Context.SyncIssues.SingleAsync(i => i.Id == issue.Id);
        Assert.Equal(SyncIssueResolution.Resolved, reloaded.Resolution);
        Assert.NotNull(reloaded.ResolvedAmazonOrderItemId);

        var item = await Context.AmazonOrderItems.SingleAsync(i => i.Id == reloaded.ResolvedAmazonOrderItemId);
        Assert.Equal("113-TEST-ORDER", item.OrderId);
        Assert.Equal("Some Supplement", item.ItemTitle);
        Assert.Equal(29.99m, item.Price);
        Assert.Equal(1, item.Quantity);
        Assert.Equal(new DateOnly(2026, 7, 18), item.OrderDate); // the issue's own ReceivedDate, not today
    }

    [Fact]
    public async Task ResolveAsync_RemovesTheIssueFromTheActiveList()
    {
        var issue = MakeIssue("msg-1");
        Context.SyncIssues.Add(issue);
        await Context.SaveChangesAsync();

        await _sut.ResolveAsync(Context, issue.Id, "113-TEST-ORDER", "Some Supplement", 29.99m, 1);

        Assert.Empty(await _sut.GetActiveAsync(Context));
    }

    [Fact]
    public async Task IgnoreAsNotAnOrderAsync_MarksItResolvedWithNoLinkedItem()
    {
        var issue = MakeIssue("msg-1");
        Context.SyncIssues.Add(issue);
        await Context.SaveChangesAsync();

        await _sut.IgnoreAsNotAnOrderAsync(Context, issue.Id);

        var reloaded = await Context.SyncIssues.SingleAsync(i => i.Id == issue.Id);
        Assert.Equal(SyncIssueResolution.NotAnOrder, reloaded.Resolution);
        Assert.Null(reloaded.ResolvedAmazonOrderItemId);
        Assert.Empty(await Context.AmazonOrderItems.ToListAsync());
    }
}
