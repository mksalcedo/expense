using Bunit;
using Expense.Web.Components.Layout;

namespace Expense.Web.Tests.Layout;

public class NavMenuTests : BunitContext
{
    [Fact]
    public void ClickingRefresh_ReloadsThePage()
    {
        var handler = JSInterop.SetupVoid("location.reload");

        var cut = Render<NavMenu>();
        cut.Find("#nav-refresh-btn").Click();

        handler.VerifyInvoke("location.reload");
    }
}
