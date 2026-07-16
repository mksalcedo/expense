using Expense.Domain.Entities;
using Expense.Domain.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Tests.Entities;

public class ImportRunTests : DatabaseTestBase
{
    [Fact]
    public async Task Run_SavedAndReloaded_RoundTripsCorrectly()
    {
        var run = new ImportRun
        {
            Source = ImportSource.SimpleFin,
            RanAt = new DateTimeOffset(2026, 7, 16, 9, 30, 0, TimeSpan.Zero),
            Success = true,
            Summary = "Transactions added: 12, duplicates skipped: 3, balance snapshots added: 2",
            ErrorMessage = null
        };
        Context.ImportRuns.Add(run);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.ImportRuns.SingleAsync(r => r.Id == run.Id);

        Assert.Equal(ImportSource.SimpleFin, reloaded.Source);
        Assert.Equal(new DateTimeOffset(2026, 7, 16, 9, 30, 0, TimeSpan.Zero), reloaded.RanAt);
        Assert.True(reloaded.Success);
        Assert.Equal("Transactions added: 12, duplicates skipped: 3, balance snapshots added: 2", reloaded.Summary);
        Assert.Null(reloaded.ErrorMessage);
    }

    [Fact]
    public async Task Run_CanRecordAFailure()
    {
        var run = new ImportRun
        {
            Source = ImportSource.AmazonGmail,
            RanAt = DateTimeOffset.UtcNow,
            Success = false,
            Summary = null,
            ErrorMessage = "Gmail OAuth token expired - re-run the console importer to re-authorize."
        };
        Context.ImportRuns.Add(run);
        await Context.SaveChangesAsync();

        await using var reloadContext = CreateContextInSameTransaction();
        var reloaded = await reloadContext.ImportRuns.SingleAsync(r => r.Id == run.Id);

        Assert.Equal(ImportSource.AmazonGmail, reloaded.Source);
        Assert.False(reloaded.Success);
        Assert.Contains("re-authorize", reloaded.ErrorMessage);
    }
}
