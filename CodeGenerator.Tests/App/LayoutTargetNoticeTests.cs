using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>What `approve` says when the target it settled on cannot use the layout that was
/// named. The wizard skips the prompt for such a target, but the target can still be CHANGED at
/// approve — the same reason SQLite is re-checked there.</summary>
[TestClass]
public class LayoutTargetNoticeTests
{
    [TestMethod]
    public void Says_nothing_when_the_target_can_use_the_layout()
    {
        var customization = AppCustomization.Default with { Layout = AppLayout.Dashboard };

        Assert.IsNull(BuildAppCommand.LayoutNotice(new WpfTarget(), customization));
        Assert.IsNull(BuildAppCommand.LayoutNotice(new WinFormsTarget(), customization));
    }

    [TestMethod]
    public void Says_nothing_when_no_layout_was_named()
    {
        Assert.IsNull(BuildAppCommand.LayoutNotice(new ConsoleTarget(), AppCustomization.Default));
    }

    [TestMethod]
    public void Warns_when_a_layout_was_named_for_a_target_with_no_window()
    {
        var notice = BuildAppCommand.LayoutNotice(
            new ConsoleTarget(), AppCustomization.Default with { Layout = AppLayout.Sidebar });

        Assert.IsNotNull(notice);
        StringAssert.Contains(notice, "console");
        StringAssert.Contains(notice, "sidebar");
    }

    [TestMethod]
    public void Warns_for_every_windowless_target()
    {
        // Named individually rather than looped over TargetScaffolders, so a target added later
        // arrives here as a decision to make rather than as silence.
        var customization = AppCustomization.Default with { Layout = AppLayout.MasterDetail };

        foreach (var target in new ITargetScaffolder[]
                 { new ConsoleTarget(), new WebApiTarget(), new WorkerTarget(), new ClassLibraryTarget() })
        {
            var notice = BuildAppCommand.LayoutNotice(target, customization);

            Assert.IsNotNull(notice, $"the {target.Key} target has no window and should say so");
            StringAssert.Contains(notice, target.Key);
            StringAssert.Contains(notice, "master-detail");
        }
    }
}
