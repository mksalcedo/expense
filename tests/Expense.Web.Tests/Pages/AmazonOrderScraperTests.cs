using Bunit;
using Expense.Web.Components.Pages;

namespace Expense.Web.Tests.Pages;

public class AmazonOrderScraperTests : BunitContext
{
    [Fact]
    public void RendersADraggableBookmarkletLink()
    {
        var cut = Render<AmazonOrderScraper>();

        var link = cut.Find("#bookmarklet-link");
        Assert.StartsWith("javascript:", link.GetAttribute("href"));
    }

    [Fact]
    public void ExplainsHowToInstallIt()
    {
        var cut = Render<AmazonOrderScraper>();

        Assert.Contains("bookmarks bar", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Review Queue", cut.Markup);
    }

    [Fact]
    public void MentionsTheScreenshotFallbackStillWorks()
    {
        var cut = Render<AmazonOrderScraper>();

        Assert.Contains("screenshot", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }
}
