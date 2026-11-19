using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AppScaffolding;
using AppScaffolding.Targets;
using DataIngestion;
using DataIngestion.Normalization;
using DataIngestion.Schema.Readers;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class GeneratedProjectBuildTests
{
    private static CandidateDomainModel SampleModel()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Book",
            Fields = { new CandidateField { Name = "Title", Type = "string" } }
        });
        model.Entities.Add(new CandidateEntity
        {
            Name = "Member",
            Fields = { new CandidateField { Name = "JoinedOn", Type = "DateTime" } }
        });
        return model;
    }

    /// <summary>
    /// A prefixed app builds. This is the only check that can fail on the thing the prefix risks:
    /// WPF's x:Class is resolved by the XAML compiler against the namespaces the generated views
    /// declare, and App.xaml comes from `dotnet new` naming the project instead. The console row
    /// adds the generated test project, whose own usings are rooted at the prefix too. webapi and
    /// worker are here too: webapi's prefixed Program.cs otherwise only has an absence assertion
    /// and worker's has none at all, and JsonStoreTemplate.Source($"{rootNamespace}.Data") is
    /// emitted by every prefixed row and read by none of the others. blazor's pages carry the prefix
    /// in <c>@using</c> directives, which the Razor compiler resolves and no other row exercises.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("console")]
    [DataRow("wpf")]
    [DataRow("winforms")]
    [DataRow("webapi")]
    [DataRow("worker")]
    [DataRow("blazor")]
    public void PrefixedProject_BuildsAndItsTestsPass(string targetKey)
    {
        if ((targetKey == "wpf" || targetKey == "winforms") && !OperatingSystem.IsWindows())
            Assert.Inconclusive($"The {targetKey} target needs the Windows desktop targeting pack.");

        Assert.IsTrue(TargetScaffolders.TryResolve(targetKey, out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder().Scaffold(
                ScaffoldRequest.For(SampleModel(), "Sample", workspace, target)
                with
                {
                    Customization = AppCustomization.Default with { NamespacePrefix = "CodisLtd" },
                });

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed for {targetKey}:\n{output}");

            if (targetKey != "console")
                return;

            Assert.IsNotNull(result.TestProjectPath, "no test project was emitted");
            var (testExit, testOutput) = RunDotNet($"test \"{result.TestProjectPath}\" -c Release");
            Assert.AreEqual(0, testExit, $"dotnet test failed:\n{testOutput}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// A model with a real transaction in it, so the business shell's own screens are compiled.
    /// </summary>
    /// <remarks>
    /// Order references a Customer and carries a date and an amount, which is what makes the
    /// classifier call it a transaction. Without it every row here is masters-only and the
    /// transaction screen, its date range and its totals are never built.
    /// </remarks>
    private static CandidateDomainModel BusinessModel()
    {
        var model = new CandidateDomainModel();

        model.Entities.Add(new CandidateEntity
        {
            Name = "Customer",
            Fields = { new CandidateField { Name = "Name", Type = "string" } }
        });

        model.Entities.Add(new CandidateEntity
        {
            Name = "Order",
            Fields =
            {
                new CandidateField { Name = "CustomerId", Type = "int" },
                new CandidateField { Name = "OrderedOn", Type = "DateTime" },
                new CandidateField { Name = "Total", Type = "decimal" },
            }
        });

        model.Entities.Add(new CandidateEntity
        {
            Name = "OrderLine",
            Fields =
            {
                new CandidateField { Name = "OrderId", Type = "int" },
                new CandidateField { Name = "Quantity", Type = "int" },
            }
        });

        return model;
    }

    /// <summary>
    /// The business shell builds, on every target that has one.
    /// </summary>
    /// <remarks>
    /// Nothing else here builds it: every other row leaves <c>Layout</c> unset, and until the
    /// default flips that means tabs — so the shell, the four screens and the editor were emitted
    /// by the generator and compiled by nothing. Branch 1 shipped five defects that passed every
    /// string assertion and were caught only by building a generated app.
    /// </remarks>
    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("wpf")]
    [DataRow("winforms")]
    [DataRow("blazor")]
    public void BusinessProject_Builds(string targetKey)
    {
        if ((targetKey == "wpf" || targetKey == "winforms") && !OperatingSystem.IsWindows())
            Assert.Inconclusive($"The {targetKey} target needs the Windows desktop targeting pack.");

        Assert.IsTrue(TargetScaffolders.TryResolve(targetKey, out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder().Scaffold(
                ScaffoldRequest.For(BusinessModel(), "Sample", workspace, target)
                with
                {
                    Customization = AppCustomization.Default with { Layout = AppLayout.Business },
                });

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            // Or the row builds a tabs app under a business name and proves nothing.
            var marker = targetKey == "blazor"
                ? Path.Combine("Components", "Screens", "MasterScreen.razor")
                : Path.Combine("ViewModels", "ShellViewModel.cs");

            Assert.IsTrue(
                File.Exists(Path.Combine(result.ProjectPath, marker)),
                $"{marker} was not emitted, so this build covers the wrong screens");

            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed for {targetKey}:\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// The business shell builds with <c>nologin</c> too.
    /// </summary>
    /// <remarks>
    /// The row above builds it only with the default login, and every business template named
    /// <c>{root}.Auth</c> and its <c>Session</c> unconditionally -- so a nologin business app,
    /// which emits no Auth folder at all, failed with CS0234 in the shell view model, the master
    /// screen and the WPF code-behind. The other four layouts already asked the flag; this one
    /// was never built without it.
    /// </remarks>
    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("wpf")]
    [DataRow("winforms")]
    public void BusinessProject_WithNoLogin_Builds(string targetKey)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive($"The {targetKey} target needs the Windows desktop targeting pack.");

        Assert.IsTrue(TargetScaffolders.TryResolve(targetKey, out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder().Scaffold(
                ScaffoldRequest.For(BusinessModel(), "Sample", workspace, target)
                with
                {
                    Customization = AppCustomization.Default with
                    {
                        Layout = AppLayout.Business, Login = false,
                    },
                });

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            // Or the row proves nothing: a business shell, and no Auth folder for it to lean on.
            Assert.IsTrue(
                File.Exists(Path.Combine(result.ProjectPath, "ViewModels", "ShellViewModel.cs")),
                "ShellViewModel.cs was not emitted, so this build covers the wrong screens");
            Assert.IsFalse(
                Directory.Exists(Path.Combine(result.ProjectPath, "Auth")),
                "an Auth folder was emitted, so this build is not a nologin one");

            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed for {targetKey}:\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// The staleness trap end to end: a project generated under one prefix and regenerated under
    /// another has to build, which means App.xaml, RootNamespace and every emitted namespace agree
    /// after the second run.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void AProjectRegeneratedUnderAChangedPrefix_StillBuilds()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("The wpf target needs the Windows desktop targeting pack.");

        Assert.IsTrue(TargetScaffolders.TryResolve("wpf", out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var scaffolder = new AppScaffolder();
            var request = ScaffoldRequest.For(SampleModel(), "Sample", workspace, target);

            scaffolder.Scaffold(request with
            {
                Customization = AppCustomization.Default with { NamespacePrefix = "CodisLtd" },
            });

            var second = scaffolder.Scaffold(request with
            {
                Customization = AppCustomization.Default with { NamespacePrefix = "Acme" },
            });

            Assert.IsTrue(second.Success, "scaffold failed: " + second.Message);

            var appXaml = File.ReadAllText(Path.Combine(second.ProjectPath, "App.xaml"));
            StringAssert.Contains(appXaml, "x:Class=\"Acme.Sample.App\"");
            Assert.IsFalse(appXaml.Contains("CodisLtd", StringComparison.Ordinal));

            var (exit, output) = RunDotNet($"build \"{second.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed after the prefix changed:\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("console")]
    [DataRow("webapi")]
    [DataRow("classlib")]
    [DataRow("worker")]
    [DataRow("wpf")]
    [DataRow("winforms")]
    [DataRow("blazor")]
    public void GeneratedProject_Builds(string targetKey)
    {
        // wpf is TargetScaffolders.Default -- what `build app:` ships unless told otherwise -- and
        // EntityViewModel<T> is the one place that resolves a validator outside Repository, under a
        // `where T : class, new()` constraint. A missing constraint is CS0452, which no substring
        // assertion can see, so the default target has to be compiled like the rest. winforms
        // shares those same view models, and additionally proves its RelayCommand variant really
        // is free of PresentationCore. blazor is here because a .razor file compiles to a class named
        // after it, so an <Entity>.razor page would shadow Models.<Entity> and break every page in the
        // app -- found exactly this way, and invisible to any assertion over the emitted markup.
        if ((targetKey == "wpf" || targetKey == "winforms") && !OperatingSystem.IsWindows())
            Assert.Inconclusive($"The {targetKey} target needs the Windows desktop targeting pack.");

        Assert.IsTrue(TargetScaffolders.TryResolve(targetKey, out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder().Scaffold(SampleModel(), "Sample", workspace, target);
            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed for {targetKey}:\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// A customized app still builds once it has an icon. This is the one part of customization
    /// MSBuild itself has to accept: it parses an ApplicationIcon to embed as a Win32 resource and
    /// rejects anything that is not a real icon, and a Resource item naming a file that is not
    /// where it says is a build error. The WinForms form's icon loading is emitted C# besides.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("wpf")]
    [DataRow("winforms")]
    public void CustomizedProject_WithAnIcon_Builds(string targetKey)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive($"The {targetKey} target needs the Windows desktop targeting pack.");

        Assert.IsTrue(TargetScaffolders.TryResolve(targetKey, out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var icon = Path.Combine(workspace, "brand.ico");
            File.WriteAllBytes(icon, MinimalIcon());

            var result = new AppScaffolder().Scaffold(
                ScaffoldRequest.For(SampleModel(), "Sample", workspace, target)
                with
                {
                    Customization = AppCustomization.Default with
                    {
                        Company = "Codis Ltd",
                        TitlePattern = AppCustomization.CompanyTitlePattern,
                        Theme = AppTheme.Dark,
                        IconPath = icon,
                    },
                    Year = 2026,
                });

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed for {targetKey}:\n{output}");

            // The two front ends reach the icon differently, and each way has to be provably in
            // place: WPF resolves the window's relative Icon attribute through the pack URI, which
            // reads the assembly's own resources, while the WinForms form opens the file beside its
            // binary. Either registration missing leaves an app that builds and then shows the
            // default icon, which no build result can report.
            if (targetKey == "winforms")
            {
                Assert.IsNotEmpty(Directory.GetFiles(
                    Path.Combine(result.ProjectPath, "bin"), "app.ico", SearchOption.AllDirectories));
            }
            else
            {
                var resources = Directory.GetFiles(
                    Path.Combine(result.ProjectPath, "obj"), "*.g.resources",
                    SearchOption.AllDirectories);

                Assert.IsNotEmpty(resources, "no generated resource stream to hold the icon");

                // Resource names are the lower-cased relative path, which is exactly what the
                // window's Icon="Assets/app.ico" resolves to.
                var names = resources.SelectMany(
                    r => new[] { Encoding.UTF8, Encoding.Unicode }.Select(
                        e => e.GetString(File.ReadAllBytes(r))));

                Assert.IsTrue(
                    names.Any(n => n.Contains("assets/app.ico", StringComparison.Ordinal)),
                    "the icon was not embedded under the name the window asks for");
            }
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// A prefixed Blazor app keeps its reconnect modal. The template's <c>_Imports.razor</c>
    /// imports <c>&lt;Project&gt;.Components.Layout</c>, which the prefix moves away; the build
    /// still succeeds, but with RZ10012 on <c>&lt;ReconnectModal /&gt;</c>, which then renders as
    /// an unknown HTML tag. So the warning itself is the failure.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void PrefixedBlazorProject_ResolvesEveryTemplateComponent()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("blazor", out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder().Scaffold(
                ScaffoldRequest.For(SampleModel(), "Sample", workspace, target)
                with
                {
                    Customization = AppCustomization.Default with { NamespacePrefix = "CodisLtd" },
                });

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release --no-incremental");
            Assert.AreEqual(0, exit, $"dotnet build failed:\n{output}");
            Assert.DoesNotContain("RZ10012", output, $"a template component did not resolve:\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// A company name carrying characters each front end treats specially still builds and shows
    /// as typed. Razor reads <c>@</c> as code (CS0103 on the word after it) and shows a C#-escaped
    /// quote as a literal backslash; XAML reads an attribute that starts with <c>{</c> as a markup
    /// extension. Each target writes the title into a different context, so each needs its own
    /// escaping, and only a real build can see a wrong one.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("blazor")]
    [DataRow("wpf")]
    [DataRow("winforms")]
    [DataRow("webapi")]
    [DataRow("console")]
    public void ACompanyNameWithSpecialCharacters_BuildsAndShowsAsTyped(string targetKey)
    {
        if ((targetKey == "wpf" || targetKey == "winforms") && !OperatingSystem.IsWindows())
            Assert.Inconclusive($"The {targetKey} target needs the Windows desktop targeting pack.");

        Assert.IsTrue(TargetScaffolders.TryResolve(targetKey, out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder().Scaffold(
                ScaffoldRequest.For(SampleModel(), "Sample", workspace, target)
                with
                {
                    Customization = AppCustomization.Default with
                    {
                        Company = "{Smith} @Jones \"Quote\" & <Co> \\ 50%",
                        TitlePattern = AppCustomization.CompanyTitlePattern,
                        Login = true,
                    },
                    Year = 2026,
                });

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed for {targetKey}:\n{output}");

            if (targetKey != "blazor")
                return;

            // Razor markup is HTML: a C# escape there is shown to the user as a backslash.
            var nav = File.ReadAllText(
                Path.Combine(result.ProjectPath, "Components", "Layout", "NavMenu.razor"));
            Assert.DoesNotContain("\\\"", nav, "the nav shows a C#-escaped quote");
            Assert.Contains("&quot;Quote&quot;", nav);
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// An app generated with logging builds — which is the only way the emitted AppLog.cs is
    /// compiled at all, since Microsoft.Extensions.Logging is not among the assemblies
    /// Compiler.Compile can reach. It also restores the versions LoggingTemplate.Packages pins: an
    /// unresolvable one would fail every generated app and nothing else here would notice.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("console")]
    [DataRow("webapi")]
    [DataRow("worker")]
    public void ProjectWithLogging_Builds(string targetKey)
    {
        Assert.IsTrue(TargetScaffolders.TryResolve(targetKey, out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder().Scaffold(
                ScaffoldRequest.For(SampleModel(), "Sample", workspace, target)
                with
                {
                    Customization = AppCustomization.Default with { Logging = true },
                });

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);
            Assert.IsTrue(File.Exists(Path.Combine(
                result.ProjectPath, LoggingTemplate.FolderName, LoggingTemplate.FileName)));

            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed for {targetKey}:\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// Validation and logging are independent flags -- `novalidation logging` reaches Delete's
    /// logging branch in <c>Repository.cs</c> with no <c>validateCall</c> above it, which is
    /// exactly the combination that let a StoreWrite/bool typing bug through uncaught: every other
    /// build test pairs logging with the DEFAULT (on) validation, whose own unrelated old-seam
    /// errors would have masked a second defect in the same file. This is the one real build that
    /// isolates the logging branch on its own.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void ProjectWithLoggingAndNoValidation_Builds()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder().Scaffold(
                ScaffoldRequest.For(SampleModel(), "Sample", workspace, target)
                with
                {
                    Customization = AppCustomization.Default
                        with
                        { Logging = true, Validation = false },
                });

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);
            Assert.IsTrue(File.Exists(Path.Combine(
                result.ProjectPath, LoggingTemplate.FolderName, LoggingTemplate.FileName)));

            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed:\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// An app whose Ticket has a value list no two tickets may share, and a Note that references
    /// a Ticket, passes its own generated tests with seeded rows in the store. The seeded rows now
    /// hold listed values, so a test record that always took the first listed value was refused
    /// as a duplicate of the first seeded row; it now takes one no stored row holds.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    [DataRow(true, DisplayName = "seeded")]
    [DataRow(false, DisplayName = "not seeded")]
    public void ProjectWithAUniqueValueList_ItsTestsPass(bool seed)
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

        static CodeGenerationEngine.Models.RuleProposal Accepted(
            CodeGenerationEngine.Models.RuleProposalKind kind, CodeGenerationEngine.Models.PropertyRule rule)
            => new() { Kind = kind, Rule = rule, Evidence = "in all rows", Accepted = true };

        var status = new CandidateField { Name = "Status", Type = "string" };
        status.Proposals.Add(Accepted(CodeGenerationEngine.Models.RuleProposalKind.Status, new CodeGenerationEngine.Models.PropertyRule
        {
            PropertyName = "Status", Name = "allowedvalues", AllowedValues = ["Open", "InProgress", "Closed", "Held", "Done"],
        }));
        status.Proposals.Add(Accepted(CodeGenerationEngine.Models.RuleProposalKind.Unique, new CodeGenerationEngine.Models.PropertyRule
        {
            PropertyName = "Status", Name = "unique", IsUnique = true,
        }));

        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Ticket",
            Fields = { new CandidateField { Name = "Title", Type = "string" }, status },
        });
        model.Entities.Add(new CandidateEntity
        {
            Name = "Note",
            Fields =
            {
                new CandidateField { Name = "Text", Type = "string" },
                new CandidateField { Name = "TicketId", Type = "int" },
            },
            Relationships = { new CandidateRelationship { Target = "Ticket", Role = "references" } },
        });

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder().Scaffold(
                ScaffoldRequest.For(model, "Sample", workspace, target)
                with
                {
                    Customization = AppCustomization.Default with { SampleSeedData = seed },
                });

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);
            Assert.IsNotNull(result.TestProjectPath, "no test project was emitted");

            var (testExit, testOutput) = RunDotNet($"test \"{result.TestProjectPath}\" -c Release");
            Assert.AreEqual(0, testExit, $"dotnet test failed:\n{testOutput}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// An app generated without sample data builds. SeedData.EnsureSeeded() is called from four
    /// separate entry points, so every target is a row here: a missed call site is a CS0103 on a
    /// class the app no longer generates, and the console row's generated tests prove nothing left
    /// in the suite names it either.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("console")]
    [DataRow("webapi")]
    [DataRow("worker")]
    [DataRow("wpf")]
    public void ProjectWithoutSampleData_BuildsAndItsTestsPass(string targetKey)
    {
        if (targetKey == "wpf" && !OperatingSystem.IsWindows())
            Assert.Inconclusive("The wpf target needs the Windows desktop targeting pack.");

        Assert.IsTrue(TargetScaffolders.TryResolve(targetKey, out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder().Scaffold(
                ScaffoldRequest.For(SampleModel(), "Sample", workspace, target)
                with
                {
                    Customization = AppCustomization.Default with { SampleSeedData = false },
                });

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);
            Assert.IsFalse(
                File.Exists(Path.Combine(result.ProjectPath, "Data", "SeedData.cs")),
                "SeedData.cs was generated anyway");

            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed for {targetKey}:\n{output}");

            if (targetKey != "console")
                return;

            Assert.IsNotNull(result.TestProjectPath, "no test project was emitted");
            var (testExit, testOutput) = RunDotNet($"test \"{result.TestProjectPath}\" -c Release");
            Assert.AreEqual(0, testExit, $"dotnet test failed:\n{testOutput}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// An app generated with validation off builds, and its companion tests still pass. Generated
    /// code hard-depends on the Validation folder in two places — Repository and EntityViewModel
    /// both resolve a validator — so this is the check that catches a call site nobody remembered:
    /// the console target proves Repository, the wpf target proves the view models, and the
    /// generated test project proves nothing left behind still names a validator.
    /// </summary>
    /// <remarks>
    /// The blazor row earns its place separately: <c>BlazorScreenTemplates</c> emits
    /// <c>@using {root}.Validation</c> UNCONDITIONALLY in both grids, and Razor markup is
    /// compiled by nothing short of a whole-app build — no unit test over the emitted string can
    /// tell you whether that using resolves with the pipeline files absent.
    /// </remarks>
    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("console")]
    [DataRow("wpf")]
    [DataRow("blazor")]
    public void ProjectWithoutValidation_BuildsAndItsTestsPass(string targetKey)
    {
        if (targetKey == "wpf" && !OperatingSystem.IsWindows())
            Assert.Inconclusive("The wpf target needs the Windows desktop targeting pack.");

        Assert.IsTrue(TargetScaffolders.TryResolve(targetKey, out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder().Scaffold(
                ScaffoldRequest.For(SampleModel(), "Sample", workspace, target)
                with
                {
                    Customization = AppCustomization.Default with { Validation = false },
                });

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            // The Validation folder is not gone: Security sits outside the novalidation flag
            // (spec Decision 4), so turning validation off must not turn permissions off. It
            // holds exactly Permissions.cs, ValidationContext.cs and IEntityLookup.cs — and
            // nothing that only the validation pipeline needs. See NoValidationTests for the
            // same assertion against a single-target scaffold.
            var validationFolder = Path.Combine(result.ProjectPath, "Validation");
            Assert.IsTrue(Directory.Exists(validationFolder), "the Validation folder was not generated");
            Assert.IsTrue(File.Exists(Path.Combine(validationFolder, "Permissions.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(validationFolder, "ValidationContext.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(validationFolder, "IEntityLookup.cs")));
            Assert.IsFalse(File.Exists(Path.Combine(validationFolder, "ValidatorFactory.cs")));
            Assert.IsFalse(File.Exists(Path.Combine(validationFolder, "BookValidator.Generated.cs")));
            Assert.IsFalse(File.Exists(Path.Combine(validationFolder, "BookValidator.Custom.cs")));
            Assert.IsFalse(File.Exists(Path.Combine(validationFolder, "MemberValidator.Generated.cs")));
            Assert.IsFalse(File.Exists(Path.Combine(validationFolder, "MemberValidator.Custom.cs")));

            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed for {targetKey}:\n{output}");

            // Neither the wpf nor the blazor row gets a test project of its own, so the console
            // row is the one that proves the generated suite. What the other two prove is that
            // the app itself compiles with the pipeline files absent — which for blazor means
            // its Razor markup, and nothing short of this build compiles that.
            if (targetKey != "console")
                return;

            Assert.IsNotNull(result.TestProjectPath, "no test project was emitted");
            var (testExit, testOutput) = RunDotNet($"test \"{result.TestProjectPath}\" -c Release");
            Assert.AreEqual(0, testExit, $"dotnet test failed:\n{testOutput}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// A 1x1 32bpp .ico, written by hand. MSBuild parses an ApplicationIcon, so the four bytes the
    /// unit tests use will not do here, and a committed binary would be unreviewable bytes in the
    /// repo — this way the header the build has to accept is visible.
    /// </summary>
    private static byte[] MinimalIcon()
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);

        const int headerSize = 40;   // BITMAPINFOHEADER
        const int pixelBytes = 4;    // one BGRA pixel
        const int maskBytes = 4;     // the AND mask's single row, padded to four bytes

        // ICONDIR: one image, of type "icon".
        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)1);

        // ICONDIRENTRY.
        writer.Write((byte)1);       // width
        writer.Write((byte)1);       // height
        writer.Write((byte)0);       // palette colours: none, this is a true-colour image
        writer.Write((byte)0);       // reserved
        writer.Write((short)1);      // colour planes
        writer.Write((short)32);     // bits per pixel
        writer.Write(headerSize + pixelBytes + maskBytes);
        writer.Write(22);            // offset: past the ICONDIR and this single entry

        // BITMAPINFOHEADER. The height is doubled because the DIB holds the colour image and the
        // AND mask stacked.
        writer.Write(headerSize);
        writer.Write(1);             // width
        writer.Write(2);             // height
        writer.Write((short)1);      // planes
        writer.Write((short)32);     // bits per pixel
        writer.Write(0);             // BI_RGB: uncompressed
        writer.Write(0);             // image size: the decoder computes it
        writer.Write(0);             // horizontal resolution
        writer.Write(0);             // vertical resolution
        writer.Write(0);             // palette entries used
        writer.Write(0);             // palette entries required

        writer.Write(new byte[] { 0x00, 0x66, 0xCC, 0xFF });   // one opaque pixel, BGRA
        writer.Write(new byte[maskBytes]);                     // nothing masked out

        writer.Flush();
        return buffer.ToArray();
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void GeneratedConsoleProject_WithRelationship_Builds()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Customer",
            Fields = { new CandidateField { Name = "Name", Type = "string" } }
        });
        var order = new CandidateEntity
        {
            Name = "Order",
            Fields =
            {
                new CandidateField { Name = "Total", Type = "decimal" },
                new CandidateField { Name = "CustomerId", Type = "int" }
            }
        };
        order.Relationships.Add(new CandidateRelationship { Target = "Customer", Role = "references" });
        model.Entities.Add(order);

        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder().Scaffold(model, "Shop", workspace, target);
            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed:\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("console")]
    [DataRow("webapi")]
    [DataRow("classlib")]
    [DataRow("worker")]
    [DataRow("wpf")]
    [DataRow("winforms")]
    public void GeneratedTestProject_BuildsAndPasses(string targetKey)
    {
        // The desktop rows were missing because there was nothing to run: `dotnet new xunit`
        // refused net10.0-windows, the scaffold swallowed it, and no WPF or WinForms app ever
        // had a test project.
        if ((targetKey == "wpf" || targetKey == "winforms") && !OperatingSystem.IsWindows())
            Assert.Inconclusive($"The {targetKey} target needs the Windows desktop targeting pack.");

        Assert.IsTrue(TargetScaffolders.TryResolve(targetKey, out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder().Scaffold(SampleModel(), "Sample", workspace, target);
            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);
            Assert.IsNotNull(result.TestProjectPath, "no test project was emitted");

            var (exit, output) = RunDotNet($"test \"{result.TestProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet test failed for {targetKey}:\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// A test project `dotnet new xunit` refuses to make is said, and leaves nothing behind. It
    /// used to be neither: every WPF and WinForms app got an empty <c>&lt;App&gt;.Tests</c>
    /// folder, no test project, and no word about either. The refusal is forced here with a
    /// framework no SDK has, written into the app's own project file, which is where the test
    /// project's framework is read from.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void A_test_project_that_cannot_be_created_is_reported_and_leaves_no_folder()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var first = new AppScaffolder().Scaffold(SampleModel(), "Sample", workspace, target);
            Assert.IsTrue(first.Success, "scaffold failed: " + first.Message);
            Assert.IsNotNull(first.TestProjectPath, "no test project was emitted");

            var testFolder = Path.GetDirectoryName(first.TestProjectPath)!;
            Directory.Delete(testFolder, recursive: true);

            var appCsproj = Directory.GetFiles(first.ProjectPath, "*.csproj").Single();
            File.WriteAllText(appCsproj, File.ReadAllText(appCsproj).Replace(
                "<TargetFramework>net10.0</TargetFramework>",
                "<TargetFramework>net99.0</TargetFramework>", StringComparison.Ordinal));

            var second = new AppScaffolder().Scaffold(SampleModel(), "Sample", workspace, target);

            Assert.IsNull(second.TestProjectPath);
            Assert.IsNotNull(second.TestProjectProblem, "the missing test project was not reported");
            StringAssert.Contains(second.TestProjectProblem, "No test project was generated");
            Assert.IsFalse(Directory.Exists(testFolder), "an empty test folder was left behind");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("console")]
    [DataRow("webapi")]
    [DataRow("worker")]
    public void GeneratedSqliteProject_BuildsSeedsAndRoundTrips(string targetKey)
    {
        Assert.IsTrue(TargetScaffolders.TryResolve(targetKey, out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder()
                .Scaffold(SampleModel(), "Sample", workspace, target, PersistenceKind.Sqlite);
            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);
            Assert.IsNotNull(result.TestProjectPath, "no test project was emitted");

            // The generated companion tests exercise Repository + SeedData, which now route through
            // EfStore -> SQLite. A green run proves the schema is created, seeded, and round-tripped.
            var (exit, output) = RunDotNet($"test \"{result.TestProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet test failed for {targetKey} (sqlite):\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void GeneratedSqliteProject_WithRelationship_SeedsAndRoundTrips()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Customer",
            Fields = { new CandidateField { Name = "Name", Type = "string" } }
        });
        var order = new CandidateEntity
        {
            Name = "Order",
            Fields =
            {
                new CandidateField { Name = "Total", Type = "decimal" },
                new CandidateField { Name = "CustomerId", Type = "int" }
            }
        };
        order.Relationships.Add(new CandidateRelationship { Target = "Customer", Role = "references" });
        model.Entities.Add(order);

        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder()
                .Scaffold(model, "Shop", workspace, target, PersistenceKind.Sqlite);
            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);
            Assert.IsNotNull(result.TestProjectPath, "no test project was emitted");

            // Proves the feature's central design decision against real SQLite: AppDbContext maps
            // scalar columns only (navigation/collection properties are ignored), so Order.CustomerId
            // is a plain column with no FK constraint. Order is a Transaction (it references Customer
            // and its name is in TransactionWords), so its generated validator now requires the key
            // rather than skipping it at zero -- TestSupport fills the companion test's Order by
            // creating a real Customer row through CustomerRepository first and using its actual Id,
            // and EF Core / SQLite accept the plain int column either way.
            var (exit, output) = RunDotNet($"test \"{result.TestProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet test failed (sqlite, with relationship):\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <remarks>The desktop rows are the only ones with a login, whose Security stage refuses a
    /// null role before any field rule runs: their blank-record test is what proves the generated
    /// validator tests act as a signed-in Admin rather than as nobody.</remarks>
    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("console")]
    [DataRow("wpf")]
    [DataRow("winforms")]
    public void A_generated_app_with_validators_builds_and_its_tests_pass(string targetKey)
    {
        if ((targetKey == "wpf" || targetKey == "winforms") && !OperatingSystem.IsWindows())
            Assert.Inconclusive($"The {targetKey} target needs the Windows desktop targeting pack.");

        // The unit suite flattens the emitted Validation/ files into one compilation unit, which
        // strips their `using <App>.Models;` / `using <App>.Data;`. Only a real build of a real
        // generated app proves those namespaces resolve. Customer has the required Name field and
        // Order carries a foreign key, so the rule branch and the lookup branch are both emitted.
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Customer",
            Fields =
            {
                new CandidateField { Name = "Name", Type = "string" },
                new CandidateField { Name = "Email", Type = "string" },
                new CandidateField { Name = "Phone", Type = "string" },
            }
        });
        var order = new CandidateEntity
        {
            Name = "Order",
            Fields =
            {
                new CandidateField { Name = "Total", Type = "decimal" },
                new CandidateField { Name = "CustomerId", Type = "int" }
            }
        };
        order.Relationships.Add(new CandidateRelationship { Target = "Customer", Role = "references" });
        model.Entities.Add(order);

        Assert.IsTrue(TargetScaffolders.TryResolve(targetKey, out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder().Scaffold(model, "Shop", workspace, target);
            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            var validation = Path.Combine(result.ProjectPath, "Validation");
            Assert.IsTrue(File.Exists(Path.Combine(validation, "CustomerValidator.Generated.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(validation, "CustomerValidator.Custom.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(result.ProjectPath, "Data", "SaveResult.cs")));

            var (buildExit, buildOutput) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, buildExit, $"dotnet build failed:\n{buildOutput}");

            // The generated suite now includes the per-model validator tests, so a green run also
            // proves the emitted rules behave: a filled record passes, a blank one names its
            // required field, and a null one is reported rather than throwing.
            Assert.IsNotNull(result.TestProjectPath, "no test project was emitted");
            var (testExit, testOutput) = RunDotNet($"test \"{result.TestProjectPath}\" -c Release");
            Assert.AreEqual(0, testExit, $"dotnet test failed:\n{testOutput}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// The whole data path in one test: a CSV is read, normalized, turned into a model, scaffolded,
    /// and the result is COMPILED. Every stage in between can produce something that parses and
    /// still will not build -- a column named after its own class is CS0542, a seed value containing
    /// a quote breaks the JSON, a foreign key to a class that was never generated is CS0246 -- and
    /// none of that is visible to a substring assertion.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void ProjectGeneratedFromACsv_BuildsAndCarriesTheData()
    {
        // Short root: the scratchpad path plus a generated project's nesting exceeds MAX_PATH, and
        // MSBuild reports that as MSB3030 "file not found" on a file that is plainly there.
        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);

        var csv = Path.Combine(workspace, "orders.csv");

        // Deliberately awkward: an Id that must be dropped, a column named after its own class,
        // a repeating group, a partial dependency, a value carrying a quote and a brace.
        File.WriteAllText(csv, string.Join("\n",
            "Id,Order,CustomerName,CustomerCity,Qty,Item1,Item2",
            "1,\"a \"\"quoted\"\" {value}\",Ada,London,2,Nut,Bolt",
            "2,plain,Grace,York,3,Washer,",
            "3,another,Ada,London,4,Nut,",
            "4,more,Grace,York,5,Bolt,",
            "5,last,Ada,London,6,Washer,Nut",
            ""));

        try
        {
            var tables = new CsvReader().Read(new DataSourceSpec(DataSourceKind.Csv, csv));

            var findings = NormalizationAnalyzer.Analyze(tables[0]).Findings;
            Assert.IsGreaterThanOrEqualTo(2, findings.Count,
                "expected at least the repeating group and the partial dependency, got: "
                + string.Join("; ", findings.Select(f => f.Headline)));

            var normalized = NormalizationPlan.Apply(tables, findings);
            var model = DataModelBuilder.Build(normalized);
            var imported = TabularImport.ToImportedData(normalized);

            Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

            var result = new AppScaffolder().Scaffold(
                model, "Shop", workspace, target, PersistenceKind.Json, imported);

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            // The generated project compiles. This is the assertion the others exist to enable.
            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed:\n{output}");

            // ...and the data really is in there, as valid JSON with the awkward value intact.
            var seedFolder = Path.Combine(result.ProjectPath, "Data", SeedFileWriter.SeedFolderName);
            var orderSeed = Path.Combine(seedFolder, "Order.json");

            Assert.IsTrue(File.Exists(orderSeed), $"no seed file at {orderSeed}");

            var rows = JsonDocument.Parse(File.ReadAllText(orderSeed))
                .RootElement.EnumerateArray().ToList();

            Assert.HasCount(5, rows);
            Assert.AreEqual(1, rows[0].GetProperty("Id").GetInt32());

            // The column named after its own class was renamed, not dropped, and its hostile value
            // survived as one string.
            Assert.AreEqual("a \"quoted\" {value}", rows[0].GetProperty("OrderName").GetString());

            // The read Id column is gone -- Id is generated, and rows[0].Id above is the generated one.
            Assert.AreEqual(5, rows.Count(r => r.TryGetProperty("Id", out _)));

            // The extracted entities were scaffolded and seeded too.
            Assert.IsTrue(File.Exists(Path.Combine(seedFolder, "Customer.json")));
            Assert.IsTrue(File.Exists(Path.Combine(seedFolder, "OrderItem.json")));

            var customers = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(seedFolder, "Customer.json")))
                .RootElement.EnumerateArray().ToList();

            // Two distinct customers out of five order rows -- the point of the split.
            Assert.HasCount(2, customers);

            // The seed files were registered as copied-to-output content, so the app finds them
            // beside its binary at run time. Without this the project still BUILDS and the app
            // still starts -- just with no data -- so asserting the copy is the only way the
            // csproj registration is covered at all.
            var copied = Directory.GetFiles(
                Path.Combine(result.ProjectPath, "bin"), "Order.json", SearchOption.AllDirectories);

            Assert.IsNotEmpty(copied);
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// A Worker app built from a CSV never compiled: the imported rows go to
    /// <c>Data\Seed\*.json</c>, the Worker SDK already globs every <c>.json</c> as
    /// <c>Content</c>, and the scaffold's explicit <c>Include</c> of the same files was
    /// NETSDK1022. The Worker rows elsewhere never saw it, because a model with no imported data
    /// writes no seed files.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void A_worker_app_generated_from_a_csv_builds()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);

        var csv = Path.Combine(workspace, "customers.csv");
        File.WriteAllText(csv, string.Join("\n",
            "Name,Email,City,CreditLimit,JoinedOn",
            "Alice Smith,alice@example.com,London,1500.50,2024-01-15",
            "Bob Jones,bob@example.com,Leeds,2500.00,2023-11-02",
            ""));

        try
        {
            var tables = new CsvReader().Read(new DataSourceSpec(DataSourceKind.Csv, csv));
            var model = DataModelBuilder.Build(tables);
            var imported = TabularImport.ToImportedData(tables);

            Assert.IsTrue(TargetScaffolders.TryResolve("worker", out var target));

            var result = new AppScaffolder().Scaffold(
                model, "Tracker", workspace, target, PersistenceKind.Json, imported);

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);
            Assert.IsNotEmpty(
                Directory.GetFiles(Path.Combine(result.ProjectPath, "Data", SeedFileWriter.SeedFolderName)),
                "no seed file was written, so this proves nothing about the Content item");

            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed:\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// The `k` (keep as-is) path must compile too. A flat table with a repeating group left in place
    /// produces Item1/Item2 as ordinary columns, and nothing about that may break the build.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void ProjectGeneratedFromACsv_WithoutNormalizing_AlsoBuilds()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);

        var csv = Path.Combine(workspace, "orders.csv");
        File.WriteAllText(csv, string.Join("\n",
            "Ref,Item1,Item2,Placed,Paid,Total",
            "ORD-1,Nut,Bolt,2026-01-15,yes,9.99",
            "ORD-2,Washer,,2026-02-01,no,4.50",
            ""));

        try
        {
            var tables = new CsvReader().Read(new DataSourceSpec(DataSourceKind.Csv, csv));

            // No findings applied: this is exactly what `k` produces.
            var model = DataModelBuilder.Build(tables);
            var imported = TabularImport.ToImportedData(tables);

            Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

            var result = new AppScaffolder().Scaffold(
                model, "Flat", workspace, target, PersistenceKind.Json, imported);

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed:\n{output}");

            var rows = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                    result.ProjectPath, "Data", SeedFileWriter.SeedFolderName, "Order.json")))
                .RootElement.EnumerateArray().ToList();

            // Each of the five types round-tripped into JSON as itself.
            Assert.AreEqual("Nut", rows[0].GetProperty("Item1").GetString());
            Assert.AreEqual(9.99m, rows[0].GetProperty("Total").GetDecimal());
            Assert.IsTrue(rows[0].GetProperty("Paid").GetBoolean());
            Assert.AreEqual(new DateTime(2026, 1, 15), rows[0].GetProperty("Placed").GetDateTime());
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// The Excel path end to end: a workbook is read, modelled, scaffolded, and the result is
    /// COMPILED. What makes this worth its runtime over the CSV case is the two things only a
    /// workbook can get wrong — a date that is really a serial number, and a second sheet that has
    /// to become a second class — and neither is visible to a substring assertion.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void ProjectGeneratedFromAnExcelWorkbook_BuildsAndCarriesTheData()
    {
        // Short root: the scratchpad path plus a generated project's nesting exceeds MAX_PATH, and
        // MSBuild reports that as MSB3030 "file not found" on a file that is plainly there.
        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);

        var xlsx = Path.Combine(workspace, "shop.xlsx");
        WriteWorkbook(xlsx);

        try
        {
            var tables = new ExcelReader().Read(new DataSourceSpec(DataSourceKind.Excel, xlsx));

            // Two visible sheets, two entities. The hidden third sheet is not one.
            Assert.HasCount(2, tables);
            CollectionAssert.AreEquivalent(
                new[] { "Orders", "Customers" }, tables.Select(t => t.Name).ToArray());

            // The date column really is a date, not a five-digit serial.
            var orders = tables.Single(t => t.Name == "Orders");
            Assert.AreEqual("DateTime", orders.Columns.Single(c => c.Name == "Placed").Type);

            // ...and the padded reference kept its zeros, so it stayed a string.
            Assert.AreEqual("string", orders.Columns.Single(c => c.Name == "Code").Type);
            Assert.AreEqual("00042", orders.Value(0, "Code"));

            // The unstyled foreign key did NOT inherit the padding: only an int pairs with the
            // entity the other sheet produced, so a stray zero here would drop the relationship
            // and the generated navigation property with it.
            Assert.AreEqual("int", orders.Columns.Single(c => c.Name == "CustomerId").Type);

            var model = DataModelBuilder.Build(tables);

            var order = model.Entities.Single(e => e.Name == "Order");
            Assert.IsTrue(
                order.Relationships.Any(r => r.Target == "Customer"),
                "expected Order -> Customer, got: "
                + string.Join(", ", order.Relationships.Select(r => r.Target)));
            var imported = TabularImport.ToImportedData(tables);

            Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

            var result = new AppScaffolder().Scaffold(
                model, "Shop", workspace, target, PersistenceKind.Json, imported);

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            // The generated project compiles. This is the assertion the rest exist to enable.
            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed:\n{output}");

            var seedFolder = Path.Combine(
                result.ProjectPath, "Data", SeedFileWriter.SeedFolderName);

            var rows = JsonDocument.Parse(File.ReadAllText(Path.Combine(seedFolder, "Order.json")))
                .RootElement.EnumerateArray().ToList();

            Assert.HasCount(2, rows);
            Assert.AreEqual(new DateTime(2026, 3, 15), rows[0].GetProperty("Placed").GetDateTime());
            Assert.AreEqual("00042", rows[0].GetProperty("Code").GetString());
            Assert.AreEqual(9.99m, rows[0].GetProperty("Total").GetDecimal());

            // The second sheet became its own seeded class.
            Assert.IsTrue(File.Exists(Path.Combine(seedFolder, "Customer.json")));
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// A minimal .xlsx written by hand. A committed binary workbook would be unreviewable bytes in
    /// the repo, and this way the date serial and the padded format the test asserts on are visible
    /// right next to the assertions.
    /// </summary>
    private static void WriteWorkbook(string path)
    {
        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string rns = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        // Style 0 is General, as Excel always writes it: a cell with no s attribute uses index 0,
        // so putting a real format there would silently apply it to every unstyled number in the
        // file -- which is what CustomerId is, and it has to stay an int for the relationship.
        // Style 1: the 00000 padded code. Style 2: a date. 46096 is 15 March 2026.
        var styles = $"<styleSheet xmlns=\"{ns}\">"
            + "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"00000\"/></numFmts>"
            + "<cellXfs count=\"3\"><xf numFmtId=\"0\" xfId=\"0\"/>"
            + "<xf numFmtId=\"164\" xfId=\"0\"/>"
            + "<xf numFmtId=\"14\" xfId=\"0\"/></cellXfs></styleSheet>";

        var orders = $"<worksheet xmlns=\"{ns}\"><sheetData>"
            + "<row r=\"1\">"
            + Inline("A1", "Code") + Inline("B1", "Placed")
            + Inline("C1", "Total") + Inline("D1", "CustomerId")
            + "</row>"
            + "<row r=\"2\"><c r=\"A2\" s=\"1\"><v>42</v></c><c r=\"B2\" s=\"2\"><v>46096</v></c>"
            + "<c r=\"C2\"><v>9.99</v></c><c r=\"D2\"><v>1</v></c></row>"
            + "<row r=\"3\"><c r=\"A3\" s=\"1\"><v>7</v></c><c r=\"B3\" s=\"2\"><v>46097</v></c>"
            + "<c r=\"C3\"><v>4.50</v></c><c r=\"D3\"><v>2</v></c></row>"
            + "</sheetData></worksheet>";

        var customers = $"<worksheet xmlns=\"{ns}\"><sheetData>"
            + "<row r=\"1\">" + Inline("A1", "Name") + Inline("B1", "City") + "</row>"
            + "<row r=\"2\">" + Inline("A2", "Ada") + Inline("B2", "London") + "</row>"
            + "<row r=\"3\">" + Inline("A3", "Grace") + Inline("B3", "York") + "</row>"
            + "</sheetData></worksheet>";

        var scratch = $"<worksheet xmlns=\"{ns}\"><sheetData>"
            + "<row r=\"1\">" + Inline("A1", "Note") + "</row>"
            + "<row r=\"2\">" + Inline("A2", "working") + "</row>"
            + "</sheetData></worksheet>";

        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        Add(zip, "xl/worksheets/sheet1.xml", orders);
        Add(zip, "xl/worksheets/sheet2.xml", customers);
        Add(zip, "xl/worksheets/sheet3.xml", scratch);
        Add(zip, "xl/styles.xml", styles);

        Add(zip, "xl/workbook.xml",
            $"<workbook xmlns=\"{ns}\" xmlns:r=\"{rns}\"><sheets>"
            + "<sheet name=\"Orders\" sheetId=\"1\" r:id=\"rId1\"/>"
            + "<sheet name=\"Customers\" sheetId=\"2\" r:id=\"rId2\"/>"
            + "<sheet name=\"Scratch\" sheetId=\"3\" state=\"hidden\" r:id=\"rId3\"/>"
            + "</sheets></workbook>");

        Add(zip, "xl/_rels/workbook.xml.rels",
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/"
            + "relationships\">"
            + $"<Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\" Type=\"{rns}/worksheet\"/>"
            + $"<Relationship Id=\"rId2\" Target=\"worksheets/sheet2.xml\" Type=\"{rns}/worksheet\"/>"
            + $"<Relationship Id=\"rId3\" Target=\"worksheets/sheet3.xml\" Type=\"{rns}/worksheet\"/>"
            + $"<Relationship Id=\"rId4\" Target=\"styles.xml\" Type=\"{rns}/styles\"/>"
            + "</Relationships>");

        static string Inline(string reference, string text)
            => $"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{text}</t></is></c>";

        static void Add(ZipArchive zip, string name, string content)
        {
            using var entry = zip.CreateEntry(name).Open();
            using var writer = new StreamWriter(entry, new UTF8Encoding(false));
            writer.Write("<?xml version=\"1.0\"?>" + content);
        }
    }

    /// <summary>
    /// A project still builds after a class is ADDED to it, on every target with screens.
    /// </summary>
    /// <remarks>
    /// Here rather than beside the other incremental tests, so it uses the one
    /// <see cref="RunDotNet"/> this suite already has instead of inventing a second way to
    /// run a build.
    /// <para>
    /// Every target, not just the one the feature was demonstrated on: composition differs
    /// between them, and a rule right for WPF and wrong for Blazor is exactly the defect shape
    /// this repository keeps producing. Note what this canNOT catch - a class with no seed
    /// data, or an unregistered validator, compiles perfectly. Those are covered by
    /// IncrementalScaffolderTests naming the three composition files directly.
    /// </para>
    /// </remarks>
    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("wpf")]
    [DataRow("winforms")]
    [DataRow("blazor")]
    public void ProjectWithAnAddedClass_StillBuilds(string targetKey)
    {
        if ((targetKey == "wpf" || targetKey == "winforms") && !OperatingSystem.IsWindows())
            Assert.Inconclusive($"The {targetKey} target needs the Windows desktop targeting pack.");

        Assert.IsTrue(TargetScaffolders.TryResolve(targetKey, out var target));

        // Short, because staging plus a project name plus Models\Whatever.cs blows MAX_PATH.
        var workspace = Path.Combine(@"C:\cg-t", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workspace);

        try
        {
            var first = new AppScaffolder().Scaffold(
                ScaffoldRequest.For(AddModel("Customer"), "Sample", workspace, target));

            Assert.IsTrue(first.Success, "scaffold failed: " + first.Message);

            var added = AppScaffolding.Incremental.IncrementalScaffolder.Add(
                ScaffoldRequest.For(
                    AddModel("Customer", "Supplier"), "Sample", workspace, target),
                ["Supplier"]);

            Assert.IsTrue(added.Success, "add failed: " + added.Message);
            Assert.IsNotEmpty(added.Written, "the add wrote nothing at all");

            var (exit, output) = RunDotNet($"build \"{first.ProjectPath}\" -c Release");

            Assert.AreEqual(
                0, exit, $"{targetKey} did not build after a class was added:\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>
    /// An app built with no namespace prefix and edited by hand still builds after an add whose
    /// current answers name a prefix and a company - and keeps every hand edit.
    /// </summary>
    /// <remarks>
    /// The reported case: a Web API built piped, then added to interactively from a workspace
    /// whose profile named ABCLtd, had Book.cs, AppDbContext.cs, Program.cs and the csproj
    /// rewritten into ABCLtd.* (88 errors), and lost a HasIndex line, a MapGet and a method.
    /// </remarks>
    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("webapi", PersistenceKind.Sqlite)]
    [DataRow("blazor", PersistenceKind.Json)]
    [DataRow("wpf", PersistenceKind.Json)]
    public void AnAddUnderOtherAnswersKeepsTheAppBuildingAndItsHandEdits(
        string targetKey, PersistenceKind persistence)
    {
        if (targetKey == "wpf" && !OperatingSystem.IsWindows())
            Assert.Inconclusive("The wpf target needs the Windows desktop targeting pack.");

        Assert.IsTrue(TargetScaffolders.TryResolve(targetKey, out var target));

        var workspace = Path.Combine(@"C:\cg-t", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workspace);

        try
        {
            var first = new AppScaffolder().Scaffold(
                ScaffoldRequest.For(AddModel("Book", "Member"), "Lib", workspace, target)
                    with { Persistence = persistence });

            Assert.IsTrue(first.Success, "scaffold failed: " + first.Message);

            var folder = first.ProjectPath;
            var csprojPath = GeneratedProjectPath.CsprojPath(workspace, "Lib", target);
            var book = Path.Combine(folder, "Models", "Book.cs");
            var bookText = File.ReadAllText(book);
            var classLine = bookText.IndexOf("public class Book", StringComparison.Ordinal);
            Assert.IsGreaterThanOrEqualTo(0, classLine, "fixture: no class line in Book.cs");
            var closing = bookText.LastIndexOf('}', bookText.LastIndexOf('}') - 1);
            bookText = bookText.Insert(closing, "    public string Label() => \"HAND\";\n    ");
            File.WriteAllText(book, bookText);

            var seed = Path.Combine(folder, "Data", "SeedData.cs");
            File.WriteAllText(seed, "// HAND: seed\n" + File.ReadAllText(seed));

            var csproj = File.ReadAllBytes(csprojPath);

            var added = AppScaffolding.Incremental.IncrementalScaffolder.Add(
                ScaffoldRequest.For(AddModel("Book", "Member", "Author"), "Lib", workspace, target)
                    with
                    {
                        Persistence = persistence,
                        Customization = AppCustomization.Default with
                        {
                            NamespacePrefix = "ABCLtd", Company = "ABC Ltd.", Logging = true
                        },
                    },
                ["Author"]);

            Assert.IsTrue(added.Success, "add failed: " + added.Message);
            Assert.IsEmpty(added.NotMerged, string.Join(", ", added.NotMerged.Select(n => n.Path)));

            Assert.AreEqual(bookText, File.ReadAllText(book), "Book.cs was rewritten");
            StringAssert.Contains(File.ReadAllText(seed), "// HAND: seed");
            StringAssert.Contains(File.ReadAllText(seed), "Seed<Author>");
            CollectionAssert.AreEqual(csproj, File.ReadAllBytes(csprojPath), "csproj rewritten");

            foreach (var file in added.Written)
                Assert.DoesNotContain("ABCLtd", File.ReadAllText(file), file);

            var (exit, output) = RunDotNet($"build \"{first.ProjectPath}\" -c Release");

            Assert.AreEqual(0, exit, $"{targetKey} did not build after the add:\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    /// <summary>A model of plain masters, for the add tests.</summary>
    private static CandidateDomainModel AddModel(params string[] names)
    {
        var model = new CandidateDomainModel();

        foreach (var name in names)
        {
            var entity = new CandidateEntity { Name = name, Provenance = Provenance.Wiki };
            entity.Fields.Add(new CandidateField
            {
                Name = "Title", Type = "string", Provenance = Provenance.Wiki
            });
            model.Entities.Add(entity);
        }

        return model;
    }

    private static (int ExitCode, string Output) RunDotNet(string arguments)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(300_000);
        return (process.ExitCode, stdout + stderr);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
