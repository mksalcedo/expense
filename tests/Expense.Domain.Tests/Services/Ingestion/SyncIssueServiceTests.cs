using Expense.Domain.Entities;
using Expense.Domain.Services.Ingestion;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Services.Ingestion;

public class SyncIssueServiceTests : DatabaseTestBase
{
    private readonly SyncIssueService _sut = new();

    private SyncIssue MakeIssue(string messageId, bool dismissed = false) => new()
    {
        Source = ImportSource.AmazonGmail, MessageId = messageId, Subject = "Some order", Reason = "could not parse",
        Dismissed = dismissed, CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task GetActiveAsync_ReturnsOnlyNonDismissedIssues()
    {
        Context.SyncIssues.AddRange(MakeIssue("msg-1"), MakeIssue("msg-2", dismissed: true));
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
    public async Task DismissAsync_MarksTheIssueDismissed()
    {
        var issue = MakeIssue("msg-1");
        Context.SyncIssues.Add(issue);
        await Context.SaveChangesAsync();

        await _sut.DismissAsync(Context, issue.Id);

        var reloaded = await Context.SyncIssues.SingleAsync(i => i.Id == issue.Id);
        Assert.True(reloaded.Dismissed);
    }
}
