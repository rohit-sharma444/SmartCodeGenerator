using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Targets;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// What a finished build says about the screens it wrote.
/// </summary>
/// <remarks>
/// The line used to print whenever the classifier found anything, on every target and every
/// layout. A `layout dashboard` run announced "8 masters, 0 transactions" while emitting a
/// window that has neither — reported from real use, and the reason the shipped business shell
/// looked to its user as if it had never arrived. The summary is only true where the roles
/// actually reach a screen.
/// </remarks>
[TestClass]
public class ScreensLineTests
{
    private const string Summary = "2 masters, 1 transaction (Order has 1 line)";

    [TestMethod]
    public void The_business_shell_reports_the_split()
    {
        var line = BuildAppCommand.ScreensLine(new WpfTarget(), AppCustomization.Default, Summary);

        Assert.IsNotNull(line);
        StringAssert.Contains(line, "Screens:");
        StringAssert.Contains(line, Summary);
    }

    /// <summary>The four older layouts bind EntityViewModel: there is no master or transaction
    /// screen to have been written, whatever the classifier decided.</summary>
    [TestMethod]
    public void An_older_layout_reports_the_layout_instead_of_the_split()
    {
        foreach (var layout in new[]
                 { AppLayout.Tabs, AppLayout.Sidebar, AppLayout.MasterDetail, AppLayout.Dashboard })
        {
            var customization = AppCustomization.Default with { Layout = layout };
            var line = BuildAppCommand.ScreensLine(new WpfTarget(), customization, Summary);

            Assert.IsNotNull(line, $"the {layout} layout says nothing about what it wrote");
            StringAssert.Contains(line, AppLayoutCatalog.KeyFor(layout));

            // The claim, not just the counts: a line saying "Screens:" is what sent a user
            // looking for screens the layout cannot show.
            Assert.DoesNotContain("Screens:", line, StringComparison.Ordinal);
            Assert.DoesNotContain(Summary, line, StringComparison.Ordinal);
        }
    }

    /// <summary>Both desktop targets have the shell, so both report it the same way.</summary>
    [TestMethod]
    public void Winforms_reads_the_same_as_wpf()
    {
        var business = BuildAppCommand.ScreensLine(
            new WinFormsTarget(), AppCustomization.Default, Summary);
        var older = BuildAppCommand.ScreensLine(
            new WinFormsTarget(), AppCustomization.Default with { Layout = AppLayout.Tabs }, Summary);

        StringAssert.Contains(business, "Screens:");
        Assert.DoesNotContain("Screens:", older!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The web app is always the shell, so naming a layout cannot take its screens away.
    /// </summary>
    /// <remarks>
    /// The layout options belong to the two desktop targets, and `approve` already says a named
    /// layout does not apply here — but the master and transaction screens are still exactly what
    /// was written, so the split is still worth reporting.
    /// </remarks>
    [TestMethod]
    public void The_web_app_reports_the_split_whatever_layout_was_named()
    {
        var named = AppCustomization.Default with { Layout = AppLayout.Dashboard };

        StringAssert.Contains(
            BuildAppCommand.ScreensLine(new BlazorTarget(), AppCustomization.Default, Summary),
            "Screens:");
        StringAssert.Contains(
            BuildAppCommand.ScreensLine(new BlazorTarget(), named, Summary), "Screens:");
    }

    /// <summary>A target with no front end at all says nothing: there were never any screens,
    /// and the classifier ran only because the summary is computed for every build.</summary>
    [TestMethod]
    public void A_target_with_no_screens_says_nothing()
    {
        foreach (var target in new ITargetScaffolder[]
                 { new ConsoleTarget(), new WebApiTarget(), new WorkerTarget(), new ClassLibraryTarget() })
        {
            Assert.IsNull(
                BuildAppCommand.ScreensLine(target, AppCustomization.Default, Summary),
                $"the {target.Key} target has no screens and should not describe any");
        }
    }

    /// <summary>An empty model classifies to nothing, and a line reading "Screens: " is worse
    /// than no line — the same guard the printed version already had.</summary>
    [TestMethod]
    public void Nothing_classified_says_nothing()
    {
        Assert.IsNull(BuildAppCommand.ScreensLine(new WpfTarget(), AppCustomization.Default, ""));
        Assert.IsNull(
            BuildAppCommand.ScreensLine(
                new WpfTarget(), AppCustomization.Default with { Layout = AppLayout.Tabs }, ""));
    }
}
