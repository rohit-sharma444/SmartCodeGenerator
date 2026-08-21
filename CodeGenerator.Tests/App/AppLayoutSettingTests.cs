using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

[TestClass]
public class AppLayoutSettingTests
{
    [TestMethod]
    public void Default_NamesNoLayout()
    {
        // Null, not Tabs. `layout tabs` is the first inline option whose value equals the
        // default, and BuildAppCommand.Merge decides a setting was typed by comparing against
        // Default — so "not mentioned" has to be a distinct value or a typed `layout tabs`
        // would lose to a remembered profile.
        Assert.IsNull(AppCustomization.Default.Layout);
    }

    [TestMethod]
    public void EffectiveLayout_IsTheBusinessShellWhenNoneWasNamed()
    {
        // What a generated application looks like when the user says nothing about its shape.
        // Tabs is still reachable by name, and is still the output the neutrality tests pin.
        Assert.AreEqual(AppLayout.Business, AppCustomization.Default.EffectiveLayout);
    }

    /// <summary>
    /// The regression proof, in one test: naming tabs AND passing nologin emits exactly the file
    /// set the generator emitted before any of this existed, plus the one file the pane interface
    /// now names.
    /// </summary>
    /// <remarks>
    /// A file set rather than file contents, which the neutrality tests already pin. The direction
    /// that matters is ADDITION: the five screen view models and the shell must not reach a layout
    /// that cannot bind them, or every older layout would carry files nothing references.
    /// <para>
    /// Screens/EntityRole.cs is the one addition, and it is deliberate — IEntityViewModel gained a
    /// Role, so the enum travels with it. The four older layouts ignore both.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void Tabs_with_nologin_emits_the_original_file_set()
    {
        var original = AppCustomization.Default with { Layout = AppLayout.Tabs, Login = false };

        CollectionAssert.AreEquivalent(
            new[]
            {
                "Screens/EntityRole.cs",
                "Data/RelationshipResolver.cs",
                "ViewModels/ObservableObject.cs",
                "ViewModels/RelayCommand.cs",
                "ViewModels/IEntityViewModel.cs",
                "ViewModels/EntityViewModel.cs",
                "ViewModels/MainViewModel.cs",
                "Views/MainWindow.xaml",
                "Views/MainWindow.xaml.cs",
            },
            Paths(new WpfTarget().FrontEndFiles("Shop", ["Order"], original)));
    }

    /// <summary>And the business shell is what carries them.</summary>
    [TestMethod]
    public void The_business_shell_is_what_carries_the_screens()
    {
        var paths = Paths(
            new WpfTarget().FrontEndFiles(
                "Shop", ["Order"], AppCustomization.Default with { Login = false }));

        foreach (var screen in new[]
                 {
                     "ViewModels/RecordEditorViewModel.cs", "ViewModels/MasterScreenViewModel.cs",
                     "ViewModels/TransactionScreenViewModel.cs", "ViewModels/DashboardViewModel.cs",
                     "ViewModels/ShellViewModel.cs",
                 })
        {
            CollectionAssert.Contains(paths, screen);
        }
    }

    private static List<string> Paths(IReadOnlyList<GeneratedFile> files)
        => [.. files.Select(f => f.RelativeFolder + "/" + f.Name)];

    [TestMethod]
    public void EffectiveLayout_IsWhateverWasNamed()
    {
        var customization = AppCustomization.Default with { Layout = AppLayout.Dashboard };

        Assert.AreEqual(AppLayout.Dashboard, customization.EffectiveLayout);
    }

    [TestMethod]
    public void EffectiveLayout_IsTabsWhenTabsWasNamedExplicitly()
    {
        var customization = AppCustomization.Default with { Layout = AppLayout.Tabs };

        Assert.AreEqual(AppLayout.Tabs, customization.EffectiveLayout);
        Assert.IsNotNull(customization.Layout);
    }
}
