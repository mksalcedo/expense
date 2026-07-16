using Expense.Domain.Services.Export;

namespace Expense.Domain.Tests.Services.Export;

public class ExportFileNamerTests
{
    [Fact]
    public void GetNextFileName_FirstCallForADate_ReturnsThePlainDatedName()
    {
        var namer = new ExportFileNamer();

        var name = namer.GetNextFileName(new DateOnly(2026, 7, 16));

        Assert.Equal("Forecast-2026-07-16.xlsx", name);
    }

    [Fact]
    public void GetNextFileName_SecondCallForTheSameDate_AppendsRev2()
    {
        var namer = new ExportFileNamer();
        namer.GetNextFileName(new DateOnly(2026, 7, 16));

        var name = namer.GetNextFileName(new DateOnly(2026, 7, 16));

        Assert.Equal("Forecast-2026-07-16-rev-2.xlsx", name);
    }

    [Fact]
    public void GetNextFileName_ThirdCallForTheSameDate_AppendsRev3()
    {
        var namer = new ExportFileNamer();
        namer.GetNextFileName(new DateOnly(2026, 7, 16));
        namer.GetNextFileName(new DateOnly(2026, 7, 16));

        var name = namer.GetNextFileName(new DateOnly(2026, 7, 16));

        Assert.Equal("Forecast-2026-07-16-rev-3.xlsx", name);
    }

    [Fact]
    public void GetNextFileName_ADifferentDate_ResetsToThePlainName()
    {
        var namer = new ExportFileNamer();
        namer.GetNextFileName(new DateOnly(2026, 7, 16));
        namer.GetNextFileName(new DateOnly(2026, 7, 16));

        var name = namer.GetNextFileName(new DateOnly(2026, 7, 17));

        Assert.Equal("Forecast-2026-07-17.xlsx", name);
    }
}
