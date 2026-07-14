using Expense.Domain.Services.Ingestion;

namespace Expense.Domain.Tests.Services;

public class DedupServiceTests
{
    [Theory]
    [InlineData("  KROGER  #421   ", "KROGER #421")]
    [InlineData("kroger #421", "KROGER #421")]
    [InlineData("KROGER    #421", "KROGER #421")]
    public void NormalizeDescription_TrimsCasesAndCollapsesWhitespace(string raw, string expected)
    {
        Assert.Equal(expected, DedupService.NormalizeDescription(raw));
    }

    [Fact]
    public void GenerateFingerprint_SameInputs_ProduceTheSameFingerprint()
    {
        var a = DedupService.GenerateFingerprint(1, new DateOnly(2026, 7, 13), -50.00m, "INGLES");
        var b = DedupService.GenerateFingerprint(1, new DateOnly(2026, 7, 13), -50.00m, "ingles");

        Assert.Equal(a, b);
    }

    [Fact]
    public void GenerateFingerprint_DifferentAccountOrDateOrAmountOrDescription_ProducesDifferentFingerprints()
    {
        var baseline = DedupService.GenerateFingerprint(1, new DateOnly(2026, 7, 13), -50.00m, "INGLES");

        Assert.NotEqual(baseline, DedupService.GenerateFingerprint(2, new DateOnly(2026, 7, 13), -50.00m, "INGLES"));
        Assert.NotEqual(baseline, DedupService.GenerateFingerprint(1, new DateOnly(2026, 7, 14), -50.00m, "INGLES"));
        Assert.NotEqual(baseline, DedupService.GenerateFingerprint(1, new DateOnly(2026, 7, 13), -51.00m, "INGLES"));
        Assert.NotEqual(baseline, DedupService.GenerateFingerprint(1, new DateOnly(2026, 7, 13), -50.00m, "KROGER"));
    }

    [Fact]
    public void GenerateFingerprint_OccurrenceIndex_DisambiguatesGenuineDuplicateCharges()
    {
        // Two identical $12 QuikTrip purchases same day - not the same transaction,
        // so they must get different fingerprints via the source-row-order tiebreaker.
        var first = DedupService.GenerateFingerprint(1, new DateOnly(2026, 7, 13), -12.00m, "QUIKTRIP", occurrenceIndex: 0);
        var second = DedupService.GenerateFingerprint(1, new DateOnly(2026, 7, 13), -12.00m, "QUIKTRIP", occurrenceIndex: 1);

        Assert.NotEqual(first, second);
    }
}
