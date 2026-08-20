using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>
/// A generated application must be leavable.
/// </summary>
/// <remarks>
/// Both desktop entry points loop on <c>Session.SignOutRequested</c>, and nothing ever called
/// <c>Session.RequestSignOut()</c> — a grep found only the definition. The flag was therefore
/// always false, the loop could never iterate, and a signed-in user could quit but never switch
/// account. The login was verified end to end; the way out of it never was.
/// <para>
/// The control lives in the pane's status row rather than beside Add/Delete/Save: signing out is
/// not an action on a record, and the status row is already where "who and what" is shown. The
/// four pre-business layouts share two pane templates per stack, so that is two edits each rather
/// than one per layout.
/// </para>
/// </remarks>
[TestClass]
public class SignOutReachableTests
{
    private static string PaneOf(ITargetScaffolder target, AppLayout layout, string file)
        => target.FrontEndFiles(
                "Shop", ["Order"], AppCustomization.Default with { Layout = layout })
            .Single(f => f.Name == file).Source;

    /// <summary>Every layout, because a user who picked `sidebar` is as stuck as one who did
    /// not.</summary>
    [TestMethod]
    public void Every_wpf_layout_offers_a_way_out()
    {
        foreach (var layout in new[]
                 { AppLayout.Tabs, AppLayout.Sidebar, AppLayout.MasterDetail, AppLayout.Dashboard })
        {
            var xaml = PaneOf(new WpfTarget(), layout, "MainWindow.xaml");

            StringAssert.Contains(xaml, "Sign out", $"{layout} has no sign-out control");
        }
    }

    [TestMethod]
    public void Every_wpf_layout_wires_the_handler()
    {
        foreach (var layout in new[]
                 { AppLayout.Tabs, AppLayout.Sidebar, AppLayout.MasterDetail, AppLayout.Dashboard })
        {
            var code = PaneOf(new WpfTarget(), layout, "MainWindow.xaml.cs");

            StringAssert.Contains(code, "Session.RequestSignOut();", $"{layout} never requests it");
        }
    }

    [TestMethod]
    public void Every_winforms_layout_offers_a_way_out()
    {
        foreach (var layout in new[]
                 { AppLayout.Tabs, AppLayout.Sidebar, AppLayout.MasterDetail, AppLayout.Dashboard })
        {
            var code = PaneOf(new WinFormsTarget(), layout, "MainForm.cs");

            StringAssert.Contains(code, "Session.RequestSignOut();", $"{layout} never requests it");
        }
    }

    /// <summary>
    /// The signed-in name is shown beside it.
    /// </summary>
    /// <remarks>
    /// Session.DisplayName existed, documented as "the name to show in the shell header", and was
    /// rendered by nothing. It reaches the pane through MainViewModel rather than an x:Static, so
    /// no layout needs a new xmlns on its window root.
    /// </remarks>
    [TestMethod]
    public void The_signed_in_name_is_shown()
    {
        StringAssert.Contains(
            PaneOf(new WpfTarget(), AppLayout.Tabs, "MainWindow.xaml"), "SignedInUser");

        StringAssert.Contains(
            PaneOf(new WpfTarget(), AppLayout.Tabs, "MainViewModel.cs"), "Session.DisplayName");

        StringAssert.Contains(
            PaneOf(new WinFormsTarget(), AppLayout.Tabs, "MainForm.cs"), "SignedInUser");
    }

    /// <summary>Requesting a sign-out has to close the shell, or the flag is set and the loop
    /// never gets control back to act on it.</summary>
    [TestMethod]
    public void Requesting_a_sign_out_closes_the_shell()
        => StringAssert.Contains(
            PaneOf(new WpfTarget(), AppLayout.Tabs, "MainWindow.xaml.cs"), "Close();");

    /// <summary>With no login there is nothing to sign out of, and the output must stay exactly
    /// what it was.</summary>
    [TestMethod]
    public void Nologin_adds_no_sign_out_anywhere()
    {
        var noLogin = AppCustomization.Default with { Login = false, Layout = AppLayout.Tabs };

        foreach (ITargetScaffolder target in new ITargetScaffolder[]
                 { new WpfTarget(), new WinFormsTarget() })
        {
            foreach (var file in target.FrontEndFiles("Shop", ["Order"], noLogin))
            {
                Assert.IsFalse(
                    file.Source.Contains("Sign out", StringComparison.Ordinal),
                    $"{target.Key}/{file.Name} mentions signing out");
            }
        }
    }

    /// <summary>The regression proof, restated where it can fail loudly: the tabbed window with
    /// no login is byte-for-byte the one this generator has always produced.</summary>
    [TestMethod]
    public void Nologin_tabs_is_the_original_window()
        => Assert.AreEqual(
            MainWindowTemplate.Xaml("Shop", "Shop"),
            new WpfTarget()
                .FrontEndFiles("Shop", ["Order"],
                    AppCustomization.Default with { Login = false, Layout = AppLayout.Tabs })
                .Single(f => f.Name == "MainWindow.xaml").Source);
}
