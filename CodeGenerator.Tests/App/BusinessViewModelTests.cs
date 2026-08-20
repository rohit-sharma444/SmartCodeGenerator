using AppScaffolding;
using AppScaffolding.Screens;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>The pane interface gains exactly two members. The four pre-business layouts bind to
/// the same interface and must ignore both without changing.</summary>
[TestClass]
public class BusinessViewModelTests
{
    [TestMethod]
    public void The_pane_interface_carries_a_role_and_a_count()
    {
        var source = ViewModelTemplates.EntityViewModelInterface("Acme");

        StringAssert.Contains(source, "EntityRole Role { get; }");
        StringAssert.Contains(source, "int Count { get; }");
    }

    /// <summary>The old layouts bind to these, and must be untouched by the additions.</summary>
    [TestMethod]
    public void The_interface_keeps_every_member_the_old_layouts_bind_to()
    {
        var source = ViewModelTemplates.EntityViewModelInterface("Acme");

        foreach (var member in new[]
                 { "Title", "Items", "Selected", "AddCommand", "DeleteCommand", "SaveCommand", "Status" })
        {
            StringAssert.Contains(source, member);
        }
    }

    [TestMethod]
    public void An_entity_view_model_reports_its_record_count()
        => StringAssert.Contains(
            ViewModelTemplates.EntityViewModel("Acme", validation: false),
            "public int Count => Records.Count;");

    /// <summary>Master is the default so the four existing layouts, which never pass a role,
    /// compile and behave exactly as before.</summary>
    [TestMethod]
    public void The_role_defaults_to_master_for_callers_that_do_not_pass_one()
        => StringAssert.Contains(
            ViewModelTemplates.EntityViewModel("Acme", validation: false),
            "EntityRole role = EntityRole.Master");

    /// <summary>The emitted enum is the app's own, in its own namespace — the generator's copy is
    /// not shipped into a generated project.</summary>
    [TestMethod]
    public void The_app_gets_its_own_role_enum()
    {
        var source = ScreenTemplates.RoleEnum("Acme");

        StringAssert.Contains(source, "namespace Acme.Screens;");

        foreach (var value in new[] { "Master", "Transaction", "DocumentLine" })
            StringAssert.Contains(source, value);
    }

    [TestMethod]
    public void Both_desktop_targets_emit_the_role_enum()
    {
        foreach (ITargetScaffolder target in new ITargetScaffolder[]
                 { new WpfTarget(), new WinFormsTarget() })
        {
            var names = target.FrontEndFiles("Shop", ["Order"], AppCustomization.Default)
                .Select(f => f.Name).ToList();

            CollectionAssert.Contains(names, "EntityRole.cs", target.Key);
        }
    }

    /// <summary>
    /// The additions cost the old layouts exactly one emitted file and no behaviour.
    /// </summary>
    /// <remarks>
    /// Role and Count are on the interface every layout binds to, so the enum has to exist in
    /// every generated app — including a `nologin` one. That is the one file a pre-business
    /// layout gains, and it is inert: nothing in those four layouts reads either member.
    /// Whether the app still COMPILES is proved by GeneratedProjectBuildTests, which builds a
    /// real project rather than a subset that never compiled on its own.
    /// </remarks>
    [TestMethod]
    public void An_old_layout_gains_only_the_role_enum()
    {
        var files = new WpfTarget().FrontEndFiles(
            "Shop", ["Order"],
            AppCustomization.Default with { Layout = AppLayout.Tabs, Login = false });

        CollectionAssert.AreEquivalent(
            new[]
            {
                "EntityRole.cs", "RelationshipResolver.cs", "ObservableObject.cs",
                "RelayCommand.cs", "IEntityViewModel.cs", "EntityViewModel.cs",
                "MainViewModel.cs", "MainWindow.xaml", "MainWindow.xaml.cs",
            },
            files.Select(f => f.Name).ToList());
    }
}
