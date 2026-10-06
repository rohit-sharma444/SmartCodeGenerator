using AppScaffolding;
using AppScaffolding.Screens;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The web app's sidebar. Both defects here were seen on a running app at 1280x720: the group
/// headings were a grey meant for a light page on the template's dark sidebar, and the sign-out
/// strip sat below the sticky 100vh sidebar, off screen.
/// </summary>
[TestClass]
public class BlazorNavMenuTests
{
    private static readonly string[] Classes = ["Customer", "Order"];

    private static string Menu(bool login)
        => BlazorShellTemplate.NavMenu("Shop", Classes, login, EntityRoleMap.Empty);

    [TestMethod]
    public void Group_headings_are_light_on_the_dark_sidebar()
    {
        var menu = Menu(login: true);

        Assert.DoesNotContain("text-body-secondary", menu);
        StringAssert.Contains(menu, "text-uppercase small text-light");
    }

    /// <summary>
    /// The template makes every <c>nav-scrollable</c> block 100vh - 3.5rem tall on a wide screen,
    /// so a second one after the nav starts a whole screen down. There must be one, holding both
    /// the nav and the sign-out strip, with the strip after the nav's end.
    /// </summary>
    [TestMethod]
    public void Sign_out_sits_inside_the_one_scrollable_column_after_the_nav()
    {
        var menu = Menu(login: true);

        Assert.AreEqual(1, CountOf(menu, "class=\"nav-scrollable"));

        var navEnd = menu.IndexOf("</nav>", StringComparison.Ordinal);
        var signOut = menu.IndexOf("Sign out", StringComparison.Ordinal);
        var columnEnd = menu.LastIndexOf("</div>", StringComparison.Ordinal);

        Assert.IsGreaterThan(navEnd, signOut);
        Assert.IsLessThan(columnEnd, signOut);
        StringAssert.Contains(menu, "flex-shrink-0");
    }

    [TestMethod]
    public void A_menu_with_no_login_has_no_sign_out()
        => Assert.DoesNotContain("Sign out", Menu(login: false));

    private static int CountOf(string text, string part)
    {
        var count = 0;

        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
            count++;

        return count;
    }
}
