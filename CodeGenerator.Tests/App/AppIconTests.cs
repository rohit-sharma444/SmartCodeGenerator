using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>The icon is copied into the project, and only a .ico becomes ApplicationIcon —
/// MSBuild rejects a .png there.</summary>
/// <remarks>
/// Registering the copied file in the project is not decoration: WPF resolves a relative
/// <c>Icon</c> attribute through the pack URI, so the file has to be a <c>Resource</c>, and the
/// WinForms form loads it off disk, so its copy has to be <c>Content</c> copied to the output
/// directory. Without either, the app builds and then fails to find its icon at run time.
/// </remarks>
[TestClass]
public class AppIconTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateWorkspace()
    {
        _root = Path.Combine(Path.GetTempPath(), "ico-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveWorkspace()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string WriteIcon(string extension)
    {
        var path = Path.Combine(_root, "brand" + extension);
        File.WriteAllBytes(path, [0, 0, 1, 0]);
        return path;
    }

    private ScaffoldResult ScaffoldWith(ITargetScaffolder target, string? iconPath)
        => new AppScaffolder().Scaffold(
            ScaffoldRequest.For(TestModels.OneEntity(), "Shop", _root, target)
                with { Customization = AppCustomization.Default with { IconPath = iconPath } });

    [TestMethod]
    public void An_ico_becomes_the_application_icon()
    {
        var updated = CsprojEditing.InjectApplicationIcon(
            "<Project><PropertyGroup></PropertyGroup></Project>", "Assets\\app.ico");

        StringAssert.Contains(updated, "<ApplicationIcon>Assets\\app.ico</ApplicationIcon>");
    }

    [TestMethod]
    public void Application_icon_injection_is_idempotent()
    {
        var once = CsprojEditing.InjectApplicationIcon("<Project></Project>", "Assets\\app.ico");
        var twice = CsprojEditing.InjectApplicationIcon(once, "Assets\\app.ico");

        Assert.AreEqual(once, twice);
    }

    [TestMethod]
    public void A_missing_icon_file_is_ignored_rather_than_failing_the_build()
    {
        var result = ScaffoldWith(new ClassLibraryTarget(), "C:\\nope\\x.ico");

        Assert.IsTrue(result.Success, result.Message);
    }

    [TestMethod]
    public void A_supplied_icon_is_copied_into_assets()
    {
        var result = ScaffoldWith(new WpfTarget(), WriteIcon(".ico"));
        var csproj = File.ReadAllText(Path.Combine(result.ProjectPath, "Shop.csproj"));

        Assert.IsTrue(File.Exists(Path.Combine(result.ProjectPath, "Assets", "app.ico")));
        StringAssert.Contains(csproj, "<ApplicationIcon>Assets\\app.ico</ApplicationIcon>");

        // The window's Icon="Assets/app.ico" is a pack URI, which only resolves for a file the
        // project embeds as a Resource.
        StringAssert.Contains(csproj, "<Resource Include=\"Assets\\app.ico\" />");
    }

    [TestMethod]
    public void A_png_is_copied_but_is_not_the_application_icon()
    {
        var result = ScaffoldWith(new WpfTarget(), WriteIcon(".png"));
        var csproj = File.ReadAllText(Path.Combine(result.ProjectPath, "Shop.csproj"));

        Assert.IsTrue(File.Exists(Path.Combine(result.ProjectPath, "Assets", "app.png")));
        Assert.IsFalse(csproj.Contains("<ApplicationIcon>", StringComparison.Ordinal));

        // A PNG cannot be the exe's icon, but it can still be the window's.
        StringAssert.Contains(csproj, "<Resource Include=\"Assets\\app.png\" />");
    }

    /// <summary>The WinForms form reads the file off disk, so the copy has to travel to the
    /// output directory beside the binary — the same registration the seed files get.</summary>
    [TestMethod]
    public void A_winforms_icon_is_copied_to_the_output_directory()
    {
        var result = ScaffoldWith(new WinFormsTarget(), WriteIcon(".ico"));
        var csproj = File.ReadAllText(Path.Combine(result.ProjectPath, "Shop.WinForms.csproj"));

        StringAssert.Contains(
            csproj,
            "<Content Include=\"Assets\\app.ico\" CopyToOutputDirectory=\"PreserveNewest\" />");
    }

    [TestMethod]
    public void No_icon_leaves_the_project_file_alone()
    {
        var result = ScaffoldWith(new WpfTarget(), iconPath: null);
        var csproj = File.ReadAllText(Path.Combine(result.ProjectPath, "Shop.csproj"));

        Assert.IsFalse(csproj.Contains("ApplicationIcon", StringComparison.Ordinal));
        Assert.IsFalse(csproj.Contains("Assets", StringComparison.Ordinal));
        Assert.IsFalse(Directory.Exists(Path.Combine(result.ProjectPath, "Assets")));
    }

    [TestMethod]
    public void A_wpf_window_shows_the_icon()
    {
        var customization = AppCustomization.Default with { IconPath = "C:\\brand\\logo.ico" };

        var xaml = new WpfTarget().FrontEndFiles("Shop", ["Order"], customization)
            .Single(f => f.Name == "MainWindow.xaml").Source;

        StringAssert.Contains(xaml, "Icon=\"Assets/app.ico\"");
    }

    [TestMethod]
    public void An_un_iconed_window_is_unchanged()
        => Assert.IsFalse(
            new WpfTarget().FrontEndFiles("Shop", ["Order"], AppCustomization.Default)
                .Single(f => f.Name == "MainWindow.xaml").Source
                .Contains("Icon=", StringComparison.Ordinal));

    /// <summary>Guarded with File.Exists: a user who deletes the asset gets an app with the
    /// default icon, not one that throws on startup.</summary>
    [TestMethod]
    public void A_winforms_form_loads_an_ico_from_beside_its_binary()
    {
        var customization = AppCustomization.Default with { IconPath = "C:\\brand\\logo.ico" };

        var source = new WinFormsTarget().FrontEndFiles("Shop", ["Order"], customization)
            .Single(f => f.Name == "MainForm.cs").Source;

        StringAssert.Contains(source, "\"Assets\", \"app.ico\"");
        StringAssert.Contains(source, "System.IO.File.Exists(iconPath)");
        StringAssert.Contains(source, "Icon = new Icon(iconPath);");
    }

    /// <summary>Form.Icon is an Icon, and a PNG is not one. Converting it would mean a bitmap
    /// handle to own and free, so a non-.ico is copied for the exe/window elsewhere and the form
    /// keeps its default.</summary>
    [TestMethod]
    public void A_winforms_form_ignores_a_png()
        => Assert.IsFalse(
            new WinFormsTarget()
                .FrontEndFiles(
                    "Shop", ["Order"],
                    AppCustomization.Default with { IconPath = "C:\\brand\\logo.png" })
                .Single(f => f.Name == "MainForm.cs").Source
                .Contains("new Icon(", StringComparison.Ordinal));

    [TestMethod]
    public void An_un_iconed_form_is_unchanged()
        => Assert.IsFalse(
            new WinFormsTarget().FrontEndFiles("Shop", ["Order"], AppCustomization.Default)
                .Single(f => f.Name == "MainForm.cs").Source
                .Contains("Icon", StringComparison.Ordinal));

    [TestMethod]
    public void The_copied_file_name_keeps_only_the_supplied_extension()
    {
        Assert.AreEqual(
            "app.ico",
            (AppCustomization.Default with { IconPath = "C:\\brand\\My Logo.ICO" }).IconFileName);

        Assert.IsNull(AppCustomization.Default.IconFileName);

        // Nothing downstream can do anything useful with an extensionless file: it cannot be an
        // ApplicationIcon and neither front end could name it.
        Assert.IsNull(
            (AppCustomization.Default with { IconPath = "C:\\brand\\logo" }).IconFileName);
    }
}
