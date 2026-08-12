using System.Collections.Generic;
using System.IO;
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WpfProjectFixupTests
{
    /// <summary>Exactly what `dotnet new wpf -n Payroll` writes for App.xaml.</summary>
    private const string PayrollAppXaml = """
<Application x:Class="Payroll.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:Payroll"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
    </Application.Resources>
</Application>
""";

    private const string TemplateAppXaml = """
<Application x:Class="Shop.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:Shop"
             StartupUri="Views/MainWindow.xaml">
    <Application.Resources>
    </Application.Resources>
</Application>
""";

    private const string TemplateAppCodeBehind = """
using System.Configuration;
using System.Data;
using System.Windows;

namespace Shop;

public partial class App : Application
{
}
""";

    private readonly List<string> _tempFolders = new();

    /// <summary>Creates a fresh, empty temp folder and tracks it for deletion in
    /// <see cref="CleanupTempFolders"/>.</summary>
    private string NewProjectFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "fixup_" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        _tempFolders.Add(folder);
        return folder;
    }

    /// <summary>A folder seeded with exactly what `dotnet new wpf -n Payroll` emits, for the
    /// <see cref="WpfProjectFixup.Apply"/> tests below.</summary>
    private string NewTemplateProjectFolder()
    {
        var folder = NewProjectFolder();
        File.WriteAllText(Path.Combine(folder, "App.xaml"), PayrollAppXaml);
        File.WriteAllText(Path.Combine(folder, "MainWindow.xaml"), "<Window />");
        File.WriteAllText(Path.Combine(folder, "MainWindow.xaml.cs"), "// template");
        return folder;
    }

    [TestCleanup]
    public void CleanupTempFolders()
    {
        foreach (var folder in _tempFolders)
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void Apply_DeletesTheTemplatesRootWindow()
    {
        var folder = NewTemplateProjectFolder();

        WpfProjectFixup.Apply(folder);

        // Left in place, its x:Class duplicates Views/MainWindow and the build fails.
        Assert.IsFalse(File.Exists(Path.Combine(folder, "MainWindow.xaml")));
        Assert.IsFalse(File.Exists(Path.Combine(folder, "MainWindow.xaml.cs")));
    }

    [TestMethod]
    public void Apply_RepointsStartupUriAtTheViewsFolder()
    {
        var folder = NewTemplateProjectFolder();

        WpfProjectFixup.Apply(folder);

        var appXaml = File.ReadAllText(Path.Combine(folder, "App.xaml"));
        StringAssert.Contains(appXaml, "StartupUri=\"Views/MainWindow.xaml\"");
        StringAssert.Contains(appXaml, "x:Class=\"Payroll.App\"", "the template's own markup must survive");
    }

    [TestMethod]
    public void Apply_OnAnAlreadyFixedProject_ChangesNothing()
    {
        var folder = NewTemplateProjectFolder();

        WpfProjectFixup.Apply(folder);
        var once = File.ReadAllText(Path.Combine(folder, "App.xaml"));

        WpfProjectFixup.Apply(folder);
        var twice = File.ReadAllText(Path.Combine(folder, "App.xaml"));

        Assert.AreEqual(once, twice, "Apply must be idempotent");
    }

    [TestMethod]
    public void Apply_WithNoAppXaml_DoesNotThrow()
        => WpfProjectFixup.Apply(NewProjectFolder());

    /// <summary>
    /// App.xaml and its code-behind come from `dotnet new wpf`, which named them after the
    /// project. With a prefix, x:Class has to name the namespace the generated views declare or
    /// the build fails — and the rewrite has to work from the CURRENT text, not a remembered
    /// value, because it also runs when re-scaffolding changes the prefix.
    /// </summary>
    [TestMethod]
    public void ReconcileNamespace_RepointsTheApplicationClassAndItsCodeBehind()
    {
        var folder = NewProjectFolder();
        File.WriteAllText(Path.Combine(folder, "App.xaml"), TemplateAppXaml);
        File.WriteAllText(Path.Combine(folder, "App.xaml.cs"), TemplateAppCodeBehind);

        WpfProjectFixup.ReconcileNamespace(folder, "CodisLtd.Shop");

        var xaml = File.ReadAllText(Path.Combine(folder, "App.xaml"));
        var code = File.ReadAllText(Path.Combine(folder, "App.xaml.cs"));

        StringAssert.Contains(xaml, "x:Class=\"CodisLtd.Shop.App\"");
        StringAssert.Contains(xaml, "clr-namespace:CodisLtd.Shop\"");
        StringAssert.Contains(code, "namespace CodisLtd.Shop;");

        // The rest of the file is untouched — the StartupUri the other fixup repointed above all.
        StringAssert.Contains(xaml, "StartupUri=\"Views/MainWindow.xaml\"");
    }

    [TestMethod]
    public void ReconcileNamespace_IsIdempotent()
    {
        var folder = NewProjectFolder();
        File.WriteAllText(Path.Combine(folder, "App.xaml"), TemplateAppXaml);
        File.WriteAllText(Path.Combine(folder, "App.xaml.cs"), TemplateAppCodeBehind);

        WpfProjectFixup.ReconcileNamespace(folder, "CodisLtd.Shop");
        var once = File.ReadAllText(Path.Combine(folder, "App.xaml"));
        WpfProjectFixup.ReconcileNamespace(folder, "CodisLtd.Shop");

        Assert.AreEqual(once, File.ReadAllText(Path.Combine(folder, "App.xaml")));
    }

    /// <summary>The trap: a second run with a different prefix must reach the value the first run
    /// wrote, which no remembered "old" string would find.</summary>
    [TestMethod]
    public void ReconcileNamespace_FollowsAChangedPrefix()
    {
        var folder = NewProjectFolder();
        File.WriteAllText(Path.Combine(folder, "App.xaml"), TemplateAppXaml);
        File.WriteAllText(Path.Combine(folder, "App.xaml.cs"), TemplateAppCodeBehind);

        WpfProjectFixup.ReconcileNamespace(folder, "CodisLtd.Shop");
        WpfProjectFixup.ReconcileNamespace(folder, "Shop");

        var xaml = File.ReadAllText(Path.Combine(folder, "App.xaml"));

        StringAssert.Contains(xaml, "x:Class=\"Shop.App\"");
        StringAssert.Contains(xaml, "clr-namespace:Shop\"");
        Assert.IsFalse(xaml.Contains("CodisLtd", StringComparison.Ordinal));
        StringAssert.Contains(
            File.ReadAllText(Path.Combine(folder, "App.xaml.cs")), "namespace Shop;");
    }

    [TestMethod]
    public void ReconcileNamespace_OnAFolderWithNoAppXaml_DoesNothing()
        => WpfProjectFixup.ReconcileNamespace(NewProjectFolder(), "CodisLtd.Shop");

    private const string TemplateAppXamlWithConverterNamespace = """
<Application x:Class="Shop.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:Shop"
             xmlns:conv="clr-namespace:Shop.Converters"
             StartupUri="Views/MainWindow.xaml">
    <Application.Resources>
    </Application.Resources>
</Application>
""";

    /// <summary>The trap: `dotnet new wpf` emits exactly one `clr-namespace:`, but a hand-added
    /// converter or resource xmlns is a routine WPF addition between scaffolds. Only the
    /// xmlns:local the template itself wrote is ours to rewrite.</summary>
    [TestMethod]
    public void ReconcileNamespace_LeavesAHandAddedClrNamespaceXmlnsAlone()
    {
        var folder = NewProjectFolder();
        File.WriteAllText(Path.Combine(folder, "App.xaml"), TemplateAppXamlWithConverterNamespace);

        WpfProjectFixup.ReconcileNamespace(folder, "CodisLtd.Shop");

        var xaml = File.ReadAllText(Path.Combine(folder, "App.xaml"));

        StringAssert.Contains(xaml, "xmlns:local=\"clr-namespace:CodisLtd.Shop\"");
        StringAssert.Contains(xaml, "xmlns:conv=\"clr-namespace:Shop.Converters\"");
    }
}
