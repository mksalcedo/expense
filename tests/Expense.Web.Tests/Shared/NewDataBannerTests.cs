using Bunit;
using Expense.Web.Components.Shared;

namespace Expense.Web.Tests.Shared;

public class NewDataBannerTests : BunitContext
{
    [Fact]
    public void Show_False_RendersNothing()
    {
        var cut = Render<NewDataBanner>(p => p.Add(b => b.Show, false));

        Assert.Empty(cut.FindAll("#new-data-banner"));
    }

    [Fact]
    public void Show_True_RendersTheBannerWithARefreshButton()
    {
        var cut = Render<NewDataBanner>(p => p.Add(b => b.Show, true));

        Assert.NotEmpty(cut.FindAll("#new-data-banner"));
        Assert.NotEmpty(cut.FindAll("#refresh-now-btn"));
    }

    [Fact]
    public void ClickingRefresh_InvokesTheOnRefreshCallback()
    {
        var refreshed = false;
        var cut = Render<NewDataBanner>(p => p
            .Add(b => b.Show, true)
            .Add(b => b.OnRefresh, () => refreshed = true));

        cut.Find("#refresh-now-btn").Click();

        Assert.True(refreshed);
    }
}
