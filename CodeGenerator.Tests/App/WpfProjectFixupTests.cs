using System.IO;
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WpfProjectFixupTests
{
    /// <summary>Exactly what `dotnet new wpf -n Payroll` writes for App.xaml.</summary>
    private const string TemplateAppXaml = """
<Application x:Class="Payroll.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:Payroll"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
    </Application.Resources>
</Application>
""";

    private static string NewProjectFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "fixup_" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "App.xaml"), TemplateAppXaml);
        File.WriteAllText(Path.Combine(folder, "MainWindow.xaml"), "<Window />");
        File.WriteAllText(Path.Combine(folder, "MainWindow.xaml.cs"), "// template");
        return folder;
    }

    [TestMethod]
    public void Apply_DeletesTheTemplatesRootWindow()
    {
        var folder = NewProjectFolder();
        try
        {
            WpfProjectFixup.Apply(folder);

            // Left in place, its x:Class duplicates Views/MainWindow and the build fails.
            Assert.IsFalse(File.Exists(Path.Combine(folder, "MainWindow.xaml")));
            Assert.IsFalse(File.Exists(Path.Combine(folder, "MainWindow.xaml.cs")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void Apply_RepointsStartupUriAtTheViewsFolder()
    {
        var folder = NewProjectFolder();
        try
        {
            WpfProjectFixup.Apply(folder);

            var appXaml = File.ReadAllText(Path.Combine(folder, "App.xaml"));
            StringAssert.Contains(appXaml, "StartupUri=\"Views/MainWindow.xaml\"");
            StringAssert.Contains(appXaml, "x:Class=\"Payroll.App\"", "the template's own markup must survive");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void Apply_OnAnAlreadyFixedProject_ChangesNothing()
    {
        var folder = NewProjectFolder();
        try
        {
            WpfProjectFixup.Apply(folder);
            var once = File.ReadAllText(Path.Combine(folder, "App.xaml"));

            WpfProjectFixup.Apply(folder);
            var twice = File.ReadAllText(Path.Combine(folder, "App.xaml"));

            Assert.AreEqual(once, twice, "Apply must be idempotent");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void Apply_WithNoAppXaml_DoesNotThrow()
    {
        var folder = Path.Combine(Path.GetTempPath(), "fixup_" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        try
        {
            WpfProjectFixup.Apply(folder);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
