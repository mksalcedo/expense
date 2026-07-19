using Expense.Domain.Entities;
using Expense.Domain.Services.Ingestion;
using Expense.Domain.Tests.TestSupport;

namespace Expense.Domain.Tests.Services.Ingestion;

public class ImportRunLookupTests : DatabaseTestBase
{
    [Fact]
    public async Task GetLastRunAsync_ReturnsTheMostRecentRunForTheGivenSource_IgnoringOtherSources()
    {
        var olderSimpleFinRun = new DateTimeOffset(2026, 7, 14, 8, 0, 0, TimeSpan.Zero);
        var newestGmailRun = new DateTimeOffset(2026, 7, 16, 9, 0, 0, TimeSpan.Zero);
        var newestSimpleFinRun = new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);

        Context.ImportRuns.AddRange(
            new ImportRun { Source = ImportSource.SimpleFin, RanAt = olderSimpleFinRun, Success = true },
            new ImportRun { Source = ImportSource.AmazonGmail, RanAt = newestGmailRun, Success = true },
            new ImportRun { Source = ImportSource.SimpleFin, RanAt = newestSimpleFinRun, Success = true });
        await Context.SaveChangesAsync();

        var lastSimpleFinRun = await ImportRunLookup.GetLastRunAsync(Context, ImportSource.SimpleFin);
        var lastGmailRun = await ImportRunLookup.GetLastRunAsync(Context, ImportSource.AmazonGmail);

        Assert.NotNull(lastSimpleFinRun);
        Assert.Equal(newestSimpleFinRun, lastSimpleFinRun!.RanAt);
        Assert.NotNull(lastGmailRun);
        Assert.Equal(newestGmailRun, lastGmailRun!.RanAt);
    }

    [Fact]
    public async Task GetLastRunAsync_WhenNoRunsExistForThatSource_ReturnsNull()
    {
        var result = await ImportRunLookup.GetLastRunAsync(Context, ImportSource.SimpleFin);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetLastSuccessfulRunAsync_SkipsFailedRuns_AndReturnsTheMostRecentSuccessForThatSource()
    {
        var failedGmailRun = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);
        var successfulGmailRun = new DateTimeOffset(2026, 7, 16, 9, 0, 0, TimeSpan.Zero);
        var successfulSimpleFinRun = new DateTimeOffset(2026, 7, 18, 8, 0, 0, TimeSpan.Zero);

        Context.ImportRuns.AddRange(
            new ImportRun { Source = ImportSource.AmazonGmail, RanAt = successfulGmailRun, Success = true },
            new ImportRun { Source = ImportSource.AmazonGmail, RanAt = failedGmailRun, Success = false },
            new ImportRun { Source = ImportSource.SimpleFin, RanAt = successfulSimpleFinRun, Success = true });
        await Context.SaveChangesAsync();

        var result = await ImportRunLookup.GetLastSuccessfulRunAsync(Context, ImportSource.AmazonGmail);

        Assert.NotNull(result);
        Assert.Equal(successfulGmailRun, result!.RanAt);
    }

    [Fact]
    public async Task GetLastSuccessfulRunAsync_WhenOnlyFailedRunsExistForThatSource_ReturnsNull()
    {
        Context.ImportRuns.Add(new ImportRun { Source = ImportSource.AmazonGmail, RanAt = DateTimeOffset.UtcNow, Success = false });
        await Context.SaveChangesAsync();

        var result = await ImportRunLookup.GetLastSuccessfulRunAsync(Context, ImportSource.AmazonGmail);

        Assert.Null(result);
    }
}
