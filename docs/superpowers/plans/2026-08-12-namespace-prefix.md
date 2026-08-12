# Namespace Prefix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let `build app` put the generated code under a company namespace root — `CodisLtd.Shop.Models` rather than `Shop.Models` — without changing the project name, folder, assembly or SQLite environment override.

**Architecture:** `AppCustomization` gains a `NamespacePrefix` and one derived accessor, `RootNamespaceFor(projectName)`. Because no template uses its name argument for anything but a namespace root, callers pass the root namespace where they pass the project name today. Three things outside the emitted set are reconciled on every run: `<RootNamespace>` in the app and test project files, and the `x:Class`/`clr-namespace`/`namespace` in the WPF `App.xaml` pair that `dotnet new` wrote.

**Tech Stack:** C# / .NET 10, MSTest, Roslyn (`Microsoft.CodeAnalysis.CSharp.SyntaxFacts` for identifier validation, `Compiler.Compile` for emitted-code tests), raw string literal templates.

**Spec:** `docs/superpowers/specs/2026-08-12-namespace-prefix-design.md`

**Branch:** `namespace-prefix` (already created off `main` at `f6cb89f`; the spec is committed at `465cebf`)

## Global Constraints

- **Repository root is `D:\SmartCodeGenerator-Repository`**, the PARENT of the tool's default working directory. Every path below is relative to that root. Use `git -C D:\SmartCodeGenerator-Repository ...`.
- **`AppCustomization.Default` must stay byte-for-byte neutral in the GENERATED OUTPUT.** With no prefix, `RootNamespaceFor(projectName) == projectName`, every emitted file is identical to today's, and no `<RootNamespace>` is written at all. If a test of *generated output* needs its expectations changed, the change is wrong.
- **The way that invariant breaks is invisible.** A raw string literal carries the line endings of the source file it is written in (CRLF on a stock Windows clone), so a template line lifted out and rebuilt with `"\n"` concatenation drops one CR per line and `diff` shows lines that look identical. Build any spliced fragment as a **raw string literal**. Three existing tests pin this: `NoValidationTests.The_validation_branch_leaves_the_line_endings_alone`, `NoSeedTests.The_seed_branch_leaves_the_line_endings_alone`, `LoggingTests.The_logging_branch_leaves_the_line_endings_alone`.
- **Proving neutrality:** `git worktree add D:/wt-head HEAD`, then a .NET 10 file-based app (first line `#:project D:/SmartCodeGenerator-Repository/AppScaffolding/AppScaffolding.csproj`, run with `dotnet run dump.cs -- <outdir>`) that writes every default-customization emitted file from each tree, then `diff -r`. Remove the worktree when finished.
- **Emitted code is verified by compiling it** — `EmittedSource.AssertCompiles` / `EmittedSource.Flatten` in `CodeGenerator.Tests/App/EmittedSource.cs` — and by real `dotnet build` runs in `CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs`. Substring assertions on emitted C# are not acceptable as the primary check.
- **Test framework is MSTest**: `[TestClass]`, `[TestMethod]`. Analyzer MSTEST0037 rejects `Assert.AreEqual(n, x.Count)` — use `Assert.HasCount(n, x)`. `Assert.IsEmpty` / `Assert.IsNotEmpty` exist and are used.
- **Local test command:** `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj`
- **CI parity command** (before the final commit): `dotnet build D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental`. The `--no-incremental` is required: an incremental build prints "Build succeeded" without rerunning analyzers.
- **Close any running CodeGenerator CLI before building** — a launched CLI locks `CodeGenerator/bin` and MSB3061 reports "access denied" on files that exist.
- **Generated-app probes need a SHORT path** (`C:\t\...`): the scratchpad path plus a generated project's nesting exceeds MAX_PATH, and MSBuild reports that as MSB3030 "file not found" on a file that is plainly there.

## The four double-duty parameters (read before Task 2)

Grepped, not assumed. Every emitted `namespace`/`using` comes from a template's name argument, but **four sites use that argument for something else**, and three of them break if handed a namespace:

| Site | Non-namespace use | Consequence of passing the root namespace |
| --- | --- | --- |
| `ConsoleProgramTemplate.cs:33` | `var banner = title == projectName ? empty : …` — the "unbranded" gate | with a prefix and no company, `title` (`Shop`) no longer equals the argument (`CodisLtd.Shop`), so an unbranded console app gains a `Console.WriteLine` it has never had |
| `WebApiProgramTemplate.cs:28` | `var rootEndpoint = title == projectName ? empty : …` — the same gate | an unbranded Web API gains a root endpoint |
| `DbContextTemplate.cs:86` | `"Data Source=" + Path.Combine(AppContext.BaseDirectory, "{{projectName}}.db")` — the **database file name** | the SQLite file is renamed to `CodisLtd.Shop.db`; the spec keeps project identity, including this, unchanged |
| `MainWindowTemplate.cs:47` | `x:Class="{{appName}}.Views.MainWindow"` | none — `x:Class` *is* the namespace, so this one correctly follows the prefix |

The first three therefore keep taking the **project name** in their existing parameter and gain a trailing `string? rootNamespace = null` that defaults to it. Every other template's name argument is a namespace root and simply receives one.

## File Structure

**Created**

| File | Responsibility |
| --- | --- |
| `AppScaffolding/NamespacePrefix.cs` | validates a prefix, and derives one from a company name |
| `CodeGenerator.Tests/App/NamespacePrefixTests.cs` | the value rules |
| `CodeGenerator.Tests/App/NamespaceScaffoldingTests.cs` | what a prefixed scaffold emits, and the two staleness traps |

**Modified**

| File | Change |
| --- | --- |
| `AppScaffolding/AppCustomization.cs` | `NamespacePrefix` member, `RootNamespaceFor` |
| `AppScaffolding/AppScaffolder.cs` | compute the root namespace; pass it to the core templates, the target and the test project; call the two reconcilers |
| `AppScaffolding/CsprojEditing.cs` | `SetRootNamespace` (update-or-insert, and remove on null) |
| `AppScaffolding/WpfProjectFixup.cs` | `ReconcileNamespace` for the `App.xaml` pair |
| `AppScaffolding/ConsoleProgramTemplate.cs`, `WebApiProgramTemplate.cs`, `DbContextTemplate.cs` | trailing `rootNamespace` parameter (see the table above) |
| `AppScaffolding/Targets/WpfTarget.cs`, `WinFormsTarget.cs`, `ConsoleTarget.cs`, `WebApiTarget.cs`, `WorkerTarget.cs`, `DesktopViewModelFiles.cs` | resolve the root namespace from the customization they already receive |
| `CodeGenerator/App/InlineCustomization.cs` | the `namespace` option |
| `CodeGenerator/App/CustomizationDefaults.cs` | `namespacePrefix` in the profile |
| `CodeGenerator/App/CustomizationWizard.cs` | the ninth prompt and the company-derived suggestion |
| `CodeGenerator/App/CustomizationSummary.cs` | the review-loop line |
| `CodeGenerationEngine/Core/CommandDictionary.cs` | the help option |
| `docs/commands.md` | regenerated from the catalog |
| `AppScaffolding/README.md` | document the prefix |
| `CodeGenerator.Tests/App/CustomizationWizardTests.cs`, `BuildAppWizardTests.cs`, `InlineCustomizationTests.cs`, `CustomizationDefaultsTests.cs`, `CustomizationMergeTests.cs`, `GeneratedProjectBuildTests.cs` | cover the new surface; the wizard tests gain the one input line the new prompt reads |

---

### Task 1: The prefix value type and the customization member

**Files:**
- Create: `AppScaffolding/NamespacePrefix.cs`
- Modify: `AppScaffolding/AppCustomization.cs`, `CodeGenerator/App/CustomizationDefaults.cs`
- Test: `CodeGenerator.Tests/App/NamespacePrefixTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `static class NamespacePrefix` with `static bool IsValid(string value)` and `static string? FromCompany(string company)`
  - `AppCustomization` gains the record member `string NamespacePrefix` (ninth positional member) and `string RootNamespaceFor(string projectName)`

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/NamespacePrefixTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The prefix is the one customization setting that can break the generated app's COMPILE, so it
/// is validated rather than ignored — unlike a malformed accent, which is dropped.
/// </summary>
[TestClass]
public class NamespacePrefixTests
{
    [TestMethod]
    public void A_plain_identifier_is_valid()
        => Assert.IsTrue(NamespacePrefix.IsValid("CodisLtd"));

    [TestMethod]
    public void Dotted_segments_are_valid()
        => Assert.IsTrue(NamespacePrefix.IsValid("Codis.Internal.Tools"));

    [TestMethod]
    public void An_underscore_start_is_valid()
        => Assert.IsTrue(NamespacePrefix.IsValid("_codis"));

    [TestMethod]
    public void Empty_is_not_a_prefix()
    {
        Assert.IsFalse(NamespacePrefix.IsValid(string.Empty));
        Assert.IsFalse(NamespacePrefix.IsValid("   "));
    }

    /// <summary>Every one of these produces source that does not compile, which is exactly why
    /// this check exists.</summary>
    [TestMethod]
    public void Malformed_prefixes_are_rejected()
    {
        Assert.IsFalse(NamespacePrefix.IsValid("3M"), "a segment may not start with a digit");
        Assert.IsFalse(NamespacePrefix.IsValid("Codis Ltd"), "a space is not an identifier char");
        Assert.IsFalse(NamespacePrefix.IsValid("Codis-Ltd"), "nor is a hyphen");
        Assert.IsFalse(NamespacePrefix.IsValid(".Codis"), "no empty leading segment");
        Assert.IsFalse(NamespacePrefix.IsValid("Codis."), "no empty trailing segment");
        Assert.IsFalse(NamespacePrefix.IsValid("Codis..Ltd"), "no empty middle segment");
        Assert.IsFalse(NamespacePrefix.IsValid("class"), "a reserved keyword is not a namespace");
        Assert.IsFalse(NamespacePrefix.IsValid("Codis.int"), "in any segment");
    }

    /// <summary>`var` is a CONTEXTUAL keyword, so `namespace var;` compiles. Rejecting it would be
    /// stricter than C# is.</summary>
    [TestMethod]
    public void A_contextual_keyword_is_allowed()
        => Assert.IsTrue(NamespacePrefix.IsValid("var"));

    [TestMethod]
    public void A_company_becomes_an_identifier()
    {
        Assert.AreEqual("CodisLtd", NamespacePrefix.FromCompany("Codis Ltd"));
        Assert.AreEqual("SmithSons", NamespacePrefix.FromCompany("Smith & Sons"));
        Assert.AreEqual("Codis", NamespacePrefix.FromCompany("  Codis  "));
    }

    /// <summary>A suggestion that would be refused is worse than no suggestion.</summary>
    [TestMethod]
    public void A_company_that_cannot_become_one_suggests_nothing()
    {
        Assert.IsNull(NamespacePrefix.FromCompany("3M"));
        Assert.IsNull(NamespacePrefix.FromCompany("&&&"));
        Assert.IsNull(NamespacePrefix.FromCompany(string.Empty));
    }

    [TestMethod]
    public void The_default_customization_has_no_prefix()
    {
        Assert.AreEqual(string.Empty, AppCustomization.Default.NamespacePrefix);
        Assert.AreEqual("Shop", AppCustomization.Default.RootNamespaceFor("Shop"));
    }

    [TestMethod]
    public void A_prefix_sits_above_the_whole_project_name()
    {
        var customization = AppCustomization.Default with { NamespacePrefix = "CodisLtd" };

        Assert.AreEqual("CodisLtd.Shop", customization.RootNamespaceFor("Shop"));

        // The project name already carries the target's folder suffix, so the prefix sits above
        // the whole project identity rather than inside it.
        Assert.AreEqual("CodisLtd.Shop.Console", customization.RootNamespaceFor("Shop.Console"));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter NamespacePrefixTests`

Expected: FAIL — the build fails, `NamespacePrefix` does not exist.

- [ ] **Step 3: Create the value type**

Create `AppScaffolding/NamespacePrefix.cs`:

```csharp
using Microsoft.CodeAnalysis.CSharp;

namespace AppScaffolding;

/// <summary>
/// The namespace root a generated app's code is placed under, above the project name.
/// </summary>
/// <remarks>
/// Validated rather than ignored, unlike every other customization value: a malformed accent
/// costs a colour, but a malformed namespace does not compile. Roslyn answers both halves of the
/// question — <c>SyntaxFacts.IsValidIdentifier</c> for the shape and
/// <c>SyntaxFacts.GetKeywordKind</c> for the reserved words — rather than a hand-kept keyword
/// list that would drift from the language.
/// </remarks>
public static class NamespacePrefix
{
    /// <summary>Whether <paramref name="value"/> can be written after <c>namespace</c>.</summary>
    /// <remarks>
    /// Contextual keywords pass: `namespace var;` compiles, so rejecting it would be stricter than
    /// C# is. <c>GetKeywordKind</c> returns <c>SyntaxKind.None</c> for those, which is the
    /// distinction being relied on here.
    /// </remarks>
    public static bool IsValid(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var segments = value.Split('.');

        return segments.All(segment =>
            SyntaxFacts.IsValidIdentifier(segment)
            && SyntaxFacts.GetKeywordKind(segment) == SyntaxKind.None);
    }

    /// <summary>
    /// <paramref name="company"/> reduced to a single identifier — every character that cannot
    /// appear in one removed — or null when the result would not be a valid prefix.
    /// </summary>
    /// <remarks>
    /// Used only to pre-fill the wizard's prompt. A company like "3M" reduces to "3M", which no
    /// namespace may be called, and offering it would put a value in front of the user that the
    /// same run would then refuse.
    /// </remarks>
    public static string? FromCompany(string company)
    {
        var identifier = new string(
            company.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

        return IsValid(identifier) ? identifier : null;
    }
}
```

- [ ] **Step 4: Add the customization member**

In `AppScaffolding/AppCustomization.cs`, add the ninth positional member to the record declaration, after `SampleSeedData`:

```csharp
public sealed record AppCustomization(
    string Company,
    AppTheme Theme,
    string? Accent,
    string TitlePattern,
    string? IconPath,
    bool Validation,
    bool Logging,
    bool SampleSeedData,
    string NamespacePrefix)
```

Add `NamespacePrefix: string.Empty` to the `Default` initializer, and this accessor beside `RootNamespaceFor`'s neighbours:

```csharp
    /// <summary>
    /// The namespace root emitted code declares: the project name, under the prefix when there is
    /// one.
    /// </summary>
    /// <remarks>
    /// This is the whole mechanism. No template uses its name argument for anything but a
    /// namespace root, so passing this where the project name is passed today puts every emitted
    /// file under the prefix — while the project name goes on naming the folder, the csproj, the
    /// assembly and the SQLite environment override.
    /// </remarks>
    public string RootNamespaceFor(string projectName)
        => NamespacePrefix.Length == 0 ? projectName : $"{NamespacePrefix}.{projectName}";
```

**Naming hazard, worth knowing before the next tasks:** the record member and the static class are
both called `NamespacePrefix`, in the same `AppScaffolding` namespace. That is legal and reads well
at every call site the plan uses — `NamespacePrefix.IsValid(value)` from the CLI, `start.NamespacePrefix`
for the value — but inside `AppCustomization` itself the name resolves to the *property*, so the
type is unreachable there unqualified. Nothing in this plan needs it there; if a later change does,
qualify it as `AppScaffolding.NamespacePrefix`.

- [ ] **Step 5: Fix the two positional construction sites**

A positional record requires every member, so the compiler will point at both. In `CodeGenerator/App/CustomizationDefaults.cs`, add the property to the `Profile` class and the argument to the `new AppCustomization(...)` call in `Load`:

```csharp
        public string? NamespacePrefix { get; set; }
```

```csharp
                NamespacePrefix: profile.NamespacePrefix ?? fallback.NamespacePrefix);
```

and in `Save`, beside the other assignments:

```csharp
                NamespacePrefix = customization.NamespacePrefix,
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter NamespacePrefixTests`

Expected: PASS, 10 tests.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj`

Expected: PASS. Nothing consumes the prefix yet, so no existing test may move.

- [ ] **Step 8: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add AppScaffolding/NamespacePrefix.cs AppScaffolding/AppCustomization.cs CodeGenerator/App/CustomizationDefaults.cs CodeGenerator.Tests/App/NamespacePrefixTests.cs
git -C D:\SmartCodeGenerator-Repository commit -m "feat(scaffold): add the namespace prefix value rules and customization member"
```

---

### Task 2: Thread the root namespace through the scaffolder and the targets

Every emitted namespace follows the prefix. **No output changes without one** — that is this task's deliverable and its test.

**Files:**
- Modify: `AppScaffolding/AppScaffolder.cs`, `ConsoleProgramTemplate.cs`, `WebApiProgramTemplate.cs`, `DbContextTemplate.cs`, `Targets/WpfTarget.cs`, `Targets/WinFormsTarget.cs`, `Targets/ConsoleTarget.cs`, `Targets/WebApiTarget.cs`, `Targets/WorkerTarget.cs`, `Targets/DesktopViewModelFiles.cs`
- Test: `CodeGenerator.Tests/App/NamespaceScaffoldingTests.cs`

**Interfaces:**
- Consumes: `AppCustomization.RootNamespaceFor` from Task 1.
- Produces:
  - `ConsoleProgramTemplate.Build(string projectName, IReadOnlyList<string> entityNames, string title, bool seed, string? rootNamespace = null)`
  - `WebApiProgramTemplate.Build(string projectName, IReadOnlyList<string> entityNames, string title, bool seed, string? rootNamespace = null)`
  - `DbContextTemplate.Source(string projectName, IReadOnlyList<string> entityNames, string envVarName, string? rootNamespace = null)`
  - `DesktopViewModelFiles.Build(string rootNamespace, IReadOnlyList<string> entityNames, string relayCommandSource, bool validation = true, bool seed = true)`
  - `ITargetScaffolder.FrontEndFiles` signatures are **unchanged**: each target derives the root namespace from the customization it already receives.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/NamespaceScaffoldingTests.cs`:

```csharp
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
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter NamespaceScaffoldingTests`

Expected: FAIL — `DbContextTemplate.Source` has no `rootNamespace` parameter, and the namespaces are unprefixed.

- [ ] **Step 3: Give the three double-duty templates a namespace parameter**

`AppScaffolding/ConsoleProgramTemplate.cs` — keep the existing four-argument overload delegating, and add the parameter to the widest one:

```csharp
    /// <param name="rootNamespace">
    /// The namespace this app's code is under, when it differs from <paramref name="projectName"/>.
    /// Separate from the project name because the banner gate below compares the title against the
    /// PROJECT name: a prefixed app whose title was never customized must stay bannerless.
    /// </param>
    public static string Build(
        string projectName, IReadOnlyList<string> entityNames, string title, bool seed,
        string? rootNamespace = null)
    {
        var ns = rootNamespace ?? projectName;
```

Replace the three `{{projectName}}` interpolations in that template's `using` lines with `{{ns}}`, and leave `var banner = title == projectName` exactly as it is.

`AppScaffolding/WebApiProgramTemplate.cs` — the same change, remembering this template is a `$$$"""` literal, so its placeholders are `{{{ns}}}`:

```csharp
    public static string Build(
        string projectName, IReadOnlyList<string> entityNames, string title, bool seed,
        string? rootNamespace = null)
    {
        var ns = rootNamespace ?? projectName;
```

`AppScaffolding/DbContextTemplate.cs`:

```csharp
    /// <param name="rootNamespace">
    /// The namespace to declare, when it differs from <paramref name="projectName"/>. The project
    /// name still names the .db file: the prefix is a namespace, and renaming the database would
    /// hide the data an existing app has already written.
    /// </param>
    public static string Source(
        string projectName, IReadOnlyList<string> entityNames, string envVarName,
        string? rootNamespace = null)
    {
        var ns = rootNamespace ?? projectName;
```

Change only `namespace {{projectName}}.Data;` to `namespace {{ns}}.Data;` and any `using {{projectName}}.` lines to `{{ns}}`. **Leave `"{{projectName}}.db"` at line 86 alone** — that is the file name.

- [ ] **Step 4: Resolve the namespace inside each target**

`ITargetScaffolder` does not change. Each target derives the root namespace from the customization it already has, so the project name stays available for the title. In `Targets/WpfTarget.cs`:

```csharp
    public IReadOnlyList<GeneratedFile> FrontEndFiles(
        string projectName, IReadOnlyList<string> entityNames, AppCustomization customization)
    {
        // The namespace the emitted code declares; projectName still names the app in its title.
        var ns = customization.RootNamespaceFor(projectName);

        var files = DesktopViewModelFiles.Build(
            ns, entityNames, ViewModelTemplates.RelayCommand(ns),
            customization.Validation, customization.SampleSeedData);

        var palette = ThemePalette.Resolve(customization.Theme, customization.Accent);

        files.Add(new("Views", "MainWindow.xaml",
            MainWindowTemplate.Xaml(
                ns, customization.TitleAttributeFor(projectName), palette is not null,
                customization.IconFileName)));
        files.Add(new("Views", "MainWindow.xaml.cs", MainWindowTemplate.CodeBehind(ns)));

        if (palette is not null)
            files.Add(new("Views", ThemeXamlTemplate.FileName, ThemeXamlTemplate.Source(palette)));

        return files;
    }
```

Make the same substitution in the other four: `WinFormsTarget` (`DesktopViewModelFiles.Build(ns, …)`, `ViewModelTemplates.PortableRelayCommand(ns)`, `WinFormsMainFormTemplate.Source(ns, customization.TitleLiteralFor(projectName), palette, customization.IconFileName)`, `WinFormsProgramTemplate.Source(ns)`), `ConsoleTarget` (`ConsoleMenuTemplate.Source(ns)`, and `ConsoleProgramTemplate.Build(projectName, entityNames, customization.TitleLiteralFor(projectName), customization.SampleSeedData, ns)`), `WebApiTarget` (`WebApiProgramTemplate.Build(projectName, entityNames, customization.TitleLiteralFor(projectName), customization.SampleSeedData, ns)`), and `WorkerTarget` (`WorkerTemplate.Build(ns, entityNames)`, `WorkerProgramTemplate.Source(ns, customization.SampleSeedData)`).

In `Targets/DesktopViewModelFiles.cs`, rename the first parameter to `rootNamespace` and pass it on unchanged — every use is already a namespace.

- [ ] **Step 5: Compute it once in the scaffolder**

In `AppScaffolding/AppScaffolder.cs`, immediately after the `customization` local is settled (after the `CopyIcon` block, before `modelsFolder`):

```csharp
        // The namespace emitted code declares. projectName goes on naming the folder, the csproj,
        // the assembly and the SQLite environment override — the prefix is a namespace only.
        var rootNamespace = customization.RootNamespaceFor(projectName);
```

Then replace `projectName` with `rootNamespace` at exactly these argument positions — they are the namespace roots, and the surrounding file names, folders and the env var must NOT change:

| Line (pre-edit) | Change |
| --- | --- |
| 308 | `WriteClasses(modelsFolder, classes, $"{rootNamespace}.Models", header)` |
| 336, 337, 339, 345, 354, 360 | `StoreSeamTemplate.Source`, `StoreFactoryTemplate.Source`, `RepositoryTemplate.Base`, `LoggingTemplate.Source`, `SeedDataTemplate.Source`, `SaveResultTemplate.Source` all take `rootNamespace` |
| 367, 369, 371, 373 | the four `ValidatorTemplate` core files take `rootNamespace` |
| 379 | `EfStoreTemplate.Source(rootNamespace)` |
| 381 | `DbContextTemplate.Source(projectName, entityNames, EnvVarName(projectName), rootNamespace)` — **project name first, namespace last** |
| 385 | `JsonStoreTemplate.Source($"{rootNamespace}.Data")` |
| 389 | `RepositoryTemplate.Typed(rootNamespace, name)` — the file name still comes from `name` |
| 400 | `ValidatorTemplate.Generated(rootNamespace, definition, …)` |
| 404 | `target.FrontEndFiles(projectName, entityNames, customization)` — **unchanged**: the target resolves the namespace itself (Step 4) |
| 448 | `ValidatorTemplate.CustomStub(rootNamespace, definition.Name)` |
| 454 | `ScaffoldTestProject(workspaceRoot, projectName, rootNamespace, classes, validation, seed)` |

- [ ] **Step 6: Give the test project both names**

In `ScaffoldTestProject`, add the parameter and use it for namespaces only — the folder, the csproj name and the project reference are identity:

```csharp
    private static string? ScaffoldTestProject(
        string workspaceRoot, string appProjectName, string appRootNamespace,
        IReadOnlyList<ClassDefinition> classes, bool validation = true, bool seed = true)
```

Inside, the four `TestProjectTemplates` calls take `appRootNamespace` (`RoundTripTests`, `SeedTests`, `TestSupport`, `ValidatorTests`); `testProjectName`, `testFolder`, `testCsproj`, `appCsproj` and `AppProjectFramework` keep using `appProjectName`.

- [ ] **Step 7: Run the new test and the whole suite**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj`

Expected: PASS. **No existing test may be edited** — without a prefix this task changes nothing.

- [ ] **Step 8: Prove neutrality byte-for-byte**

Follow the Global Constraints recipe: worktree at the previous commit, dump every default-customization emitted file from both trees, `diff -r`. Include at minimum `RepositoryTemplate.Base`, `SeedDataTemplate.Source`, `DbContextTemplate.Source`, and each target's `FrontEndFiles` output for a two-entity model.

Expected: identical. A difference here means a `projectName` that should have stayed one was replaced.

- [ ] **Step 9: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add AppScaffolding CodeGenerator.Tests/App/NamespaceScaffoldingTests.cs
git -C D:\SmartCodeGenerator-Repository commit -m "feat(scaffold): declare emitted namespaces under the customization's prefix"
```

---

### Task 3: `<RootNamespace>` in the app and test project files

**Files:**
- Modify: `AppScaffolding/CsprojEditing.cs`, `AppScaffolding/AppScaffolder.cs`
- Test: `CodeGenerator.Tests/App/CsprojEditingTests.cs` (extend), `CodeGenerator.Tests/App/NamespaceScaffoldingTests.cs` (extend)

**Interfaces:**
- Consumes: `AppCustomization.RootNamespaceFor` from Task 1.
- Produces: `CsprojEditing.SetRootNamespace(string csproj, string? rootNamespace)` — inserts, updates, or (on null/empty) removes.

- [ ] **Step 1: Write the failing test**

Add to `CodeGenerator.Tests/App/CsprojEditingTests.cs`:

```csharp
    /// <summary>
    /// Update-or-insert, not insert-if-absent like the branding properties above: a stale root
    /// namespace would send every file the user adds later into a namespace no generated file
    /// declares.
    /// </summary>
    [TestMethod]
    public void A_root_namespace_is_inserted()
        => StringAssert.Contains(
            CsprojEditing.SetRootNamespace(Csproj, "CodisLtd.Shop"),
            "<RootNamespace>CodisLtd.Shop</RootNamespace>");

    [TestMethod]
    public void A_root_namespace_is_updated_in_place()
    {
        var once = CsprojEditing.SetRootNamespace(Csproj, "CodisLtd.Shop");
        var twice = CsprojEditing.SetRootNamespace(once, "Acme.Shop");

        StringAssert.Contains(twice, "<RootNamespace>Acme.Shop</RootNamespace>");
        Assert.IsFalse(twice.Contains("CodisLtd", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Setting_the_same_root_namespace_twice_changes_nothing()
    {
        var once = CsprojEditing.SetRootNamespace(Csproj, "CodisLtd.Shop");

        Assert.AreEqual(once, CsprojEditing.SetRootNamespace(once, "CodisLtd.Shop"));
    }

    /// <summary>Dropping the prefix has to take the element with it, or the project keeps a root
    /// namespace nothing in it declares.</summary>
    [TestMethod]
    public void An_empty_root_namespace_removes_the_element()
    {
        var set = CsprojEditing.SetRootNamespace(Csproj, "CodisLtd.Shop");
        var cleared = CsprojEditing.SetRootNamespace(set, null);

        Assert.IsFalse(cleared.Contains("RootNamespace", StringComparison.Ordinal));
    }

    [TestMethod]
    public void No_root_namespace_leaves_an_untouched_project_file_alone()
        => Assert.AreEqual(Csproj, CsprojEditing.SetRootNamespace(Csproj, null));
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter CsprojEditingTests`

Expected: FAIL — `SetRootNamespace` does not exist.

- [ ] **Step 3: Implement it**

Add to `AppScaffolding/CsprojEditing.cs`:

```csharp
    private const string RootNamespaceOpen = "<RootNamespace>";
    private const string RootNamespaceClose = "</RootNamespace>";

    /// <summary>
    /// Sets <c>&lt;RootNamespace&gt;</c> to <paramref name="rootNamespace"/>, replacing any
    /// existing value; a null or empty value removes the element. A no-op on a project file with
    /// no closing tag.
    /// </summary>
    /// <remarks>
    /// Update-or-insert rather than the insert-if-absent <see cref="InjectProperties"/> uses,
    /// because this value can go stale in a way that misleads the IDE: re-scaffolding an existing
    /// project under a different prefix would otherwise leave every file the user adds afterwards
    /// in a namespace no generated file declares. Removing it on an empty value matters for the
    /// same reason, in reverse.
    /// </remarks>
    public static string SetRootNamespace(string csproj, string? rootNamespace)
    {
        var open = csproj.IndexOf(RootNamespaceOpen, StringComparison.Ordinal);

        if (open >= 0)
        {
            var close = csproj.IndexOf(RootNamespaceClose, open, StringComparison.Ordinal);

            if (close < 0)
                return csproj;

            var end = close + RootNamespaceClose.Length;

            if (string.IsNullOrEmpty(rootNamespace))
            {
                // Take the whole line, so removing the element does not leave a blank one behind.
                var lineStart = csproj.LastIndexOf('\n', open) + 1;
                var lineEnd = end < csproj.Length && csproj[end] == '\n' ? end + 1 : end;

                return csproj[..lineStart] + csproj[lineEnd..];
            }

            return csproj[..open] + RootNamespaceOpen + rootNamespace + csproj[close..];
        }

        if (string.IsNullOrEmpty(rootNamespace))
            return csproj;

        var group =
            "  <PropertyGroup>\n" +
            $"    {RootNamespaceOpen}{rootNamespace}{RootNamespaceClose}\n" +
            "  </PropertyGroup>\n";

        var closeProject = csproj.LastIndexOf("</Project>", StringComparison.Ordinal);
        return closeProject < 0 ? csproj : csproj.Insert(closeProject, group);
    }
```

- [ ] **Step 4: Call it from the scaffolder**

In `AppScaffolder.Scaffold(ScaffoldRequest)`, beside the other csproj edits (after the logging package block), add a best-effort writer modelled on `InjectBranding`:

```csharp
        // Written on every run, not only at creation: a project re-scaffolded under a different
        // prefix must not keep pointing at the old namespace, and one re-scaffolded without a
        // prefix must lose the element entirely.
        SetRootNamespace(csprojPath, customization.NamespacePrefix.Length == 0 ? null : rootNamespace);
```

with:

```csharp
    /// <summary>Best-effort root-namespace reconciliation for a project file. Silent on failure:
    /// a project whose RootNamespace could not be written still builds, because every generated
    /// file declares its namespace explicitly.</summary>
    private static void SetRootNamespace(string csprojPath, string? rootNamespace)
    {
        try
        {
            if (!File.Exists(csprojPath))
                return;

            File.WriteAllText(
                csprojPath,
                CsprojEditing.SetRootNamespace(File.ReadAllText(csprojPath), rootNamespace));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
```

Note the `rootNamespace` local is declared in Task 2 Step 5; this call must come after it.

In `ScaffoldTestProject`, after the project reference is injected and before the sources are written, do the same for the test project, whose namespace is the app's root plus `.Tests`:

```csharp
        SetRootNamespace(
            testCsproj,
            appRootNamespace == appProjectName ? null : appRootNamespace + ".Tests");
```

- [ ] **Step 5: Extend the scaffolding test**

Add to `CodeGenerator.Tests/App/NamespaceScaffoldingTests.cs`:

```csharp
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
```

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj`

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add AppScaffolding CodeGenerator.Tests/App
git -C D:\SmartCodeGenerator-Repository commit -m "feat(scaffold): reconcile RootNamespace in the app and test project files"
```

---

### Task 4: The WPF `App.xaml` pair

The only `dotnet new` output that survives the existing fixups with a project-name namespace. Probed against every template: `winforms`'s `Form1*` and `classlib`'s `Class1.cs` are deleted by their fixups, `worker`'s `Worker.cs` and every `Program.cs` are overwritten by ours, and `console`/`webapi` `Program.cs` files have no namespace at all.

**Files:**
- Modify: `AppScaffolding/WpfProjectFixup.cs`, `AppScaffolding/AppScaffolder.cs`
- Test: `CodeGenerator.Tests/App/WpfProjectFixupTests.cs` (extend)

**Interfaces:**
- Consumes: nothing from earlier tasks (takes the resolved namespace as a string).
- Produces: `WpfProjectFixup.ReconcileNamespace(string projectFolder, string rootNamespace)`.

- [ ] **Step 1: Write the failing test**

Add to `CodeGenerator.Tests/App/WpfProjectFixupTests.cs`. Read the existing class first for its temp-folder helper and reuse it rather than adding a second one; the fixtures below are what `dotnet new wpf` actually emits:

```csharp
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
```

If the existing test class has no folder helper, add:

```csharp
    private string NewProjectFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wpf-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        return folder;
    }
```

and delete it in an existing `[TestCleanup]`, or track the folders in a list and remove them there.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter WpfProjectFixupTests`

Expected: FAIL — `ReconcileNamespace` does not exist.

- [ ] **Step 3: Implement it**

Add to `AppScaffolding/WpfProjectFixup.cs`:

```csharp
    /// <summary>
    /// Repoints <c>App.xaml</c>'s <c>x:Class</c> and <c>clr-namespace</c>, and its code-behind's
    /// <c>namespace</c>, at <paramref name="rootNamespace"/>. Best-effort and idempotent.
    /// </summary>
    /// <remarks>
    /// These two files come from <c>dotnet new wpf</c>, which named them after the project, and
    /// neither is deleted nor overwritten by <see cref="Apply"/>. With a namespace prefix the
    /// generated views declare a different namespace, and <c>x:Class</c> naming the old one does
    /// not compile.
    /// <para>
    /// Called on every scaffold rather than only at creation — see
    /// <see cref="MergeTheme"/> for the same reasoning — so it must work from whatever the files
    /// currently say. It matches the SHAPE (<c>x:Class="….App"</c>, <c>clr-namespace:…</c>,
    /// <c>namespace …;</c>) and never a remembered previous value, which a re-scaffold under a
    /// changed prefix would not have.
    /// </para>
    /// </remarks>
    public static void ReconcileNamespace(string projectFolder, string rootNamespace)
    {
        Rewrite(
            Path.Combine(projectFolder, "App.xaml"),
            xaml => Regex.Replace(
                Regex.Replace(
                    xaml,
                    "x:Class=\"[^\"]*\\.App\"",
                    $"x:Class=\"{rootNamespace}.App\""),
                "clr-namespace:[^\"]*\"",
                $"clr-namespace:{rootNamespace}\""));

        // Both namespace forms: net10's template emits a file-scoped declaration, but a block one
        // must not be corrupted if some template version emits it.
        Rewrite(
            Path.Combine(projectFolder, "App.xaml.cs"),
            code => Regex.Replace(
                code,
                @"^namespace\s+[^;{\s]+",
                $"namespace {rootNamespace}",
                RegexOptions.Multiline));
    }

    /// <summary>Best-effort read-transform-write. A file that cannot be read or written is left
    /// alone: the build error that follows is clearer than a crash here.</summary>
    private static void Rewrite(string path, Func<string, string> transform)
    {
        try
        {
            if (!File.Exists(path))
                return;

            var original = File.ReadAllText(path);
            var updated = transform(original);

            if (!string.Equals(original, updated, StringComparison.Ordinal))
                File.WriteAllText(path, updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
```

Add `using System.Text.RegularExpressions;` at the top of the file.

- [ ] **Step 4: Call it from the scaffolder**

In `AppScaffolder.Scaffold(ScaffoldRequest)`, extend the existing WPF block that runs after the files are written:

```csharp
        // After the files are written, not in FixupProject: Views/Theme.xaml does not exist yet
        // when the project is created, and the namespace has to be reconciled on every run rather
        // than only on the run that created the project.
        if (target.ProjectType == DotNetProjectType.Wpf)
        {
            WpfProjectFixup.MergeTheme(outputFolder);
            WpfProjectFixup.ReconcileNamespace(outputFolder, rootNamespace);
        }
```

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj`

Expected: PASS. Without a prefix `rootNamespace == projectName`, so the reconcile is a no-op on a freshly created project and no existing WPF test moves.

- [ ] **Step 6: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add AppScaffolding CodeGenerator.Tests/App/WpfProjectFixupTests.cs
git -C D:\SmartCodeGenerator-Repository commit -m "feat(scaffold): repoint the WPF App.xaml pair at the prefixed namespace"
```

---

### Task 5: The inline `namespace` option and the remembered profile

**Files:**
- Modify: `CodeGenerator/App/InlineCustomization.cs`
- Test: `CodeGenerator.Tests/App/InlineCustomizationTests.cs` (extend), `CodeGenerator.Tests/App/CustomizationDefaultsTests.cs` (extend), `CodeGenerator.Tests/App/CustomizationMergeTests.cs` (extend)

**Interfaces:**
- Consumes: `NamespacePrefix.IsValid` and `AppCustomization.NamespacePrefix` from Task 1.
- Produces: no new types — `InlineCustomization.Split` recognizes `namespace <value>`.

- [ ] **Step 1: Write the failing test**

Add to `CodeGenerator.Tests/App/InlineCustomizationTests.cs`:

```csharp
    [TestMethod]
    public void A_namespace_prefix_is_read()
    {
        var (remaining, customization) = Split("a shop namespace CodisLtd");

        Assert.AreEqual("a shop", remaining);
        Assert.AreEqual("CodisLtd", customization.NamespacePrefix);
        Assert.AreEqual("CodisLtd.Shop", customization.RootNamespaceFor("Shop"));
    }

    [TestMethod]
    public void A_dotted_namespace_prefix_is_read()
    {
        var (_, customization) = Split("a shop namespace \"Codis.Internal\"");

        Assert.AreEqual("Codis.Internal", customization.NamespacePrefix);
    }

    /// <summary>Unlike a malformed accent, which is ignored, a malformed namespace cannot be
    /// accepted at all — it would not compile. The words stay part of the description, which is
    /// what `theme mauve` already does.</summary>
    [TestMethod]
    public void A_malformed_namespace_prefix_is_left_on_the_line()
    {
        var (remaining, customization) = Split("a shop namespace 3M");

        Assert.AreEqual("a shop namespace 3M", remaining);
        Assert.AreEqual(string.Empty, customization.NamespacePrefix);
    }
```

Add to `CodeGenerator.Tests/App/CustomizationDefaultsTests.cs`, inside `A_saved_profile_round_trips`'s `saved` initializer:

```csharp
            NamespacePrefix = "CodisLtd",
```

Add to `CodeGenerator.Tests/App/CustomizationMergeTests.cs`, in the `Saved` initializer and a new test:

```csharp
        NamespacePrefix = "CodisLtd",
```

```csharp
    [TestMethod]
    public void A_typed_namespace_prefix_overrides_the_profile()
    {
        var merged = BuildAppCommand.Merge(
            Saved, AppCustomization.Default with { NamespacePrefix = "Acme" });

        Assert.AreEqual("Acme", merged.NamespacePrefix);
        Assert.AreEqual("Codis Ltd", merged.Company);
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter "InlineCustomizationTests|CustomizationDefaultsTests|CustomizationMergeTests"`

Expected: FAIL — the option is not recognized, so the words stay on the line and the prefix stays empty.

- [ ] **Step 3: Implement it**

In `CodeGenerator/App/InlineCustomization.cs`, add `"namespace"` to `ValueKeys`:

```csharp
    private static readonly string[] ValueKeys =
        ["company", "theme", "accent", "icon", "title", "namespace"];
```

and a case in `ApplyValue`:

```csharp
            case "namespace":
                // Refused rather than ignored, unlike accent: an invalid namespace does not
                // compile, and returning null leaves the words in the description where a user
                // who was describing rather than configuring meant them to be.
                return NamespacePrefix.IsValid(value)
                    ? customization with { NamespacePrefix = value }
                    : null;
```

`Merge` in `BuildAppCommand` needs no change: it compares each member against the default generically.

- [ ] **Step 4: Run them to verify they pass**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter "InlineCustomizationTests|CustomizationDefaultsTests|CustomizationMergeTests"`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add CodeGenerator/App/InlineCustomization.cs CodeGenerator.Tests/App
git -C D:\SmartCodeGenerator-Repository commit -m "feat(build-app): read a namespace prefix off the command line"
```

---

### Task 6: The wizard prompt and the review-loop line

**Files:**
- Modify: `CodeGenerator/App/CustomizationWizard.cs`, `CodeGenerator/App/CustomizationSummary.cs`
- Test: `CodeGenerator.Tests/App/CustomizationWizardTests.cs` (extend), `CodeGenerator.Tests/App/BuildAppWizardTests.cs` (one input line per scripted run), `CodeGenerator.Tests/App/CustomizationMergeTests.cs` (extend)

**Interfaces:**
- Consumes: `NamespacePrefix.FromCompany` / `IsValid` from Task 1.
- Produces: no new types. `CustomizationWizard.Run` asks a ninth question, after the title and before the three feature toggles.

- [ ] **Step 1: Write the failing test**

Add to `CodeGenerator.Tests/App/CustomizationWizardTests.cs`. **Every existing multi-answer script in this class gains one blank line**, because the wizard now reads one more: `Accepting_and_answering_collects_every_setting`, `Naming_a_company_offers_the_company_title_pattern`, `Blank_answers_keep_the_prefilled_values`, `A_malformed_accent_keeps_the_previous_one_rather_than_failing`, `None_clears_a_remembered_accent_and_icon` and `Every_prompt_shows_the_value_it_would_keep`. The new prompt sits after "Window title", so the extra line goes in that position, not at the end.

**One of those needs more than a blank line, and it is the trap this plan inherits from the last
one.** `Blank_answers_keep_the_prefilled_values` asserts `Assert.AreEqual(saved, result)` — an exact
round trip — with a `saved` whose company is `Codis Ltd` and whose prefix is empty. The company
suggestion then fills the prefix with `CodisLtd`, and the round trip is no longer an identity, so the
test fails for the right reason. Fix it by seeding the fixture, exactly as the same test already
seeds `TitlePattern` for the title suggestion:

```csharp
        var saved = AppCustomization.Default with
        {
            Company = "Codis Ltd",
            Theme = AppTheme.Dark,
            TitlePattern = AppCustomization.CompanyTitlePattern,
            NamespacePrefix = "CodisLtd",
        };
```

Do **not** weaken the suggestion to make that test pass — the suggestion is the behaviour being
built, and `Naming_a_company_offers_it_as_the_namespace_prefix` below pins it.

```csharp
    [TestMethod]
    public void A_namespace_prefix_is_collected()
    {
        var result = Run("y\n\n\n\n\n\nCodisLtd\n\n\n\n");

        Assert.IsNotNull(result);
        Assert.AreEqual("CodisLtd", result.NamespacePrefix);
    }

    /// <summary>A company just named is the likeliest prefix, so it is offered — the same courtesy
    /// as a named company adopting the company title pattern. Blank accepts it.</summary>
    [TestMethod]
    public void Naming_a_company_offers_it_as_the_namespace_prefix()
    {
        var result = Run("y\nCodis Ltd\n\n\n\n\n\n\n\n\n");

        Assert.IsNotNull(result);
        Assert.AreEqual("CodisLtd", result.NamespacePrefix);
    }

    /// <summary>A company that cannot become an identifier must not be offered as one: the same
    /// run would refuse it.</summary>
    [TestMethod]
    public void A_company_that_is_not_an_identifier_offers_no_prefix()
    {
        var result = Run("y\n3M\n\n\n\n\n\n\n\n\n");

        Assert.IsNotNull(result);
        Assert.AreEqual(string.Empty, result.NamespacePrefix);
    }

    /// <summary>An explicit prefix is not overwritten by the company suggestion.</summary>
    [TestMethod]
    public void An_existing_prefix_is_kept_over_the_company_suggestion()
    {
        var saved = AppCustomization.Default with { NamespacePrefix = "Acme" };

        var result = Run("y\nCodis Ltd\n\n\n\n\n\n\n\n\n", saved);

        Assert.IsNotNull(result);
        Assert.AreEqual("Acme", result.NamespacePrefix);
    }

    [TestMethod]
    public void A_malformed_namespace_prefix_keeps_the_previous_one()
    {
        var saved = AppCustomization.Default with { NamespacePrefix = "CodisLtd" };

        var result = Run("y\n\n\n\n\n\nnot a namespace\n\n\n\n", saved);

        Assert.IsNotNull(result);
        Assert.AreEqual("CodisLtd", result.NamespacePrefix);
    }

    [TestMethod]
    public void None_clears_a_remembered_namespace_prefix()
    {
        var saved = AppCustomization.Default with { NamespacePrefix = "CodisLtd" };

        var result = Run("y\n\n\n\n\n\nnone\n\n\n\n", saved);

        Assert.IsNotNull(result);
        Assert.AreEqual(string.Empty, result.NamespacePrefix);
    }
```

Add to `CodeGenerator.Tests/App/CustomizationMergeTests.cs`:

```csharp
    [TestMethod]
    public void The_namespace_prefix_is_named_in_the_review_loop()
    {
        var lines = CustomizationSummary.Lines(
            AppCustomization.Default with { NamespacePrefix = "CodisLtd" });

        Assert.HasCount(1, lines);
        StringAssert.Contains(lines[0], "Namespace prefix: CodisLtd");
    }
```

In `CodeGenerator.Tests/App/BuildAppWizardTests.cs`, every script that reaches the customization step and answers `y` gains a blank line in the same position; the runs that answer `n` at the gate are unaffected. Only `TheCustomizationStep_IsAskedLast` answers `y` — its script becomes:

```csharp
            Bare(), new StringReader("a shop\nn\ny\nCodis Ltd\n\n\n\n\n\n\n\n\n"), writer, () => 0);
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter "CustomizationWizardTests|CustomizationMergeTests|BuildAppWizardTests"`

Expected: FAIL — there is no ninth prompt and no summary line.

- [ ] **Step 3: Add the prompt**

In `CodeGenerator/App/CustomizationWizard.cs`, after the title prompt and before the `Validation layer` question:

```csharp
        // A company just named is the likeliest prefix — the same courtesy as the title pattern
        // above. Only offered when nothing is set yet, and only when it reduces to something a
        // namespace may actually be called, so the run never refuses its own suggestion.
        var suggestedPrefix = start.NamespacePrefix.Length == 0 && company.Length > 0
            ? NamespacePrefix.FromCompany(company) ?? string.Empty
            : start.NamespacePrefix;

        var prefix = Ask(
            reader, writer, "Namespace prefix",
            suggestedPrefix.Length == 0 ? NoneLabel : suggestedPrefix);

        if (prefix is null) return null;
```

and in the returned `start with { … }`:

```csharp
            NamespacePrefix = ParsePrefix(prefix, suggestedPrefix),
```

with:

```csharp
    /// <summary>The typed prefix, the offered one when the answer is unusable, or none.</summary>
    /// <remarks>
    /// An invalid answer keeps what was offered rather than re-prompting, as a malformed accent
    /// does — but unlike an accent, an invalid namespace could not be emitted at all, so this is
    /// the one prompt where keeping the previous value also protects the build.
    /// </remarks>
    private static string ParsePrefix(string value, string suggested)
    {
        if (string.Equals(value, NoneLabel, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return NamespacePrefix.IsValid(value) ? value : suggested;
    }
```

- [ ] **Step 4: Add the summary line**

In `CodeGenerator/App/CustomizationSummary.cs`, after the `Title` line:

```csharp
        // The bare prefix, not the resolved root: the root ends in the target's folder suffix
        // (Shop.Console, Shop.WinForms), and the target can still be changed at `approve`.
        if (customization.NamespacePrefix.Length > 0)
            lines.Add($"  Namespace prefix: {customization.NamespacePrefix}");
```

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add CodeGenerator/App CodeGenerator.Tests/App
git -C D:\SmartCodeGenerator-Repository commit -m "feat(build-app): ask for a namespace prefix and show it in the review loop"
```

---

### Task 7: Help catalog and README

**Files:**
- Modify: `CodeGenerationEngine/Core/CommandDictionary.cs`, `docs/commands.md`, `AppScaffolding/README.md`
- Test: `CodeGenerator.Tests/Core/CommandDictionaryDataTests.cs` (extend)

**Interfaces:**
- Consumes: nothing.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the failing test**

In `CodeGenerator.Tests/Core/CommandDictionaryDataTests.cs`, add `"namespace"` to the option list in `The_help_catalog_documents_the_customization_options`:

```csharp
        foreach (var option in new[]
                 {
                     "company", "theme", "accent", "icon", "title", "namespace",
                     "novalidation", "logging", "noseed",
                 })
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter CommandDictionaryDataTests`

Expected: FAIL — `'namespace' is not in the help catalog.`

- [ ] **Step 3: Extend the catalog entry**

In `CodeGenerationEngine/Core/CommandDictionary.cs`, in the `build app: <description> [customization options]` entry, extend the description and example:

```csharp
            Description = "Customizes the generated application. Options go LAST on the line, "
                        + "after any data source: company \"<name>\" sets the company (used in "
                        + "the project metadata, the window title and a copyright header); "
                        + "theme light|dark and accent #rrggbb colour the desktop front ends; "
                        + "icon <path> supplies a .ico or .png; title \"<pattern>\" overrides the "
                        + "window title, where {company} and {app} are substituted; "
                        + "namespace <prefix> puts the generated code under that namespace root "
                        + "(CodisLtd.Shop), leaving the project and assembly names alone.",
            Example = "build app: a shop company \"Codis Ltd\" namespace CodisLtd theme dark"
```

- [ ] **Step 4: Regenerate the committed doc**

`docs/commands.md` is generated from the catalog and its own test fails when it is stale:

Run: `UPDATE_COMMAND_DOCS=1 dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter CommandsDoc_MatchesTheCatalog`

- [ ] **Step 5: Document it in the scaffolding README**

In `AppScaffolding/README.md`, add a row to the Customization table:

```markdown
| `NamespacePrefix` | The namespace root every emitted file declares: `RootNamespaceFor(projectName)` puts the project name under the prefix, so `Shop.Console` becomes `CodisLtd.Shop.Console`. The project name, folder, assembly, `<AssemblyName>` and the SQLite `<PROJECT>_DB` override are deliberately untouched — the prefix is a namespace only. `<RootNamespace>` is written into the app and test project files, and WPF's template-generated `App.xaml`/`App.xaml.cs` are repointed at it, both on every run so a changed or dropped prefix cannot leave a stale one behind. |
```

Add `NamespacePrefix` to the Key types table:

```markdown
| `NamespacePrefix` | Whether a prefix can be written after `namespace` — Roslyn's `SyntaxFacts.IsValidIdentifier` plus a reserved-keyword check per dot-separated segment, so contextual keywords like `var` stay legal — and `FromCompany`, which reduces a company name to an identifier for the wizard to offer, or null when it cannot. |
```

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj`

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add CodeGenerationEngine/Core/CommandDictionary.cs docs/commands.md AppScaffolding/README.md CodeGenerator.Tests/Core/CommandDictionaryDataTests.cs
git -C D:\SmartCodeGenerator-Repository commit -m "docs(help): document the namespace prefix option"
```

---

### Task 8: Prove a prefixed app builds

Unit tests prove the emitted strings say the right thing. Only MSBuild and the XAML compiler can prove `x:Class` resolves.

**Files:**
- Modify: `CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: nothing.

- [ ] **Step 1: Write the failing test**

Add to `CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs`:

```csharp
    /// <summary>
    /// A prefixed app builds. This is the only check that can fail on the thing the prefix risks:
    /// WPF's x:Class is resolved by the XAML compiler against the namespaces the generated views
    /// declare, and App.xaml comes from `dotnet new` naming the project instead. The console row
    /// adds the generated test project, whose own usings are rooted at the prefix too.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("console")]
    [DataRow("wpf")]
    [DataRow("winforms")]
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
```

- [ ] **Step 2: Run them**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter "PrefixedProject_BuildsAndItsTestsPass|AProjectRegeneratedUnderAChangedPrefix_StillBuilds"`

Expected: PASS. If the WPF row fails on `x:Class`, read the MSBuild error before changing anything — it names the namespace it could not find, which says whether `App.xaml`, its code-behind or a generated view is the odd one out.

- [ ] **Step 3: Verify through the real CLI**

`dotnet test` leaves the CLI's `bin` stale, so rebuild first:

```bash
dotnet build D:\SmartCodeGenerator-Repository\CodeGenerator\CodeGenerator.csproj -c Debug
```

Then drive a piped run (redirected stdin never prompts, so the inline option carries the prefix). Use a short path:

```bash
mkdir C:\t\ns
cd C:\t\ns
printf 'build app: a library that lends books to members company "Codis Ltd" namespace CodisLtd\napprove console\nexit\n' | D:\SmartCodeGenerator-Repository\CodeGenerator\bin\Debug\net10.0\CodeGenerator.exe
```

Confirm from the output that the review loop printed `Namespace prefix: CodisLtd`, then build what it generated (the CLI writes to its own workspace, printed as "Generated project at …", not the current directory) and check by inspection:

- `Models/*.cs` declare `namespace CodisLtd.<Project>.Models`
- the csproj contains `<RootNamespace>CodisLtd.<Project></RootNamespace>` and still has no `<AssemblyName>`
- the folder and csproj file are still named for the project alone

- [ ] **Step 4: Run the CI-parity build and every test project**

```bash
dotnet build D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental
dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx -c Release
```

Expected: no warnings, no errors, all five test projects green. Record the counts.

- [ ] **Step 5: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs
git -C D:\SmartCodeGenerator-Repository commit -m "test(scaffold): build a prefixed app, and one whose prefix changed"
```

- [ ] **Step 6: Finish the branch**

Use superpowers:finishing-a-development-branch. The base branch is `main`.
