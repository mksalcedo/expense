using Expense.Domain.Entities;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Entities;

public class ImportRunProgressLineTests : DatabaseTestBase
{
    [Fact]
    public async Task ProgressLines_SavedViaTheParentRun_RoundTripInOrder()
    {
        var run = new ImportRun
        {
            Source = ImportSource.AmazonGmail,
            RanAt = new DateTimeOffset(2026, 7, 22, 6, 0, 0, TimeSpan.Zero),
            Success = true,
            ProgressLines =
            [
                new ImportRunProgressLine { Sequence = 0, Text = "Found 1 order confirmation email(s) to check.", IsError = false },
                new ImportRunProgressLine { Sequence = 1, Text = "FAILED: could not parse", IsError = true }
            ]
        };
        Context.ImportRuns.Add(run);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.ImportRuns
            .Include(r => r.ProgressLines)
            .SingleAsync(r => r.Id == run.Id);

        var orderedLines = reloaded.ProgressLines.OrderBy(l => l.Sequence).ToList();
        Assert.Equal(2, orderedLines.Count);
        Assert.Equal("Found 1 order confirmation email(s) to check.", orderedLines[0].Text);
        Assert.False(orderedLines[0].IsError);
        Assert.Equal("FAILED: could not parse", orderedLines[1].Text);
        Assert.True(orderedLines[1].IsError);
    }

    [Fact]
    public async Task DeletingTheParentRun_CascadesToDeleteItsProgressLines()
    {
        var run = new ImportRun
        {
            Source = ImportSource.SimpleFin,
            RanAt = DateTimeOffset.UtcNow,
            Success = true,
            ProgressLines = [new ImportRunProgressLine { Sequence = 0, Text = "line 1" }]
        };
        Context.ImportRuns.Add(run);
        await Context.SaveChangesAsync();

        Context.ImportRuns.Remove(run);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        Assert.Equal(0, await reloadContext.ImportRunProgressLines.CountAsync());
    }
}
