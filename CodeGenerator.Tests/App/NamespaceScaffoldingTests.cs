using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>
/// A prefixed app declares every namespace under the prefix, and an unprefixed one is unchanged.
/// </summary>
/// <remarks>
/// The three parameters that serve double duty are the reason this class exists: two templates
/// compare their title against the project name to decide whether anything was branded, and
/// AppDbContext names the SQLite file after it. Handing any of them a namespace changes behaviour
/// no namespace should touch.
/// </remarks>
[TestClass]
public class NamespaceScaffoldingTests
{
    private static readonly AppCustomization Prefixed =
        AppCustomization.Default with { NamespacePrefix = "CodisLtd" };

    private string _root = string.Empty;

    [TestInitialize]
    public void CreateWorkspace()
    {
        _root = Path.Combine(Path.GetTempPath(), "ns-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveWorkspace()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private ScaffoldResult Scaffold(ITargetScaffolder target, AppCustomization customization)
        => new AppScaffolder().Scaffold(
            ScaffoldRequest.For(TestModels.OneEntity(), "Shop", _root, target)
                with { Customization = customization });

    [TestMethod]
    public void Every_emitted_namespace_sits_under_the_prefix()
    {
        var result = Scaffold(new ConsoleTarget(), Prefixed);
        var project = result.ProjectPath;

        Assert.IsTrue(result.Success, result.Message);

        StringAssert.Contains(
            File.ReadAllText(Path.Combine(project, "Models", "Order.cs")),
            "namespace CodisLtd.Shop.Console.Models");
        StringAssert.Contains(
            File.ReadAllText(Path.Combine(project, "Data", "Repository.cs")),
            "namespace CodisLtd.Shop.Console.Data");
        StringAssert.Contains(
            File.ReadAllText(Path.Combine(project, "Validation", "ValidatorFactory.cs")),
            "namespace CodisLtd.Shop.Console.Validation");
        StringAssert.Contains(
            File.ReadAllText(Path.Combine(project, "Program.cs")),
            "using CodisLtd.Shop.Console.Data;");
    }

    /// <summary>The project's identity is not a namespace: the folder, the csproj and the assembly
    /// keep the name the user's description produced.</summary>
    [TestMethod]
    public void The_project_identity_is_untouched()
    {
        var result = Scaffold(new ConsoleTarget(), Prefixed);

        Assert.AreEqual(Path.Combine(_root, "Shop.Console"), result.ProjectPath);
        Assert.IsTrue(File.Exists(Path.Combine(result.ProjectPath, "Shop.Console.csproj")));
    }

    /// <summary>The gate that decides whether anything was branded compares the title against the
    /// PROJECT NAME. Given a namespace instead, a prefixed but unbranded console app would gain a
    /// banner it has never had.</summary>
    [TestMethod]
    public void A_prefixed_but_unbranded_console_app_still_has_no_banner()
    {
        var source = new ConsoleTarget()
            .FrontEndFiles("Shop", ["Order"], Prefixed)
            .Single(f => f.Name == "Program.cs").Source;

        Assert.IsFalse(source.Contains("Console.WriteLine(\"Shop\")", StringComparison.Ordinal));
        Assert.IsFalse(
            source.Contains("Console.WriteLine(\"CodisLtd.Shop\")", StringComparison.Ordinal));
    }

    [TestMethod]
    public void A_prefixed_but_unbranded_web_api_still_has_no_root_endpoint()
        => Assert.IsFalse(
            new WebApiTarget().FrontEndFiles("Shop", ["Order"], Prefixed)
                .Single(f => f.Name == "Program.cs").Source
                .Contains("app.MapGet(\"/\"", StringComparison.Ordinal));

    /// <summary>The window title is the app's name, not its namespace.</summary>
    [TestMethod]
    public void A_prefixed_window_is_titled_for_the_app_and_classed_for_the_namespace()
    {
        var xaml = new WpfTarget().FrontEndFiles("Shop", ["Order"], Prefixed)
            .Single(f => f.Name == "MainWindow.xaml").Source;

        StringAssert.Contains(xaml, "x:Class=\"CodisLtd.Shop.Views.MainWindow\"");
        StringAssert.Contains(xaml, "Title=\"Shop\"");
    }

    /// <summary>AppDbContext names the database file after the project. A prefix must not rename
    /// the file the app has been reading since it was generated.</summary>
    [TestMethod]
    public void A_prefixed_sqlite_app_keeps_its_database_file_name()
    {
        var source = DbContextTemplate.Source(
            "Shop", ["Order"], "SHOP_DB", rootNamespace: "CodisLtd.Shop");

        StringAssert.Contains(source, "namespace CodisLtd.Shop.Data;");
        StringAssert.Contains(source, "\"Shop.db\"");
        StringAssert.Contains(source, "Environment.GetEnvironmentVariable(\"SHOP_DB\")");
        Assert.IsFalse(source.Contains("CodisLtd.Shop.db", StringComparison.Ordinal));
    }

    /// <summary>Regression guard for the one call site where <c>projectName</c> and
    /// <c>rootNamespace</c> are passed to the same method (<c>DbContextTemplate.Source</c> from
    /// <c>AppScaffolder</c>): with <c>Default</c> customization the two are equal, so only a
    /// prefixed, SQLite-backed run through the actual scaffolder call site can catch the
    /// arguments being swapped or the trailing one dropped.</summary>
    [TestMethod]
    public void A_prefixed_sqlite_app_keeps_its_database_file_and_env_var()
    {
        var result = new AppScaffolder().Scaffold(
            ScaffoldRequest.For(TestModels.OneEntity(), "Shop", _root, new ConsoleTarget())
                with { Customization = Prefixed, Persistence = PersistenceKind.Sqlite });

        Assert.IsTrue(result.Success, result.Message);

        var source = File.ReadAllText(Path.Combine(result.ProjectPath, "Data", "AppDbContext.cs"));

        StringAssert.Contains(source, "namespace CodisLtd.Shop.Console.Data;");
        StringAssert.Contains(source, "\"Shop.Console.db\"");
        StringAssert.Contains(source, "Environment.GetEnvironmentVariable(\"SHOP_CONSOLE_DB\")");
        Assert.IsFalse(source.Contains("CodisLtd.Shop.Console.db", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Without_a_prefix_nothing_moves()
    {
        var result = Scaffold(new ConsoleTarget(), AppCustomization.Default);

        StringAssert.Contains(
            File.ReadAllText(Path.Combine(result.ProjectPath, "Models", "Order.cs")),
            "namespace Shop.Console.Models");
        Assert.IsFalse(
            File.ReadAllText(Path.Combine(result.ProjectPath, "Data", "Repository.cs"))
                .Contains("CodisLtd", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Both_project_files_declare_the_root_namespace()
    {
        var result = Scaffold(new ConsoleTarget(), Prefixed);

        StringAssert.Contains(
            File.ReadAllText(Path.Combine(result.ProjectPath, "Shop.Console.csproj")),
            "<RootNamespace>CodisLtd.Shop.Console</RootNamespace>");

        Assert.IsNotNull(result.TestProjectPath);
        StringAssert.Contains(
            File.ReadAllText(result.TestProjectPath),
            "<RootNamespace>CodisLtd.Shop.Console.Tests</RootNamespace>");
    }

    /// <summary>Without a prefix the element is not written at all: MSBuild already defaults it to
    /// the project name, and writing it would change a project file this generator has always
    /// left alone.</summary>
    [TestMethod]
    public void Without_a_prefix_no_root_namespace_is_written()
    {
        var result = Scaffold(new ConsoleTarget(), AppCustomization.Default);

        Assert.IsFalse(
            File.ReadAllText(Path.Combine(result.ProjectPath, "Shop.Console.csproj"))
                .Contains("RootNamespace", StringComparison.Ordinal));
    }

    /// <summary>The staleness trap this design exists to prevent: regenerating over an existing
    /// project with a CHANGED prefix must leave nothing pointing at the old one.</summary>
    [TestMethod]
    public void Re_scaffolding_with_a_changed_prefix_updates_the_root_namespace()
    {
        Scaffold(new ConsoleTarget(), Prefixed);
        var result = Scaffold(
            new ConsoleTarget(), AppCustomization.Default with { NamespacePrefix = "Acme" });

        var csproj = File.ReadAllText(Path.Combine(result.ProjectPath, "Shop.Console.csproj"));

        StringAssert.Contains(csproj, "<RootNamespace>Acme.Shop.Console</RootNamespace>");
        Assert.IsFalse(csproj.Contains("CodisLtd", StringComparison.Ordinal));
        StringAssert.Contains(
            File.ReadAllText(Path.Combine(result.ProjectPath, "Models", "Order.cs")),
            "namespace Acme.Shop.Console.Models");
    }

    [TestMethod]
    public void Re_scaffolding_without_a_prefix_drops_the_root_namespace()
    {
        Scaffold(new ConsoleTarget(), Prefixed);
        var result = Scaffold(new ConsoleTarget(), AppCustomization.Default);

        Assert.IsFalse(
            File.ReadAllText(Path.Combine(result.ProjectPath, "Shop.Console.csproj"))
                .Contains("RootNamespace", StringComparison.Ordinal));
    }
}
