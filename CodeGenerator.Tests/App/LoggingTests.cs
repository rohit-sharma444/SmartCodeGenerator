using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>Logging is off by default; with it on the app gains AppLog and its packages.</summary>
/// <remarks>
/// The plan offered a Roslyn compile test for <c>AppLog.cs</c> or a real build, and this is the
/// real build: <c>Compiler.Compile</c> references this process's trusted platform assemblies, and
/// <c>Microsoft.Extensions.Logging</c> is not among them, so the compile test would have meant
/// three package references on the test project purely to type-check twelve emitted lines.
/// <c>GeneratedProjectBuildTests.ProjectWithLogging_Builds</c> compiles the file for real, in a
/// generated app, against the very package versions <see cref="LoggingTemplate.Packages"/> names.
/// </remarks>
[TestClass]
public class LoggingTests
{
    private static readonly AppCustomization Logging =
        AppCustomization.Default with { Logging = true };

    [TestMethod]
    public void The_repository_logs_nothing_by_default()
        => Assert.IsFalse(
            RepositoryTemplate.Base("Acme").Contains("AppLog", StringComparison.Ordinal));

    [TestMethod]
    public void A_logging_repository_records_each_write()
    {
        var source = RepositoryTemplate.Base("Acme", validation: true, logging: true);

        StringAssert.Contains(source, "using Acme.Logging;");

        // ILogger and its LogInformation extension both live in Microsoft.Extensions.Logging, so
        // the emitted file needs that using as well as the app's own.
        StringAssert.Contains(source, "using Microsoft.Extensions.Logging;");
        StringAssert.Contains(source, "AppLog.For<");

        StringAssert.Contains(source, "Log.LogInformation(\"Created {Entity}");
        StringAssert.Contains(source, "Log.LogInformation(\"Updated {Entity}");
        StringAssert.Contains(source, "Log.LogInformation(\"Deleted {Entity}");
    }

    /// <summary>A rejected write is the one thing worth logging that is not an exception and not a
    /// success — but there is nothing to reject when validation is off.</summary>
    [TestMethod]
    public void A_logging_repository_warns_only_when_it_can_reject()
    {
        StringAssert.Contains(
            RepositoryTemplate.Base("Acme", validation: true, logging: true), "Log.LogWarning(");

        Assert.IsFalse(
            RepositoryTemplate.Base("Acme", validation: false, logging: true)
                .Contains("Log.LogWarning(", StringComparison.Ordinal));
    }

    /// <summary>Delete still answers whether it deleted anything: the logging variant holds the
    /// save result in a local rather than returning it straight out, and must not lose it.</summary>
    [TestMethod]
    public void A_logging_delete_still_returns_whether_it_saved()
    {
        var source = RepositoryTemplate.Base("Acme", validation: true, logging: true);

        StringAssert.Contains(source, "var saved = _store.SaveAll(all);");
        StringAssert.Contains(source, "return saved;");
    }

    [TestMethod]
    public void Every_declared_package_has_a_real_version()
    {
        Assert.IsNotEmpty(LoggingTemplate.Packages);

        foreach (var (id, version) in LoggingTemplate.Packages)
        {
            Assert.IsNotEmpty(id);

            // Not just non-empty: a placeholder left behind here would fail restore in every
            // generated app, and nothing else in this suite would notice.
            Assert.IsTrue(
                Version.TryParse(version, out _),
                $"{id} has no usable version: '{version}'");
        }
    }

    [TestMethod]
    public void A_logging_app_gets_the_file_and_the_packages()
    {
        var root = Path.Combine(Path.GetTempPath(), "log-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            var request = ScaffoldRequest.For(
                    TestModels.OneEntity(), "Shop", root, new ConsoleTarget())
                with { Customization = Logging };

            var result = new AppScaffolder().Scaffold(request);
            var csproj = File.ReadAllText(Path.Combine(result.ProjectPath, "Shop.Console.csproj"));

            Assert.IsTrue(result.Success, result.Message);
            Assert.IsTrue(File.Exists(Path.Combine(result.ProjectPath, "Logging", "AppLog.cs")));

            foreach (var (id, version) in LoggingTemplate.Packages)
                StringAssert.Contains(csproj, $"Include=\"{id}\" Version=\"{version}\"");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    /// <summary>
    /// The Web API gets AppLog.cs but no package references: the ASP.NET Core framework reference
    /// already carries those assemblies, and referencing one anyway makes NuGet report NU1510 on
    /// every build of the generated project. Verified against a real build in
    /// <c>GeneratedProjectBuildTests.ProjectWithLogging_Builds</c>, which compiles AppLog.cs in a
    /// web api with no logging package in sight.
    /// </summary>
    [TestMethod]
    public void A_logging_web_api_takes_its_logging_from_the_framework()
    {
        var root = Path.Combine(Path.GetTempPath(), "log-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            var request = ScaffoldRequest.For(
                    TestModels.OneEntity(), "Shop", root, new WebApiTarget())
                with { Customization = Logging };

            var result = new AppScaffolder().Scaffold(request);
            var csproj = File.ReadAllText(Path.Combine(result.ProjectPath, "Shop.Api.csproj"));

            Assert.IsTrue(File.Exists(Path.Combine(
                result.ProjectPath, LoggingTemplate.FolderName, LoggingTemplate.FileName)));
            Assert.IsFalse(
                csproj.Contains("Microsoft.Extensions.Logging", StringComparison.Ordinal),
                "the web api should take Microsoft.Extensions.Logging from its framework reference");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    [TestMethod]
    public void An_app_without_logging_has_no_logging_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "log-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            var result = new AppScaffolder().Scaffold(
                ScaffoldRequest.For(TestModels.OneEntity(), "Shop", root, new ConsoleTarget()));

            var csproj = File.ReadAllText(Path.Combine(result.ProjectPath, "Shop.Console.csproj"));

            Assert.IsFalse(Directory.Exists(Path.Combine(result.ProjectPath, "Logging")));
            Assert.IsFalse(csproj.Contains("Microsoft.Extensions.Logging", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    /// <summary>As in <see cref="NoValidationTests"/>: the logging lines are spliced into a
    /// raw-string template, and "\n" concatenation would leave the default output one CR per line
    /// short on a CRLF checkout.</summary>
    [TestMethod]
    public void The_logging_branch_leaves_the_line_endings_alone()
    {
        string[] sources =
        [
            RepositoryTemplate.Base("Acme"),
            RepositoryTemplate.Base("Acme", validation: true, logging: true),
            RepositoryTemplate.Base("Acme", validation: false, logging: true),
            LoggingTemplate.Source("Acme"),
        ];

        foreach (var source in sources)
        {
            var newlines = source.Count(c => c == '\n');
            var carriageReturns = source.Count(c => c == '\r');

            Assert.IsTrue(
                carriageReturns == 0 || carriageReturns == newlines,
                $"mixed line endings: {newlines} newline(s), {carriageReturns} carriage return(s)");
        }
    }
}
