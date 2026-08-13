# Window Layout Customization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let `build app` choose the shape of the generated desktop front end — `tabs` (today's), `sidebar`, `master-detail` or `dashboard` — on both the WPF and Windows Forms targets.

**Architecture:** One nullable `AppLayout?` member on `AppCustomization` reaches the targets through the `FrontEndFiles(projectName, entityNames, customization)` default interface method that already exists, so `ITargetScaffolder` does not change. Each layout is a small class behind `IAppLayout`, resolved from a per-target registry shaped exactly like the existing `TargetScaffolders`. The `tabs` layouts delegate to today's untouched templates, which is what makes the default output byte-identical.

**Tech Stack:** C# 13 / .NET 10, MSTest 4.0.2, Roslyn (`CodeGenerationEngine.Core.Compiler`) for compile-testing emitted C#, WPF (XAML) and Windows Forms for the generated apps.

**Spec:** `docs/superpowers/specs/2026-08-13-app-layout-customization-design.md`

## Global Constraints

- **Byte-neutrality is the hard requirement.** No `layout` option, or `layout tabs`, must reproduce today's output **byte for byte**. If an existing test needs editing to stay green, the change is not neutral — stop and re-think rather than editing the test.
- **Never edit an existing test to make a new change pass.** The existing `MainWindowTemplateTests`, `WinFormsMainFormTemplateTests` and every `CustomizedTitleTests` / `CustomizationMergeTests` case must pass untouched.
- **Emitted C# is verified by compiling it**, never by substring assertion alone. Use `EmittedSource.AssertCompiles` for plain C# and the new `EmittedDesktopSource.AssertCompiles` (Task 1) for anything referencing WPF or WinForms types. Substring and `ParseText` checks pass green on CS0023 and on literal-injection bugs.
- **Raw string literals only** for emitted template text. A template line rebuilt with `"\n"` concatenation silently drops a CR on this repo's CRLF checkout, and `diff` cannot see it.
- **Repo root is `D:\SmartCodeGenerator-Repository`** — the parent of the `SmartCodeGenerator` tool directory. Branch: `app-layout`, already created off `main` at `896b97e`.
- **Test project:** `CodeGenerator.Tests` (MSTest, `[TestClass]`/`[TestMethod]`, globally-imported `Microsoft.VisualStudio.TestTools.UnitTesting`). New tests go in `CodeGenerator.Tests\App\`.
- **Run tests with:** `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx` — CI builds that solution in Release with `-warnaserror`, and the CodeGenerator Debug build hides analyzer errors.
- **MSTest analyzer note:** `Assert.AreEqual(n, x.Count)` trips MSTEST0037. Use `Assert.HasCount(n, x)` / `Assert.IsEmpty` / `Assert.IsLessThan` as the existing tests do.
- **Layout keys are exactly:** `tabs`, `sidebar`, `master-detail`, `dashboard`. `master-detail` is hyphenated, never two words — the inline tokenizer scans right-to-left by whitespace.

---

### Task 1: Desktop-reference compile harness

The emitted WPF and WinForms code cannot be compiled by the existing `EmittedSource.AssertCompiles`: the test project targets `net10.0`, not `net10.0-windows`, so `PresentationFramework`, `WindowsBase` and `System.Windows.Forms` are absent from the test process's `TRUSTED_PLATFORM_ASSEMBLIES`. `Compiler.Compile` accepts an `additionalAssemblyPaths` argument, and the WindowsDesktop **reference** pack supplies exactly those assemblies.

This was proved by probe before this plan was written: with the 50 DLLs from `Microsoft.WindowsDesktop.App.Ref\10.0.11\ref\net10.0` a `ContentControl` subclass compiles with 0 errors and 0 warnings; without them the same source produces 18 errors. Every later task depends on this harness, so it comes first.

**Files:**
- Create: `CodeGenerator.Tests\App\EmittedDesktopSource.cs`
- Test: `CodeGenerator.Tests\App\EmittedDesktopSourceTests.cs`

**Interfaces:**
- Consumes: `CodeGenerationEngine.Core.Compiler.Compile(string, IEnumerable<string>?)`
- Produces: `EmittedDesktopSource.AssertCompiles(string source)` and `EmittedDesktopSource.DesktopReferences` (an `IReadOnlyList<string>` of DLL paths). Every later task's compile test calls `AssertCompiles`.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests\App\EmittedDesktopSourceTests.cs`:

```csharp
namespace CodeGenerator.Tests.App;

[TestClass]
public class EmittedDesktopSourceTests
{
    [TestMethod]
    public void DesktopReferences_FindsTheWindowsDesktopReferencePack()
    {
        // A harness that silently found nothing would make every compile test in this suite
        // vacuous: the source would "compile" only because nothing was checked.
        Assert.IsNotEmpty(
            EmittedDesktopSource.DesktopReferences,
            "the Microsoft.WindowsDesktop.App.Ref pack was not found — the desktop compile tests "
            + "would be vacuous without it");
    }

    [TestMethod]
    public void AssertCompiles_AcceptsSourceUsingWpfTypes()
    {
        EmittedDesktopSource.AssertCompiles("""
            using System.Windows.Controls;

            namespace Acme.Views;

            public sealed class Probe : ContentControl
            {
                public Probe() => AddChild(new Grid());
            }
            """);
    }

    [TestMethod]
    public void AssertCompiles_AcceptsSourceUsingWinFormsTypes()
    {
        EmittedDesktopSource.AssertCompiles("""
            using System.Windows.Forms;

            namespace Acme.Views;

            public sealed class Probe : Form
            {
                public Probe() => Controls.Add(new PropertyGrid());
            }
            """);
    }

    [TestMethod]
    public void PlainHarness_CannotCompileWpfTypes_WhichIsWhyThisOneExists()
    {
        // The control for the two tests above. If EmittedSource could already do this, this
        // whole harness would be redundant — and if this test ever fails, delete the harness.
        var result = CodeGenerationEngine.Core.Compiler.Compile("""
            using System.Windows.Controls;

            namespace Acme.Views;

            public sealed class Probe : ContentControl { }
            """);

        Assert.IsFalse(
            result.Success,
            "the plain compiler unexpectedly resolved WPF types; EmittedDesktopSource is no longer needed");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~EmittedDesktopSourceTests"`

Expected: FAIL to **build** with CS0103 — `EmittedDesktopSource` does not exist. (A compile failure is the correct red for a missing type in C#; it is not a reason to skip step 3.)

- [ ] **Step 3: Write the harness**

Create `CodeGenerator.Tests\App\EmittedDesktopSource.cs`:

```csharp
using System.Linq;
using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Compiles emitted WPF / WinForms C# — the code <see cref="EmittedSource"/> cannot reach.
/// </summary>
/// <remarks>
/// This test project targets net10.0, not net10.0-windows, so PresentationFramework, WindowsBase
/// and System.Windows.Forms are not among the running process's TRUSTED_PLATFORM_ASSEMBLIES and
/// <see cref="Compiler.Compile(string, IEnumerable{string})"/> cannot resolve a Window, a
/// ContentControl or a Form. The WindowsDesktop REFERENCE pack shipped with the SDK supplies
/// exactly those assemblies, and mixing them with the runtime's own platform assemblies produces
/// no duplicate-assembly conflict — verified by probe, 0 errors and 0 warnings.
/// <para>
/// Retargeting the test project to net10.0-windows would be the alternative. It was rejected: it
/// would make the whole suite Windows-only for the sake of a handful of tests, and the emitted
/// desktop code is only ever compiled for real by the generated app's own build.
/// </para>
/// </remarks>
internal static class EmittedDesktopSource
{
    private const string PackRoot =
        @"C:\Program Files\dotnet\packs\Microsoft.WindowsDesktop.App.Ref";

    /// <summary>
    /// Every reference assembly in the newest installed WindowsDesktop ref pack, or empty when the
    /// pack is missing.
    /// </summary>
    /// <remarks>
    /// Empty rather than throwing, so the dedicated guard test in
    /// <c>EmittedDesktopSourceTests</c> reports the cause once instead of every desktop test
    /// failing with the same path error.
    /// </remarks>
    public static IReadOnlyList<string> DesktopReferences { get; } = LoadDesktopReferences();

    public static void AssertCompiles(string source)
    {
        Assert.IsNotEmpty(
            DesktopReferences,
            "the Microsoft.WindowsDesktop.App.Ref pack was not found, so this assertion would be "
            + "vacuous; see EmittedDesktopSourceTests");

        var result = Compiler.Compile(source, DesktopReferences);
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var message = string.Join("\n", errors.Select(e => e.ToString()));

        Assert.IsTrue(result.Success, $"Compilation failed:\n{message}");
        Assert.IsEmpty(errors, $"Compilation errors:\n{message}");
    }

    private static IReadOnlyList<string> LoadDesktopReferences()
    {
        try
        {
            if (!Directory.Exists(PackRoot))
                return [];

            // Ordinal-descending on the version folder name: 9.0.19 sorts above 10.0.11 that way,
            // so the newest pack is found by parsing the version rather than by string order.
            var newest = Directory.GetDirectories(PackRoot)
                .Select(d => (Dir: d, Parsed: Version.TryParse(Path.GetFileName(d), out var v) ? v : null))
                .Where(x => x.Parsed is not null)
                .OrderByDescending(x => x.Parsed)
                .Select(x => Path.Combine(x.Dir, "ref", "net10.0"))
                .FirstOrDefault(Directory.Exists);

            return newest is null ? [] : Directory.GetFiles(newest, "*.dll");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~EmittedDesktopSourceTests"`

Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
cd D:/SmartCodeGenerator-Repository
git add CodeGenerator.Tests/App/EmittedDesktopSource.cs CodeGenerator.Tests/App/EmittedDesktopSourceTests.cs
git commit -m "test: harness for compiling emitted WPF and WinForms source"
```

---

### Task 2: The `AppLayout` setting

**Files:**
- Create: `AppScaffolding\Layouts\AppLayout.cs`
- Modify: `AppScaffolding\AppCustomization.cs` (the record's parameter list and `Default`)
- Test: `CodeGenerator.Tests\App\AppLayoutSettingTests.cs`

**Interfaces:**
- Produces: `enum AppLayout { Tabs, Sidebar, MasterDetail, Dashboard }`; `AppCustomization.Layout` of type `AppLayout?`; `AppCustomization.EffectiveLayout` of type `AppLayout`. Every later task reads `EffectiveLayout` on the emit path and `Layout` on the CLI path.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests\App\AppLayoutSettingTests.cs`:

```csharp
using AppScaffolding;

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
    public void EffectiveLayout_IsTabsWhenNoneWasNamed()
    {
        Assert.AreEqual(AppLayout.Tabs, AppCustomization.Default.EffectiveLayout);
    }

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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~AppLayoutSettingTests"`

Expected: FAIL to build — `AppLayout` and `AppCustomization.Layout` do not exist.

- [ ] **Step 3: Add the enum**

Create `AppScaffolding\Layouts\AppLayout.cs`:

```csharp
namespace AppScaffolding;

/// <summary>
/// The shape of a generated desktop front end.
/// </summary>
/// <remarks>
/// <see cref="Tabs"/> is what this generator emitted before layouts existed, and every layout
/// mechanism treats it as the value that must reproduce that output byte for byte.
/// </remarks>
public enum AppLayout
{
    /// <summary>A tab per entity, its grid filling the tab.</summary>
    Tabs,

    /// <summary>A navigation list of entities, the selected one's grid beside it.</summary>
    Sidebar,

    /// <summary>Tab chrome, each pane split into a grid above and an edit form below.</summary>
    MasterDetail,

    /// <summary>A home screen of per-entity cards showing row counts, clicking through to a grid.</summary>
    Dashboard
}
```

- [ ] **Step 4: Add the member and the accessor**

In `AppScaffolding\AppCustomization.cs`, add the parameter to the record — **last**, after `NamespacePrefix`, so no positional caller changes:

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
    string NamespacePrefix,
    AppLayout? Layout)
```

Add `Layout: null` as the last argument of `Default`:

```csharp
    public static AppCustomization Default { get; } = new(
        Company: string.Empty,
        Theme: AppTheme.Default,
        Accent: null,
        TitlePattern: AppPlaceholder,
        IconPath: null,
        Validation: true,
        Logging: false,
        SampleSeedData: true,
        NamespacePrefix: string.Empty,
        Layout: null);
```

And the accessor, beside `HasBranding`:

```csharp
    /// <summary>
    /// The layout to emit: whatever was named, or <see cref="AppLayout.Tabs"/> when nothing was.
    /// </summary>
    /// <remarks>
    /// <see cref="Layout"/> is nullable because "not mentioned" and "tabs" are different answers
    /// to <c>BuildAppCommand.Merge</c>, which reads a setting as typed-inline when it differs from
    /// <see cref="Default"/>. The emit path never cares about that distinction, and uses this.
    /// </remarks>
    public AppLayout EffectiveLayout => Layout ?? AppLayout.Tabs;
```

- [ ] **Step 5: Run the tests to verify they pass, and the whole suite to prove neutrality**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~AppLayoutSettingTests"`
Expected: PASS, 4 tests.

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx`
Expected: PASS — the full suite (2000+ tests). A positional-argument break anywhere shows up here. **If any existing test fails, do not edit it**: the new parameter is in the wrong position or `Default` is not neutral.

- [ ] **Step 6: Commit**

```bash
cd D:/SmartCodeGenerator-Repository
git add AppScaffolding/Layouts/AppLayout.cs AppScaffolding/AppCustomization.cs CodeGenerator.Tests/App/AppLayoutSettingTests.cs
git commit -m "feat(scaffold): AppLayout setting on AppCustomization"
```

---

### Task 3: The layout vocabulary

The single source of the four keys and their descriptions, shared by the inline parser, the wizard prompt and the help catalogue.

**Files:**
- Create: `AppScaffolding\Layouts\AppLayoutCatalog.cs`
- Test: `CodeGenerator.Tests\App\AppLayoutCatalogTests.cs`

**Interfaces:**
- Consumes: `AppLayout` (Task 2)
- Produces: `AppLayoutCatalog.Keys` (`IReadOnlyList<string>`, display order, `tabs` first); `AppLayoutCatalog.KeyFor(AppLayout)`; `AppLayoutCatalog.TryResolve(string, out AppLayout)`; `AppLayoutCatalog.PromptOptions` (`"tabs/sidebar/master-detail/dashboard"`); `AppLayoutCatalog.Descriptions` (`IReadOnlyDictionary<AppLayout, string>`).

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests\App\AppLayoutCatalogTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class AppLayoutCatalogTests
{
    [TestMethod]
    public void Keys_ListEveryLayoutWithTabsFirst()
    {
        Assert.AreEqual("tabs", AppLayoutCatalog.Keys[0]);
        Assert.HasCount(Enum.GetValues<AppLayout>().Length, AppLayoutCatalog.Keys);
    }

    [TestMethod]
    public void KeyFor_HyphenatesMasterDetail()
    {
        // One token, never two words: InlineCustomization scans the line right to left by
        // whitespace, so "master detail" would be read as two separate tokens.
        Assert.AreEqual("master-detail", AppLayoutCatalog.KeyFor(AppLayout.MasterDetail));
        Assert.DoesNotContain(" ", AppLayoutCatalog.KeyFor(AppLayout.MasterDetail));
    }

    [TestMethod]
    public void TryResolve_ReadsEveryKeyCaseInsensitively()
    {
        foreach (var layout in Enum.GetValues<AppLayout>())
        {
            Assert.IsTrue(AppLayoutCatalog.TryResolve(AppLayoutCatalog.KeyFor(layout).ToUpperInvariant(), out var parsed));
            Assert.AreEqual(layout, parsed);
        }
    }

    [TestMethod]
    public void TryResolve_RefusesAnythingElse()
    {
        Assert.IsFalse(AppLayoutCatalog.TryResolve("kanban", out _));
        Assert.IsFalse(AppLayoutCatalog.TryResolve("", out _));
        Assert.IsFalse(AppLayoutCatalog.TryResolve("master detail", out _));
    }

    [TestMethod]
    public void EveryLayoutHasADescription()
    {
        foreach (var layout in Enum.GetValues<AppLayout>())
            Assert.IsNotEmpty(AppLayoutCatalog.Descriptions[layout]);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~AppLayoutCatalogTests"`
Expected: FAIL to build — `AppLayoutCatalog` does not exist.

- [ ] **Step 3: Write the catalogue**

Create `AppScaffolding\Layouts\AppLayoutCatalog.cs`:

```csharp
namespace AppScaffolding;

/// <summary>
/// The layout vocabulary: the CLI keys and their descriptions, in display order.
/// </summary>
/// <remarks>
/// One catalogue rather than one per target, because the WPF and Windows Forms targets ship the
/// same four layouts under the same four names — so the inline parser, the wizard prompt and the
/// help catalogue all read from here and no surface keeps its own list.
/// </remarks>
public static class AppLayoutCatalog
{
    private static readonly (AppLayout Layout, string Key, string Description)[] All =
    [
        (AppLayout.Tabs, "tabs", "a tab per entity, its grid filling the tab"),
        (AppLayout.Sidebar, "sidebar", "a list of entities on the left, the selected one's grid beside it"),
        // Hyphenated deliberately: the inline parser tokenizes on whitespace.
        (AppLayout.MasterDetail, "master-detail", "tabs, with an edit form for the selected row under the grid"),
        (AppLayout.Dashboard, "dashboard", "a home screen of per-entity cards showing row counts"),
    ];

    /// <summary>Layout keys in display order; the first is the default.</summary>
    public static IReadOnlyList<string> Keys { get; } = All.Select(x => x.Key).ToList();

    public static IReadOnlyDictionary<AppLayout, string> Descriptions { get; } =
        All.ToDictionary(x => x.Layout, x => x.Description);

    /// <summary>The prompt/help rendering of the vocabulary: "tabs/sidebar/master-detail/dashboard".</summary>
    public static string PromptOptions { get; } = string.Join("/", Keys);

    /// <summary>The pipe rendering the help catalogue uses: "tabs|sidebar|master-detail|dashboard".</summary>
    public static string HelpOptions { get; } = string.Join("|", Keys);

    public static string KeyFor(AppLayout layout) => All.First(x => x.Layout == layout).Key;

    /// <summary>Resolves a key (case-insensitively) to a layout.</summary>
    public static bool TryResolve(string key, out AppLayout layout)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                layout = candidate.Layout;
                return true;
            }
        }

        layout = AppLayout.Tabs;
        return false;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~AppLayoutCatalogTests"`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
cd D:/SmartCodeGenerator-Repository
git add AppScaffolding/Layouts/AppLayoutCatalog.cs CodeGenerator.Tests/App/AppLayoutCatalogTests.cs
git commit -m "feat(scaffold): the layout key vocabulary"
```

---

### Task 4: `IAppLayout`, `LayoutContext`, and the WPF `tabs` layout

The abstraction plus the one layout that must change nothing. `WpfTarget` is rewired to go through the registry, and the proof of neutrality is that `WpfTabsLayout` returns strings *equal to* today's template output.

**Files:**
- Create: `AppScaffolding\Layouts\IAppLayout.cs`, `AppScaffolding\Layouts\LayoutContext.cs`, `AppScaffolding\Layouts\Wpf\WpfTabsLayout.cs`, `AppScaffolding\Layouts\Wpf\WpfLayouts.cs`
- Modify: `AppScaffolding\Targets\WpfTarget.cs:17-41`
- Test: `CodeGenerator.Tests\App\WpfLayoutNeutralityTests.cs`

**Interfaces:**
- Consumes: `AppLayout` (Task 2), `MainWindowTemplate.Xaml(string, string, bool, string?)`, `MainWindowTemplate.CodeBehind(string)`, `ThemePalette.Resolve(AppTheme, string?)`, `GeneratedFile(string Folder, string Name, string Content)`
- Produces: `IAppLayout` with `Key`, `Description`, `ViewFiles(LayoutContext)`; `LayoutContext(string RootNamespace, string ProjectName, AppCustomization Customization, ThemePalette? Palette)`; `WpfLayouts.Resolve(AppLayout)` and `WpfLayouts.Default`. Tasks 6–8 add layouts to the same registry; Task 5 mirrors it for WinForms.

- [ ] **Step 1: Write the failing neutrality tests**

Create `CodeGenerator.Tests\App\WpfLayoutNeutralityTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WpfLayoutNeutralityTests
{
    private static LayoutContext Context(AppCustomization? customization = null)
    {
        var settings = customization ?? AppCustomization.Default;

        return new LayoutContext(
            RootNamespace: "Payroll",
            ProjectName: "Payroll",
            Customization: settings,
            Palette: ThemePalette.Resolve(settings.Theme, settings.Accent));
    }

    [TestMethod]
    public void TabsLayout_EmitsExactlyWhatTheTemplateEmitsToday()
    {
        var files = WpfLayouts.Resolve(AppLayout.Tabs).ViewFiles(Context());

        var xaml = files.Single(f => f.Name == "MainWindow.xaml");
        var codeBehind = files.Single(f => f.Name == "MainWindow.xaml.cs");

        // Equality, not Contains: this is the byte-neutrality contract. The tabs layout
        // delegates, it does not reimplement.
        Assert.AreEqual(
            MainWindowTemplate.Xaml("Payroll", "Payroll", themed: false, iconFileName: null),
            xaml.Content);
        Assert.AreEqual(MainWindowTemplate.CodeBehind("Payroll"), codeBehind.Content);
        Assert.AreEqual("Views", xaml.Folder);
    }

    [TestMethod]
    public void TabsLayout_PassesTheTitleThemeAndIconThrough()
    {
        var customization = AppCustomization.Default with
        {
            Company = "Codis Ltd",
            TitlePattern = AppCustomization.CompanyTitlePattern,
            Theme = AppTheme.Dark,
            IconPath = @"D:\brand\logo.ico",
        };

        var files = WpfLayouts.Resolve(AppLayout.Tabs).ViewFiles(Context(customization));
        var xaml = files.Single(f => f.Name == "MainWindow.xaml").Content;

        Assert.AreEqual(
            MainWindowTemplate.Xaml("Payroll", "Codis Ltd - Payroll", themed: true, iconFileName: "app.ico"),
            xaml);
    }

    [TestMethod]
    public void WpfTarget_DefaultsToTheTabsLayoutOutput()
    {
        var files = new WpfTarget().FrontEndFiles("Payroll", ["Employee"], AppCustomization.Default);

        Assert.AreEqual(
            MainWindowTemplate.Xaml("Payroll", "Payroll", themed: false, iconFileName: null),
            files.Single(f => f.Name == "MainWindow.xaml").Content);
    }

    [TestMethod]
    public void Registry_DefaultsToTabs()
    {
        Assert.AreEqual("tabs", WpfLayouts.Default.Key);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~WpfLayoutNeutralityTests"`
Expected: FAIL to build — `AppScaffolding.Layouts` does not exist.

- [ ] **Step 3: Write the abstraction**

Create `AppScaffolding\Layouts\LayoutContext.cs`:

```csharp
namespace AppScaffolding.Layouts;

/// <summary>
/// Everything a layout needs to emit its views.
/// </summary>
/// <remarks>
/// A record rather than four parameters because all four derive from the same two inputs — the
/// project name and the customization — and <see cref="Palette"/> in particular would otherwise be
/// re-resolved inside every layout. <see cref="RootNamespace"/> and <see cref="ProjectName"/> are
/// both present and are NOT interchangeable: emitted code declares the root namespace (which may
/// carry a company prefix), while the window title names the project.
/// </remarks>
public sealed record LayoutContext(
    string RootNamespace,
    string ProjectName,
    AppCustomization Customization,
    ThemePalette? Palette)
{
    /// <summary>Whether the layout should bind to the theme brushes.</summary>
    public bool Themed => Palette is not null;

    /// <summary>The window/form caption, XML-escaped.</summary>
    public string TitleAttribute => Customization.TitleAttributeFor(ProjectName);

    /// <summary>The icon file inside the assets folder, or null when the app was given none.</summary>
    public string? IconFileName => Customization.IconFileName;
}
```

Create `AppScaffolding\Layouts\IAppLayout.cs`:

```csharp
namespace AppScaffolding.Layouts;

/// <summary>
/// One shape of a generated desktop front end. A target holds a registry of these and asks the one
/// the customization named for its view files.
/// </summary>
/// <remarks>
/// Deliberately shaped like <see cref="ITargetScaffolder"/>: a stable lower-case
/// <see cref="Key"/>, a human <see cref="Description"/>, and one method producing files. The
/// shared view-model files are emitted by the target before this runs, and no layout may change
/// them — a layout is a view concern only.
/// </remarks>
public interface IAppLayout
{
    /// <summary>Stable lower-case key, matching <see cref="AppLayoutCatalog"/>.</summary>
    string Key { get; }

    /// <summary>Human phrase for the wizard prompt and the help catalogue.</summary>
    string Description { get; }

    /// <summary>The view files this layout emits, folder-relative to the project root.</summary>
    IReadOnlyList<GeneratedFile> ViewFiles(LayoutContext context);
}
```

- [ ] **Step 4: Write the tabs layout and the registry**

Create `AppScaffolding\Layouts\Wpf\WpfTabsLayout.cs`:

```csharp
namespace AppScaffolding.Layouts.Wpf;

/// <summary>
/// The original WPF shape: a TabControl over IEntityViewModel, one tab per entity.
/// </summary>
/// <remarks>
/// Reimplements NOTHING. Every byte comes from <see cref="MainWindowTemplate"/>, which is
/// untouched, because this layout's output is the output every pre-layout app has — and the
/// regression proof is equality against that template, not a substring assertion.
/// </remarks>
public sealed class WpfTabsLayout : IAppLayout
{
    public string Key => AppLayoutCatalog.KeyFor(AppLayout.Tabs);

    public string Description => AppLayoutCatalog.Descriptions[AppLayout.Tabs];

    public IReadOnlyList<GeneratedFile> ViewFiles(LayoutContext context) =>
    [
        new("Views", "MainWindow.xaml",
            MainWindowTemplate.Xaml(
                context.RootNamespace, context.TitleAttribute, context.Themed, context.IconFileName)),
        new("Views", "MainWindow.xaml.cs", MainWindowTemplate.CodeBehind(context.RootNamespace)),
    ];
}
```

Create `AppScaffolding\Layouts\Wpf\WpfLayouts.cs`:

```csharp
namespace AppScaffolding.Layouts.Wpf;

/// <summary>The WPF layouts, keyed by <see cref="AppLayout"/>.</summary>
/// <remarks>Shaped like <c>TargetScaffolders</c>: a private array, the first element as the
/// default, and a resolve that cannot fail for a valid enum value.</remarks>
public static class WpfLayouts
{
    private static readonly IAppLayout[] All =
    [
        new WpfTabsLayout(),
    ];

    /// <summary>The historical default: the tabbed window.</summary>
    public static IAppLayout Default { get; } = All[0];

    public static IReadOnlyList<IAppLayout> Keys { get; } = All;

    /// <summary>
    /// The layout for <paramref name="layout"/>, or <see cref="Default"/> while a layout is still
    /// unimplemented.
    /// </summary>
    /// <remarks>
    /// Falling back rather than throwing keeps a half-built registry from crashing a build during
    /// this feature's own implementation; by the end of it every enum value has an entry, and the
    /// registry-completeness test in <c>WpfLayoutRegistryTests</c> pins that.
    /// </remarks>
    public static IAppLayout Resolve(AppLayout layout)
    {
        var key = AppLayoutCatalog.KeyFor(layout);

        return All.FirstOrDefault(l => l.Key == key) ?? Default;
    }
}
```

- [ ] **Step 5: Rewire `WpfTarget`**

Replace the body of `FrontEndFiles(string, IReadOnlyList<string>, AppCustomization)` in `AppScaffolding\Targets\WpfTarget.cs` (currently lines 17-41). Keep the `using CodeGenerationEngine.Models;` at the top and add `using AppScaffolding.Layouts;` / `using AppScaffolding.Layouts.Wpf;`:

```csharp
    public IReadOnlyList<GeneratedFile> FrontEndFiles(
        string projectName, IReadOnlyList<string> entityNames, AppCustomization customization)
    {
        // The namespace the emitted code declares; projectName still names the app in its title.
        var ns = customization.RootNamespaceFor(projectName);

        // Shared with WinFormsTarget. The WPF variant of RelayCommand requeries through
        // CommandManager, which is what re-enables Delete when the grid selection changes.
        var files = DesktopViewModelFiles.Build(
            ns, entityNames, ViewModelTemplates.RelayCommand(ns),
            customization.Validation, customization.SampleSeedData);

        var palette = ThemePalette.Resolve(customization.Theme, customization.Accent);

        // Theming stays out here rather than inside the layouts: it is orthogonal to shape, and
        // four layouts would otherwise each reimplement the merge into App.xaml.
        files.AddRange(
            WpfLayouts.Resolve(customization.EffectiveLayout).ViewFiles(
                new LayoutContext(ns, projectName, customization, palette)));

        if (palette is not null)
            files.Add(new("Views", ThemeXamlTemplate.FileName, ThemeXamlTemplate.Source(palette)));

        return files;
    }
```

- [ ] **Step 6: Run the tests to verify they pass, then the full suite**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~WpfLayoutNeutralityTests"`
Expected: PASS, 4 tests.

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx`
Expected: PASS in full. Any WPF scaffolding test that fails here means the rewiring changed the file ORDER or content — `MainWindow.xaml` and its code-behind must still land after the view models and before `Theme.xaml`.

- [ ] **Step 7: Commit**

```bash
cd D:/SmartCodeGenerator-Repository
git add AppScaffolding/Layouts AppScaffolding/Targets/WpfTarget.cs CodeGenerator.Tests/App/WpfLayoutNeutralityTests.cs
git commit -m "refactor(scaffold): WPF front end goes through a layout registry"
```

---

### Task 5: The WinForms `tabs` layout and the target capability check

**Files:**
- Create: `AppScaffolding\Layouts\WinForms\WinFormsTabsLayout.cs`, `AppScaffolding\Layouts\WinForms\WinFormsLayouts.cs`, `AppScaffolding\Layouts\AppLayouts.cs`
- Modify: `AppScaffolding\Targets\WinFormsTarget.cs` (its `FrontEndFiles` overload)
- Test: `CodeGenerator.Tests\App\WinFormsLayoutNeutralityTests.cs`

**Interfaces:**
- Consumes: `IAppLayout`, `LayoutContext` (Task 4), `WinFormsMainFormTemplate.Source(string, string, ThemePalette?, string?)`
- Produces: `WinFormsLayouts.Resolve(AppLayout)`, `WinFormsLayouts.Default`, `AppLayouts.Supports(string targetKey)`. Task 13 calls `AppLayouts.Supports` from the wizard; Task 14 calls it at `approve`.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests\App\WinFormsLayoutNeutralityTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.WinForms;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WinFormsLayoutNeutralityTests
{
    private static LayoutContext Context(AppCustomization? customization = null)
    {
        var settings = customization ?? AppCustomization.Default;

        return new LayoutContext(
            "Payroll.WinForms", "Payroll.WinForms", settings,
            ThemePalette.Resolve(settings.Theme, settings.Accent));
    }

    [TestMethod]
    public void TabsLayout_EmitsExactlyWhatTheTemplateEmitsToday()
    {
        var files = WinFormsLayouts.Resolve(AppLayout.Tabs).ViewFiles(Context());
        var form = files.Single(f => f.Name == "MainForm.cs");

        Assert.AreEqual(
            WinFormsMainFormTemplate.Source("Payroll.WinForms", "Payroll.WinForms", palette: null, iconFileName: null),
            form.Content);
        Assert.AreEqual("Views", form.Folder);
    }

    [TestMethod]
    public void WinFormsTarget_DefaultsToTheTabsLayoutOutput()
    {
        var files = new WinFormsTarget().FrontEndFiles(
            "Payroll.WinForms", ["Employee"], AppCustomization.Default);

        Assert.AreEqual(
            WinFormsMainFormTemplate.Source("Payroll.WinForms", "Payroll.WinForms", palette: null, iconFileName: null),
            files.Single(f => f.Name == "MainForm.cs").Content);
    }

    [TestMethod]
    public void Supports_IsTrueForTheTwoDesktopTargetsOnly()
    {
        Assert.IsTrue(AppLayouts.Supports("wpf"));
        Assert.IsTrue(AppLayouts.Supports("winforms"));
        Assert.IsTrue(AppLayouts.Supports("WPF"));

        Assert.IsFalse(AppLayouts.Supports("console"));
        Assert.IsFalse(AppLayouts.Supports("webapi"));
        Assert.IsFalse(AppLayouts.Supports("worker"));
        Assert.IsFalse(AppLayouts.Supports("classlib"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~WinFormsLayoutNeutralityTests"`
Expected: FAIL to build — `WinFormsLayouts` and `AppLayouts` do not exist.

- [ ] **Step 3: Write the WinForms tabs layout and registry**

Create `AppScaffolding\Layouts\WinForms\WinFormsTabsLayout.cs`:

```csharp
namespace AppScaffolding.Layouts.WinForms;

/// <summary>The original WinForms shape: a TabControl over IEntityViewModel.</summary>
/// <remarks>Delegates entirely to <see cref="WinFormsMainFormTemplate"/>, for the reason
/// <see cref="Wpf.WpfTabsLayout"/> gives: this layout's output is what every pre-layout app
/// already has.</remarks>
public sealed class WinFormsTabsLayout : IAppLayout
{
    public string Key => AppLayoutCatalog.KeyFor(AppLayout.Tabs);

    public string Description => AppLayoutCatalog.Descriptions[AppLayout.Tabs];

    public IReadOnlyList<GeneratedFile> ViewFiles(LayoutContext context) =>
    [
        new("Views", "MainForm.cs",
            WinFormsMainFormTemplate.Source(
                context.RootNamespace, context.TitleAttribute, context.Palette, context.IconFileName)),
    ];
}
```

Create `AppScaffolding\Layouts\WinForms\WinFormsLayouts.cs`:

```csharp
namespace AppScaffolding.Layouts.WinForms;

/// <summary>The Windows Forms layouts, keyed by <see cref="AppLayout"/>.</summary>
public static class WinFormsLayouts
{
    private static readonly IAppLayout[] All =
    [
        new WinFormsTabsLayout(),
    ];

    public static IAppLayout Default { get; } = All[0];

    public static IReadOnlyList<IAppLayout> Keys { get; } = All;

    /// <summary>The layout for <paramref name="layout"/>, or <see cref="Default"/> while one is
    /// still unimplemented — see the note on <c>WpfLayouts.Resolve</c>.</summary>
    public static IAppLayout Resolve(AppLayout layout)
    {
        var key = AppLayoutCatalog.KeyFor(layout);

        return All.FirstOrDefault(l => l.Key == key) ?? Default;
    }
}
```

Create `AppScaffolding\Layouts\AppLayouts.cs`:

```csharp
namespace AppScaffolding.Layouts;

/// <summary>Which output targets have a window a layout can shape.</summary>
/// <remarks>
/// Lives here rather than in the CLI so the wizard and the `approve` check read one list. A key
/// list rather than a member on <see cref="ITargetScaffolder"/>, following the
/// <c>BuildAppCommand.JsonOnlyTargets</c> precedent — and deliberately its own list even though it
/// currently holds the same two keys, because "reads JSON only" and "has a window" are different
/// facts that could diverge.
/// </remarks>
public static class AppLayouts
{
    private static readonly string[] LayoutTargets = ["wpf", "winforms"];

    public static bool Supports(string targetKey)
        => LayoutTargets.Contains(targetKey, StringComparer.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Rewire `WinFormsTarget`**

Read `AppScaffolding\Targets\WinFormsTarget.cs` first, then replace the `MainForm.cs` line in its `FrontEndFiles(string, IReadOnlyList<string>, AppCustomization)` overload with the registry call, keeping everything else (view models, palette resolution) exactly as it is:

```csharp
        files.AddRange(
            WinFormsLayouts.Resolve(customization.EffectiveLayout).ViewFiles(
                new LayoutContext(ns, projectName, customization, palette)));
```

Add `using AppScaffolding.Layouts;` and `using AppScaffolding.Layouts.WinForms;`.

- [ ] **Step 5: Run the tests to verify they pass, then the full suite**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~WinFormsLayoutNeutralityTests"`
Expected: PASS, 3 tests.

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx`
Expected: PASS in full.

- [ ] **Step 6: Commit**

```bash
cd D:/SmartCodeGenerator-Repository
git add AppScaffolding/Layouts AppScaffolding/Targets/WinFormsTarget.cs CodeGenerator.Tests/App/WinFormsLayoutNeutralityTests.cs
git commit -m "refactor(scaffold): WinForms front end goes through a layout registry"
```

---

### Task 6: The shared WPF entity pane, and the `sidebar` layout

The first layout with new output. The per-entity pane (grid + button row + status bar) becomes a keyed `DataTemplate` in `Window.Resources`, which every new layout references as `{StaticResource EntityPane}`. That fixes one nesting depth for the fragment, so no layout has to re-indent it.

**The `tabs` layout keeps its own inline copy of that markup and must NOT be refactored onto the fragment** — recomposing the one file that owes byte-identity is exactly the failure PR #78 recorded. The pane markup therefore exists twice on purpose.

**Files:**
- Create: `AppScaffolding\Layouts\Wpf\EntityPaneXaml.cs`, `AppScaffolding\Layouts\Wpf\WpfSidebarLayout.cs`
- Modify: `AppScaffolding\Layouts\Wpf\WpfLayouts.cs` (register it)
- Test: `CodeGenerator.Tests\App\WpfSidebarLayoutTests.cs`

**Interfaces:**
- Consumes: `IAppLayout`, `LayoutContext`, `MainWindowTemplate.CodeBehind(string)`
- Produces: `EntityPaneXaml.Resource(ThemePalette? palette, bool withEditor)` returning the `<Window.Resources>` block with the `EntityPane` `DataTemplate`; `WpfSidebarLayout`. Tasks 7 and 8 both call `EntityPaneXaml.Resource`.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests\App\WpfSidebarLayoutTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.Wpf;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WpfSidebarLayoutTests
{
    private static string Xaml(AppCustomization? customization = null)
    {
        var settings = customization ?? AppCustomization.Default;

        var files = WpfLayouts.Resolve(AppLayout.Sidebar).ViewFiles(
            new LayoutContext("Payroll", "Payroll", settings,
                ThemePalette.Resolve(settings.Theme, settings.Accent)));

        return files.Single(f => f.Name == "MainWindow.xaml").Content;
    }

    [TestMethod]
    public void ListsTheEntitiesBesideTheSelectedOnesPane()
    {
        var xaml = Xaml();

        StringAssert.Contains(xaml, "x:Class=\"Payroll.Views.MainWindow\"");
        StringAssert.Contains(xaml, "<ListBox x:Name=\"EntityList\"");
        StringAssert.Contains(xaml, "ItemsSource=\"{Binding Entities}\"");
        StringAssert.Contains(xaml, "DisplayMemberPath=\"Title\"");
        StringAssert.Contains(xaml, "Content=\"{Binding SelectedItem, ElementName=EntityList}\"");
        StringAssert.Contains(xaml, "ContentTemplate=\"{StaticResource EntityPane}\"");
    }

    [TestMethod]
    public void SelectsTheFirstEntityOnStartup()
    {
        // Same reason the TabControl needs it: a Selector bound to a plain ObservableCollection
        // rather than a CollectionView opens with nothing selected.
        StringAssert.Contains(Xaml(), "SelectedIndex=\"0\"");
    }

    [TestMethod]
    public void CarriesTheWholePaneTheTabbedLayoutHas()
    {
        var xaml = Xaml();

        StringAssert.Contains(xaml, "<DataTemplate x:Key=\"EntityPane\">");
        StringAssert.Contains(xaml, "ItemsSource=\"{Binding Items}\"");
        StringAssert.Contains(xaml, "SelectedItem=\"{Binding Selected, Mode=TwoWay}\"");
        StringAssert.Contains(xaml, "AutoGenerateColumns=\"True\"");
        StringAssert.Contains(xaml, "CanUserAddRows=\"False\"");
        StringAssert.Contains(xaml, "AutoGeneratingColumn=\"OnAutoGeneratingColumn\"");
        StringAssert.Contains(xaml, "Command=\"{Binding AddCommand}\"");
        StringAssert.Contains(xaml, "Command=\"{Binding DeleteCommand}\"");
        StringAssert.Contains(xaml, "Command=\"{Binding SaveCommand}\"");
        StringAssert.Contains(xaml, "Text=\"{Binding Status}\"");
    }

    [TestMethod]
    public void ReusesTodaysCodeBehindUnchanged()
    {
        // The pane's DataGrid still raises AutoGeneratingColumn, so the handler — and the FK
        // combo logic with it — is needed verbatim. No second copy to keep in sync.
        var files = WpfLayouts.Resolve(AppLayout.Sidebar).ViewFiles(
            new LayoutContext("Payroll", "Payroll", AppCustomization.Default, null));

        Assert.AreEqual(
            MainWindowTemplate.CodeBehind("Payroll"),
            files.Single(f => f.Name == "MainWindow.xaml.cs").Content);
    }

    [TestMethod]
    public void PaintsWithTheThemeBrushesOnlyWhenThemed()
    {
        var themed = Xaml(AppCustomization.Default with { Theme = AppTheme.Dark });
        var plain = Xaml();

        StringAssert.Contains(themed, "{DynamicResource AppBackgroundBrush}");
        StringAssert.Contains(themed, "{DynamicResource AppSurfaceBrush}");
        Assert.DoesNotContain("DynamicResource", plain);
    }

    [TestMethod]
    public void ShowsTheWindowIconAndTitleItWasGiven()
    {
        var xaml = Xaml(AppCustomization.Default with
        {
            Company = "Codis Ltd",
            TitlePattern = AppCustomization.CompanyTitlePattern,
            IconPath = @"D:\brand\logo.ico",
        });

        StringAssert.Contains(xaml, "Title=\"Codis Ltd - Payroll\"");
        StringAssert.Contains(xaml, "Icon=\"Assets/app.ico\"");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~WpfSidebarLayoutTests"`
Expected: FAIL — `WpfLayouts.Resolve(AppLayout.Sidebar)` returns the tabs layout, so `x:Name="EntityList"` is absent.

- [ ] **Step 3: Write the shared pane fragment**

Create `AppScaffolding\Layouts\Wpf\EntityPaneXaml.cs`:

```csharp
namespace AppScaffolding.Layouts.Wpf;

/// <summary>
/// The per-entity pane — grid, button row, status bar — as a keyed <c>DataTemplate</c> in
/// <c>Window.Resources</c>.
/// </summary>
/// <remarks>
/// Shared by the sidebar, master-detail and dashboard layouts, which reference it as
/// <c>{StaticResource EntityPane}</c>. A keyed resource rather than markup inlined into each
/// window means one fixed nesting depth, so no layout has to re-indent the fragment.
/// <para>
/// The tabbed layout does NOT use this and must not be refactored onto it: its markup is the
/// byte-identical output every pre-layout app has, and recomposing it from fragments is precisely
/// how a CRLF regression enters invisibly. The duplication is deliberate — change both sites.
/// </para>
/// </remarks>
public static class EntityPaneXaml
{
    /// <summary>The resource key layouts bind to.</summary>
    public const string Key = "EntityPane";

    /// <summary>
    /// The <c>Window.Resources</c> block declaring the pane. With <paramref name="withEditor"/> the
    /// pane carries a <c>PropertyEditor</c> for the selected row between the grid and the buttons.
    /// </summary>
    public static string Resource(ThemePalette? palette, bool withEditor = false)
    {
        var gridBrushes = palette is null
            ? string.Empty
            : """

                              Background="{DynamicResource AppSurfaceBrush}"
                              Foreground="{DynamicResource AppForegroundBrush}"
                              HorizontalGridLinesBrush="{DynamicResource AppGridLineBrush}"
                              VerticalGridLinesBrush="{DynamicResource AppGridLineBrush}"
              """;

        var editor = withEditor
            ? """

                          <local:PropertyEditor DockPanel.Dock="Bottom"
                                                Margin="8"
                                                Content="{Binding Selected}" />
              """
            : string.Empty;

        return $$"""
    <Window.Resources>
        <DataTemplate x:Key="{{Key}}">
            <DockPanel>
                <StatusBar DockPanel.Dock="Bottom">
                    <TextBlock Text="{Binding Status}" />
                </StatusBar>
                <StackPanel DockPanel.Dock="Bottom"
                            Orientation="Horizontal"
                            HorizontalAlignment="Right"
                            Margin="8">
                    <Button Content="Add" Command="{Binding AddCommand}"
                            Width="80" Margin="4,0" />
                    <Button Content="Delete" Command="{Binding DeleteCommand}"
                            Width="80" Margin="4,0" />
                    <Button Content="Save" Command="{Binding SaveCommand}"
                            Width="80" Margin="4,0" />
                </StackPanel>{{editor}}
                <DataGrid ItemsSource="{Binding Items}"{{gridBrushes}}
                          SelectedItem="{Binding Selected, Mode=TwoWay}"
                          AutoGenerateColumns="True"
                          CanUserAddRows="False"
                          AutoGeneratingColumn="OnAutoGeneratingColumn" />
            </DockPanel>
        </DataTemplate>
    </Window.Resources>
""";
    }
}
```

- [ ] **Step 4: Write the sidebar layout**

Create `AppScaffolding\Layouts\Wpf\WpfSidebarLayout.cs`:

```csharp
namespace AppScaffolding.Layouts.Wpf;

/// <summary>
/// A navigation list of entities on the left, the selected entity's pane on the right.
/// </summary>
/// <remarks>
/// Needs no view-model change and no new code-behind: the pane follows the ListBox through
/// <c>ElementName</c> binding, so <see cref="MainWindowTemplate.CodeBehind"/> is emitted verbatim
/// — its AutoGeneratingColumn handler is still what locks the Id column and builds the foreign-key
/// combo boxes.
/// </remarks>
public sealed class WpfSidebarLayout : IAppLayout
{
    public string Key => AppLayoutCatalog.KeyFor(AppLayout.Sidebar);

    public string Description => AppLayoutCatalog.Descriptions[AppLayout.Sidebar];

    public IReadOnlyList<GeneratedFile> ViewFiles(LayoutContext context) =>
    [
        new("Views", "MainWindow.xaml", Xaml(context)),
        new("Views", "MainWindow.xaml.cs", MainWindowTemplate.CodeBehind(context.RootNamespace)),
    ];

    private static string Xaml(LayoutContext context)
    {
        var windowIcon = context.IconFileName is null
            ? string.Empty
            : $"\n        Icon=\"{AppCustomization.AssetsFolder}/{context.IconFileName}\"";

        var windowBrushes = context.Themed
            ? "\n        Background=\"{DynamicResource AppBackgroundBrush}\""
              + "\n        Foreground=\"{DynamicResource AppForegroundBrush}\""
            : string.Empty;

        var listBrushes = context.Themed
            ? "\n                 Background=\"{DynamicResource AppSurfaceBrush}\""
              + "\n                 Foreground=\"{DynamicResource AppForegroundBrush}\""
            : string.Empty;

        return $$"""
<Window x:Class="{{context.RootNamespace}}.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="{{context.TitleAttribute}}" Height="600" Width="900"{{windowIcon}}{{windowBrushes}}>
{{EntityPaneXaml.Resource(context.Palette)}}
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="180" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <!-- SelectedIndex is explicit for the reason the tabbed layout gives: a Selector only
             follows its collection's current item when the ItemsSource is a CollectionView, so
             bound to an ObservableCollection it would open with nothing selected. -->
        <ListBox x:Name="EntityList"
                 Grid.Column="0"
                 ItemsSource="{Binding Entities}"
                 DisplayMemberPath="Title"
                 SelectedIndex="0"{{listBrushes}} />
        <ContentControl Grid.Column="1"
                        Content="{Binding SelectedItem, ElementName=EntityList}"
                        ContentTemplate="{StaticResource EntityPane}" />
    </Grid>
</Window>
""";
    }
}
```

- [ ] **Step 5: Register it**

In `AppScaffolding\Layouts\Wpf\WpfLayouts.cs`, extend `All`:

```csharp
    private static readonly IAppLayout[] All =
    [
        new WpfTabsLayout(),
        new WpfSidebarLayout(),
    ];
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~WpfSidebarLayoutTests"`
Expected: PASS, 6 tests.

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~WpfLayoutNeutralityTests"`
Expected: PASS — registering a second layout must not have moved the default.

- [ ] **Step 7: Commit**

```bash
cd D:/SmartCodeGenerator-Repository
git add AppScaffolding/Layouts/Wpf CodeGenerator.Tests/App/WpfSidebarLayoutTests.cs
git commit -m "feat(scaffold): WPF sidebar layout"
```

---

### Task 7: The WPF `dashboard` layout

**Files:**
- Create: `AppScaffolding\Layouts\Wpf\WpfDashboardLayout.cs`
- Modify: `AppScaffolding\Layouts\Wpf\WpfLayouts.cs`
- Test: `CodeGenerator.Tests\App\WpfDashboardLayoutTests.cs`

**Interfaces:**
- Consumes: `EntityPaneXaml.Resource`, `MainWindowTemplate.CodeBehind(string)`
- Produces: `WpfDashboardLayout`. Nothing later depends on it.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests\App\WpfDashboardLayoutTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.Wpf;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WpfDashboardLayoutTests
{
    private static IReadOnlyList<GeneratedFile> Files(AppCustomization? customization = null)
    {
        var settings = customization ?? AppCustomization.Default;

        return WpfLayouts.Resolve(AppLayout.Dashboard).ViewFiles(
            new LayoutContext("Payroll", "Payroll", settings,
                ThemePalette.Resolve(settings.Theme, settings.Accent)));
    }

    private static string Xaml(AppCustomization? customization = null)
        => Files(customization).Single(f => f.Name == "MainWindow.xaml").Content;

    private static string CodeBehind()
        => Files().Single(f => f.Name == "MainWindow.xaml.cs").Content;

    [TestMethod]
    public void ShowsOneCardPerEntityWithItsRowCount()
    {
        var xaml = Xaml();

        StringAssert.Contains(xaml, "<ListBox x:Name=\"EntityCards\"");
        StringAssert.Contains(xaml, "ItemsSource=\"{Binding Entities}\"");
        StringAssert.Contains(xaml, "<WrapPanel />");
        StringAssert.Contains(xaml, "Text=\"{Binding Title}\"");

        // The count comes free: Items returns an ObservableCollection<T>, which raises
        // PropertyChanged("Count") on every mutation, and WPF binds against the runtime type.
        StringAssert.Contains(xaml, "Text=\"{Binding Items.Count}\"");
    }

    [TestMethod]
    public void OpensOnTheCardsRatherThanAnEntity()
    {
        StringAssert.Contains(Xaml(), "SelectedIndex=\"-1\"");
    }

    [TestMethod]
    public void ShowsTheSelectedEntitysPaneThroughTheSharedTemplate()
    {
        var xaml = Xaml();

        StringAssert.Contains(xaml, "<ContentControl x:Name=\"PaneHost\"");
        StringAssert.Contains(xaml, "Content=\"{Binding SelectedItem, ElementName=EntityCards}\"");
        StringAssert.Contains(xaml, "ContentTemplate=\"{StaticResource EntityPane}\"");
    }

    [TestMethod]
    public void CodeBehindNavigatesBothWaysAndStillHandlesColumns()
    {
        var source = CodeBehind();

        StringAssert.Contains(source, "private void OnCardSelected(");
        StringAssert.Contains(source, "private void OnHome(");
        StringAssert.Contains(source, "EntityCards.SelectedIndex = -1;");

        // Today's handler is still there: the pane's DataGrid raises AutoGeneratingColumn.
        StringAssert.Contains(source, "OnAutoGeneratingColumn");
        StringAssert.Contains(source, "DataGridComboBoxColumn");
    }

    [TestMethod]
    public void CodeBehindCompiles()
    {
        EmittedDesktopSource.AssertCompiles(CodeBehind());
    }

    [TestMethod]
    public void CodeBehindLeaksNoGeneratorImplementationDetails()
    {
        var source = CodeBehind();

        Assert.DoesNotContain("MainWindowTemplate", source);
        Assert.DoesNotContain("WpfDashboardLayout", source);
    }
}
```

> **Note on `CodeBehindCompiles`:** the emitted code-behind declares `partial class MainWindow : Window` and refers to `EntityCards`, `PaneHost` and `HomeButton`, which the XAML generates. Roslyn alone has no `InitializeComponent` and no named fields, so the test must supply them. Do this by compiling the code-behind together with a small stub in the same compilation — see step 4's helper.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~WpfDashboardLayoutTests"`
Expected: FAIL — the dashboard resolves to the tabs layout today.

- [ ] **Step 3: Write the layout**

Create `AppScaffolding\Layouts\Wpf\WpfDashboardLayout.cs`:

```csharp
namespace AppScaffolding.Layouts.Wpf;

/// <summary>
/// A home screen of per-entity cards showing row counts, clicking through to that entity's pane.
/// </summary>
/// <remarks>
/// The row count needs nothing added to the view models: <c>IEntityViewModel.Items</c> returns the
/// <c>ObservableCollection&lt;T&gt;</c> behind it, which raises <c>PropertyChanged("Count")</c> on
/// every mutation, and a WPF binding reflects over the runtime type. Navigation is view-local too
/// — swapping two Visibility values in the code-behind — so <c>MainViewModel</c> still exposes only
/// <c>Entities</c>.
/// </remarks>
public sealed class WpfDashboardLayout : IAppLayout
{
    public string Key => AppLayoutCatalog.KeyFor(AppLayout.Dashboard);

    public string Description => AppLayoutCatalog.Descriptions[AppLayout.Dashboard];

    public IReadOnlyList<GeneratedFile> ViewFiles(LayoutContext context) =>
    [
        new("Views", "MainWindow.xaml", Xaml(context)),
        new("Views", "MainWindow.xaml.cs", CodeBehind(context.RootNamespace)),
    ];

    private static string Xaml(LayoutContext context)
    {
        var windowIcon = context.IconFileName is null
            ? string.Empty
            : $"\n        Icon=\"{AppCustomization.AssetsFolder}/{context.IconFileName}\"";

        var windowBrushes = context.Themed
            ? "\n        Background=\"{DynamicResource AppBackgroundBrush}\""
              + "\n        Foreground=\"{DynamicResource AppForegroundBrush}\""
            : string.Empty;

        var cardBrush = context.Themed
            ? " Background=\"{DynamicResource AppSurfaceBrush}\""
            : string.Empty;

        return $$"""
<Window x:Class="{{context.RootNamespace}}.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="{{context.TitleAttribute}}" Height="600" Width="900"{{windowIcon}}{{windowBrushes}}>
{{EntityPaneXaml.Resource(context.Palette)}}
    <DockPanel>
        <Button x:Name="HomeButton"
                DockPanel.Dock="Top"
                Content="Home"
                Width="80"
                Margin="8"
                HorizontalAlignment="Left"
                Visibility="Collapsed"
                Click="OnHome" />
        <Grid>
            <!-- SelectedIndex is -1 on purpose: the app opens on the cards, and the pane
                 appears only once one is clicked. -->
            <ListBox x:Name="EntityCards"
                     ItemsSource="{Binding Entities}"
                     SelectedIndex="-1"
                     BorderThickness="0"
                     Background="Transparent"
                     ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                     SelectionChanged="OnCardSelected">
                <ListBox.ItemsPanel>
                    <ItemsPanelTemplate>
                        <WrapPanel />
                    </ItemsPanelTemplate>
                </ListBox.ItemsPanel>
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <Border Width="160" Height="90" Margin="8" CornerRadius="4"{{cardBrush}}>
                            <StackPanel VerticalAlignment="Center">
                                <TextBlock Text="{Binding Title}"
                                           FontSize="15"
                                           HorizontalAlignment="Center" />
                                <TextBlock Text="{Binding Items.Count}"
                                           FontSize="28"
                                           HorizontalAlignment="Center" />
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
            <ContentControl x:Name="PaneHost"
                            Visibility="Collapsed"
                            Content="{Binding SelectedItem, ElementName=EntityCards}"
                            ContentTemplate="{StaticResource EntityPane}" />
        </Grid>
    </DockPanel>
</Window>
""";
    }

    /// <summary>
    /// Today's code-behind plus the two navigation handlers.
    /// </summary>
    /// <remarks>
    /// Built by inserting into <see cref="MainWindowTemplate.CodeBehind"/> rather than restating
    /// it: the AutoGeneratingColumn handler, the foreign-key combo logic and the display
    /// convention are long, and a copy here would be a second thing to keep in sync.
    /// </remarks>
    private static string CodeBehind(string rootNamespace)
    {
        var source = MainWindowTemplate.CodeBehind(rootNamespace);

        // Raw literal, so the inserted lines carry this file's own line endings — the same ones
        // the template's literal carries. Rebuilt with "\n" they would silently lose a CR.
        var handlers = """
            // Navigation is a view concern: the view models know nothing about which screen is
            // showing, so the dashboard swaps two Visibility values here.
            private void OnCardSelected(object sender, SelectionChangedEventArgs e)
            {
                // Clearing the selection also raises this event; the Home handler has already
                // put the cards back, so there is nothing to do.
                if (EntityCards.SelectedItem is null)
                    return;

                EntityCards.Visibility = Visibility.Collapsed;
                PaneHost.Visibility = Visibility.Visible;
                HomeButton.Visibility = Visibility.Visible;
            }

            private void OnHome(object sender, RoutedEventArgs e)
            {
                EntityCards.SelectedIndex = -1;
                PaneHost.Visibility = Visibility.Collapsed;
                HomeButton.Visibility = Visibility.Collapsed;
                EntityCards.Visibility = Visibility.Visible;
            }

            """;

        // Inserted before the AutoGeneratingColumn handler's comment, which is the first member
        // after the constructor.
        const string anchor = "    // Column presentation is a view concern";

        return source.Replace(anchor, handlers + anchor);
    }
}
```

- [ ] **Step 4: Add the compile stub the code-behind needs**

The emitted code-behind is one half of a partial class; XAML generates the other half. Add this to `CodeGenerator.Tests\App\WpfDashboardLayoutTests.cs` and use it in `CodeBehindCompiles`:

```csharp
    /// <summary>
    /// The half of the partial class the XAML compiler generates: InitializeComponent and a field
    /// per x:Name. Without it the code-behind cannot compile on its own — which is a fact about
    /// WPF, not a defect in the emitted source.
    /// </summary>
    private const string XamlPartialStub = """
        namespace Payroll.Views;

        public partial class MainWindow
        {
            internal System.Windows.Controls.ListBox EntityCards = new();
            internal System.Windows.Controls.ContentControl PaneHost = new();
            internal System.Windows.Controls.Button HomeButton = new();

            private void InitializeComponent() { }
        }
        """;
```

and rewrite that test as:

```csharp
    [TestMethod]
    public void CodeBehindCompiles()
    {
        // The ViewModels namespace the code-behind imports is emitted by DesktopViewModelFiles in
        // the real app; here a MainViewModel stub stands in, as the other emitted-source tests do.
        var viewModelStub = """
            using System.Collections.ObjectModel;

            namespace Payroll.ViewModels;

            public sealed class MainViewModel
            {
                public ObservableCollection<IEntityViewModel> Entities { get; } = new();
            }

            public interface IEntityViewModel
            {
                string Title { get; }
                System.Collections.IEnumerable Items { get; }
            }
            """;

        EmittedDesktopSource.AssertCompiles(
            EmittedSource.Flatten(CodeBehind(), XamlPartialStub, viewModelStub));
    }
```

> `EmittedSource.Flatten` strips each fragment's file-scoped namespace and hoists the usings, which is required because `Compiler.Compile` parses one syntax tree and a compilation unit may declare only one file-scoped namespace. Note that flattening puts all three fragments in the global namespace, so the code-behind's `using Payroll.ViewModels;` is dropped by the same pass — that is why the stub declares the view-model types too.

- [ ] **Step 5: Register the layout**

In `WpfLayouts.All`, append `new WpfDashboardLayout(),`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~WpfDashboardLayoutTests"`
Expected: PASS, 6 tests.

- [ ] **Step 7: Commit**

```bash
cd D:/SmartCodeGenerator-Repository
git add AppScaffolding/Layouts/Wpf CodeGenerator.Tests/App/WpfDashboardLayoutTests.cs
git commit -m "feat(scaffold): WPF dashboard layout"
```

---

### Task 8: `PropertyEditor` and the WPF `master-detail` layout

The only genuinely new emitted type. WPF has no `PropertyGrid`, so the detail pane is an emitted `ContentControl` subclass that rebuilds a label/editor grid when its `Content` changes. It is used declaratively from inside the pane `DataTemplate`, which is why it is a control and not code-behind: named elements inside a `DataTemplate` are per-instance and reachable only through the visual tree.

**Files:**
- Create: `AppScaffolding\PropertyEditorTemplate.cs`, `AppScaffolding\Layouts\Wpf\WpfMasterDetailLayout.cs`
- Modify: `AppScaffolding\Layouts\Wpf\WpfLayouts.cs`; `AppScaffolding\MainWindowTemplate.cs:86-90` and `AppScaffolding\WinFormsMainFormTemplate.cs:10-14` (the "keep the two in sync" remarks — now three sites)
- Test: `CodeGenerator.Tests\App\WpfMasterDetailLayoutTests.cs`

**Interfaces:**
- Consumes: `EntityPaneXaml.Resource(ThemePalette?, bool withEditor)`, `MainWindowTemplate.CodeBehind(string)`
- Produces: `PropertyEditorTemplate.FileName` (`"PropertyEditor.cs"`), `PropertyEditorTemplate.Source(string rootNamespace)`, `WpfMasterDetailLayout`.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests\App\WpfMasterDetailLayoutTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.Wpf;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WpfMasterDetailLayoutTests
{
    private static IReadOnlyList<GeneratedFile> Files(AppCustomization? customization = null)
    {
        var settings = customization ?? AppCustomization.Default;

        return WpfLayouts.Resolve(AppLayout.MasterDetail).ViewFiles(
            new LayoutContext("Payroll", "Payroll", settings,
                ThemePalette.Resolve(settings.Theme, settings.Accent)));
    }

    [TestMethod]
    public void KeepsTheTabChromeAndAddsAnEditorUnderTheGrid()
    {
        var xaml = Files().Single(f => f.Name == "MainWindow.xaml").Content;

        StringAssert.Contains(xaml, "<TabControl ItemsSource=\"{Binding Entities}\"");
        StringAssert.Contains(xaml, "DisplayMemberPath=\"Title\"");
        StringAssert.Contains(xaml, "SelectedIndex=\"0\"");
        StringAssert.Contains(xaml, "ContentTemplate=\"{StaticResource EntityPane}\"");
        StringAssert.Contains(xaml, "<local:PropertyEditor DockPanel.Dock=\"Bottom\"");
        StringAssert.Contains(xaml, "Content=\"{Binding Selected}\"");

        // The editor is a type in the app's own Views namespace, so the window needs the xmlns.
        StringAssert.Contains(xaml, "xmlns:local=\"clr-namespace:Payroll.Views\"");
    }

    [TestMethod]
    public void EmitsThePropertyEditorAlongsideTheWindow()
    {
        var names = Files().Select(f => f.Name).ToList();

        Assert.Contains("MainWindow.xaml", names);
        Assert.Contains("MainWindow.xaml.cs", names);
        Assert.Contains("PropertyEditor.cs", names);
        Assert.AreEqual("Views", Files().Single(f => f.Name == "PropertyEditor.cs").Folder);
    }

    [TestMethod]
    public void PropertyEditorCompiles()
    {
        EmittedDesktopSource.AssertCompiles(PropertyEditorTemplate.Source("Payroll"));
    }

    [TestMethod]
    public void PropertyEditorEditsWritableScalarsAndSkipsTheId()
    {
        var source = PropertyEditorTemplate.Source("Payroll");

        StringAssert.Contains(source, "namespace Payroll.Views");
        StringAssert.Contains(source, ": ContentControl");
        StringAssert.Contains(source, "OnContentChanged");
        StringAssert.Contains(source, "IsDisplayable");
        StringAssert.Contains(source, "BindingMode.TwoWay");

        // Id is generated by the store, so it is shown read-only rather than offered as an editor
        // — the same rule the grid's column handler applies.
        StringAssert.Contains(source, "\"Id\"");
    }

    [TestMethod]
    public void PropertyEditorLeaksNoGeneratorImplementationDetails()
    {
        var source = PropertyEditorTemplate.Source("Payroll");

        Assert.DoesNotContain("PropertyEditorTemplate", source);
        Assert.DoesNotContain("MainWindowTemplate", source);
        Assert.DoesNotContain("WinFormsMainFormTemplate", source);
    }

    [TestMethod]
    public void ReusesTodaysCodeBehindUnchanged()
    {
        Assert.AreEqual(
            MainWindowTemplate.CodeBehind("Payroll"),
            Files().Single(f => f.Name == "MainWindow.xaml.cs").Content);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~WpfMasterDetailLayoutTests"`
Expected: FAIL to build — `PropertyEditorTemplate` does not exist.

- [ ] **Step 3: Write the `PropertyEditor` template**

Create `AppScaffolding\PropertyEditorTemplate.cs`. This source was verified to compile against the WindowsDesktop reference assemblies before this plan was written:

```csharp
namespace AppScaffolding;

/// <summary>
/// Emits <c>Views/PropertyEditor.cs</c>: the master-detail layout's edit form.
/// </summary>
/// <remarks>
/// WPF has no PropertyGrid, so the form is built by reflecting over the selected record. A
/// <c>ContentControl</c> subclass rather than code-behind, because the editor sits inside a
/// <c>DataTemplate</c> — named elements there are per-instance and cannot be reached by name from
/// the window's code-behind at all.
/// <para>
/// The <c>IsDisplayable</c> convention here is a THIRD copy of the one in
/// <see cref="MainWindowTemplate.CodeBehind"/> and <see cref="WinFormsMainFormTemplate.Source"/>.
/// Sharing it is not available: these are emitted files in a generated app, and lifting the helper
/// into a shared emitted type would change the tabbed layout's output, which must stay byte for
/// byte what it is. Keep all three in sync.
/// </para>
/// </remarks>
public static class PropertyEditorTemplate
{
    public const string FileName = "PropertyEditor.cs";

    public static string Source(string appName) => $$"""
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace {{appName}}.Views;

/// <summary>An edit form for whatever record it is given, built from that record's properties.</summary>
public sealed class PropertyEditor : ContentControl
{
    private readonly Grid _grid = new();

    public PropertyEditor()
    {
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddChild(_grid);
    }

    // Rebuilt on every change rather than bound once: each entity type has different properties,
    // and the same editor instance is reused as the grid selection moves between records.
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);

        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();

        if (newContent is null)
            return;

        var row = 0;

        foreach (var property in newContent.GetType().GetProperties())
        {
            if (!property.CanRead || !IsDisplayable(property.PropertyType))
                continue;

            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = property.Name,
                Margin = new Thickness(4),
                VerticalAlignment = VerticalAlignment.Center,
            };

            Grid.SetRow(label, row);
            Grid.SetColumn(label, 0);
            _grid.Children.Add(label);

            var box = new TextBox { Margin = new Thickness(4) };

            // Id is assigned by the store's NextId(), so it is shown but never edited -- the same
            // rule the window's column handler applies to the grid.
            box.IsReadOnly = !property.CanWrite
                || string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase);

            box.SetBinding(TextBox.TextProperty, new Binding(property.Name)
            {
                Mode = box.IsReadOnly ? BindingMode.OneWay : BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });

            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            _grid.Children.Add(box);

            row++;
        }
    }

    // Kept in step with the same convention in MainWindow.xaml.cs: a navigation property has no
    // sensible editor, and a collection would render as its type name.
    private static bool IsDisplayable(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying.IsPrimitive
            || underlying.IsEnum
            || underlying == typeof(string)
            || underlying == typeof(decimal)
            || underlying == typeof(DateTime)
            || underlying == typeof(Guid);
    }
}
""";
}
```

- [ ] **Step 4: Write the master-detail layout**

Create `AppScaffolding\Layouts\Wpf\WpfMasterDetailLayout.cs`:

```csharp
namespace AppScaffolding.Layouts.Wpf;

/// <summary>
/// Tab chrome, with each pane split into a grid above and an edit form for the selected row below.
/// </summary>
public sealed class WpfMasterDetailLayout : IAppLayout
{
    public string Key => AppLayoutCatalog.KeyFor(AppLayout.MasterDetail);

    public string Description => AppLayoutCatalog.Descriptions[AppLayout.MasterDetail];

    public IReadOnlyList<GeneratedFile> ViewFiles(LayoutContext context) =>
    [
        new("Views", "MainWindow.xaml", Xaml(context)),
        new("Views", "MainWindow.xaml.cs", MainWindowTemplate.CodeBehind(context.RootNamespace)),
        new("Views", PropertyEditorTemplate.FileName,
            PropertyEditorTemplate.Source(context.RootNamespace)),
    ];

    private static string Xaml(LayoutContext context)
    {
        var windowIcon = context.IconFileName is null
            ? string.Empty
            : $"\n        Icon=\"{AppCustomization.AssetsFolder}/{context.IconFileName}\"";

        var windowBrushes = context.Themed
            ? "\n        Background=\"{DynamicResource AppBackgroundBrush}\""
              + "\n        Foreground=\"{DynamicResource AppForegroundBrush}\""
            : string.Empty;

        return $$"""
<Window x:Class="{{context.RootNamespace}}.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:{{context.RootNamespace}}.Views"
        Title="{{context.TitleAttribute}}" Height="600" Width="900"{{windowIcon}}{{windowBrushes}}>
{{EntityPaneXaml.Resource(context.Palette, withEditor: true)}}
    <!-- SelectedIndex is explicit for the reason the tabbed layout gives. -->
    <TabControl ItemsSource="{Binding Entities}"
                DisplayMemberPath="Title"
                SelectedIndex="0"
                ContentTemplate="{StaticResource EntityPane}" />
</Window>
""";
    }
}
```

- [ ] **Step 5: Register it and update the two sync remarks**

Append `new WpfMasterDetailLayout(),` to `WpfLayouts.All`.

In `AppScaffolding\MainWindowTemplate.cs`, change the `CodeBehind` remark (currently "are deliberately duplicated in `WinFormsMainFormTemplate.Source` rather than shared: keep the two in sync") to name the third site:

```
    /// The <c>IsDisplayable</c> / <c>IsEntityType</c> / <c>DisplayMember</c> helpers emitted here
    /// are deliberately duplicated in <see cref="WinFormsMainFormTemplate.Source"/> and, for
    /// <c>IsDisplayable</c> alone, in <see cref="PropertyEditorTemplate.Source"/> rather than
    /// shared: keep all three in sync if the display convention ever changes.
```

Make the matching edit in `AppScaffolding\WinFormsMainFormTemplate.cs:10-14`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~WpfMasterDetailLayoutTests"`
Expected: PASS, 6 tests.

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx`
Expected: PASS in full — the two remark edits are comments in template *host* files, not in emitted strings, so no output changes. If a neutrality test fails, the edit landed inside a raw literal.

- [ ] **Step 7: Commit**

```bash
cd D:/SmartCodeGenerator-Repository
git add AppScaffolding CodeGenerator.Tests/App/WpfMasterDetailLayoutTests.cs
git commit -m "feat(scaffold): WPF master-detail layout with an emitted PropertyEditor"
```

---

### Task 9: The shared WinForms pane, and the `sidebar` layout

WinForms' pane lives in `BuildTab`, which returns a `TabPage` — so the new containers cannot call it as-is. The non-tabs layouts share a `BuildPane` returning a `Control`, emitted from one fragment. As on the WPF side, `tabs` keeps its own untouched copy.

**Files:**
- Create: `AppScaffolding\Layouts\WinForms\WinFormsPaneCode.cs`, `AppScaffolding\Layouts\WinForms\WinFormsSidebarLayout.cs`
- Modify: `AppScaffolding\Layouts\WinForms\WinFormsLayouts.cs`
- Test: `CodeGenerator.Tests\App\WinFormsSidebarLayoutTests.cs`

**Interfaces:**
- Consumes: `IAppLayout`, `LayoutContext`, `EmittedDesktopSource.AssertCompiles`
- Produces: `WinFormsPaneCode.BuildPaneMethod(ThemePalette? palette, bool withPropertyGrid)` — the emitted `BuildPane` method plus the `ApplyColumnRules` helpers it needs; `WinFormsPaneCode.FormShell(LayoutContext, string containerCode, string members)`; `WinFormsSidebarLayout`. Tasks 10 and 11 both call `WinFormsPaneCode`.

- [ ] **Step 1: Read the whole existing template first**

Read `AppScaffolding\WinFormsMainFormTemplate.cs` end to end. `BuildPane` is its `BuildTab` body with the `TabPage` swapped for a `Panel`, and it needs the same `ApplyColumnRules`, `IsDisplayable`, `IsEntityType` and `DisplayMember` helpers that file already emits. Copy them exactly rather than paraphrasing — a paraphrase here is a behaviour change in the generated app.

- [ ] **Step 2: Write the failing tests**

Create `CodeGenerator.Tests\App\WinFormsSidebarLayoutTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.WinForms;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WinFormsSidebarLayoutTests
{
    private static string Source(AppCustomization? customization = null)
    {
        var settings = customization ?? AppCustomization.Default;

        return WinFormsLayouts.Resolve(AppLayout.Sidebar).ViewFiles(
                new LayoutContext("Payroll.WinForms", "Payroll.WinForms", settings,
                    ThemePalette.Resolve(settings.Theme, settings.Accent)))
            .Single(f => f.Name == "MainForm.cs").Content;
    }

    [TestMethod]
    public void ListsTheEntitiesBesideAPaneHost()
    {
        var source = Source();

        StringAssert.Contains(source, "new ListBox");
        StringAssert.Contains(source, "DisplayMember = \"Title\"");
        StringAssert.Contains(source, "DockStyle.Left");
        StringAssert.Contains(source, "SelectedIndexChanged");
        StringAssert.Contains(source, "BuildPane(");
    }

    [TestMethod]
    public void SelectsTheFirstEntityOnStartup()
    {
        StringAssert.Contains(Source(), "SelectedIndex = 0;");
    }

    [TestMethod]
    public void KeepsTheGridBindingAndColumnRules()
    {
        var source = Source();

        // The BindingSource republish is what makes Add/Delete visible in a DataGridView, which
        // does not listen to INotifyCollectionChanged.
        StringAssert.Contains(source, "new BindingSource");
        StringAssert.Contains(source, "binding.ResetBindings(false)");
        StringAssert.Contains(source, "ApplyColumnRules");
        StringAssert.Contains(source, "DataBindingComplete");
    }

    [TestMethod]
    public void Compiles()
    {
        EmittedDesktopSource.AssertCompiles(
            EmittedSource.Flatten(Source(), ViewModelStubs.WinFormsHost));
    }

    [TestMethod]
    public void PaintsWithTheThemeColoursOnlyWhenThemed()
    {
        StringAssert.Contains(
            Source(AppCustomization.Default with { Theme = AppTheme.Dark }),
            "ColorTranslator.FromHtml");

        Assert.DoesNotContain("ColorTranslator", Source());
    }
}
```

- [ ] **Step 3: Add the shared view-model stubs the compile tests need**

Create `CodeGenerator.Tests\App\ViewModelStubs.cs` — Tasks 9, 10 and 11 all use it:

```csharp
namespace CodeGenerator.Tests.App;

/// <summary>
/// Stand-ins for the view-model files a generated app emits, so an emitted view can be compiled
/// on its own.
/// </summary>
/// <remarks>
/// The real types come from <c>DesktopViewModelFiles</c>. Stubs rather than the real emitted
/// source because the real <c>EntityViewModel&lt;T&gt;</c> pulls in JsonStore, the validators and
/// the models — a compile test of a VIEW should fail for reasons in the view.
/// </remarks>
internal static class ViewModelStubs
{
    /// <summary>Everything a WinForms MainForm binds to.</summary>
    public const string WinFormsHost = """
        using System.Collections;
        using System.Collections.ObjectModel;
        using System.ComponentModel;
        using System.Windows.Input;

        namespace Payroll.WinForms.ViewModels;

        public interface IEntityViewModel
        {
            string Title { get; }
            IEnumerable Items { get; }
            object? Selected { get; set; }
            ICommand AddCommand { get; }
            ICommand DeleteCommand { get; }
            ICommand SaveCommand { get; }
            string Status { get; }
        }

        public sealed class MainViewModel
        {
            public ObservableCollection<IEntityViewModel> Entities { get; } = new();
        }
        """;
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~WinFormsSidebarLayoutTests"`
Expected: FAIL — the sidebar resolves to the tabs layout, so `DockStyle.Left` is absent.

- [ ] **Step 5: Write `WinFormsPaneCode`**

Create `AppScaffolding\Layouts\WinForms\WinFormsPaneCode.cs`. `BuildPaneMethod` is `WinFormsMainFormTemplate`'s `BuildTab` with the `TabPage` replaced by a `Panel` and, when `withPropertyGrid`, a `PropertyGrid` added to a fourth row that follows the grid's selection:

```csharp
namespace AppScaffolding.Layouts.WinForms;

/// <summary>
/// The per-entity pane the non-tabbed WinForms layouts share, and the form shell around it.
/// </summary>
/// <remarks>
/// <c>WinFormsMainFormTemplate.BuildTab</c> returns a <c>TabPage</c>, which a sidebar or a
/// dashboard cannot host, so the pane is re-emitted here as a <c>Panel</c>-returning
/// <c>BuildPane</c>. The tabbed layout keeps its own copy and must not be refactored onto this
/// one: its output is byte-identical to every pre-layout app's. Change both when the pane changes.
/// </remarks>
public static class WinFormsPaneCode
{
    /// <summary>
    /// The <c>BuildPane</c> method plus the column-rule helpers it needs, ready to paste into a
    /// MainForm class body. With <paramref name="withPropertyGrid"/> the pane carries a
    /// PropertyGrid bound to the current row.
    /// </summary>
    public static string BuildPaneMethod(ThemePalette? palette, bool withPropertyGrid = false)
    {
        // ... emitted with the same gridColours fragment WinFormsMainFormTemplate builds, the same
        // BindingSource republish, the same DataBindingComplete idempotence comment, and the same
        // ApplyColumnRules / IsDisplayable / IsEntityType / DisplayMember helpers copied verbatim
        // from that file.
        throw new NotImplementedException("copy from WinFormsMainFormTemplate per step 1");
    }
}
```

> **Implementer:** the `throw` above is a marker, not the deliverable. Fill this method by copying `BuildTab`'s body and the four helpers verbatim out of `WinFormsMainFormTemplate.Source`, making exactly three changes: return `Control` and build a `Panel` instead of a `TabPage`; add the `PropertyGrid` row when `withPropertyGrid`; keep every comment. The test for step 3 that must pass is `Compiles` — copying by hand and getting a member name wrong fails there, not silently.

The PropertyGrid addition, when `withPropertyGrid` is true, goes after the grid is created and adds a fourth row to the `TableLayoutPanel`:

```csharp
        // WinForms has a PropertyGrid, so the detail pane needs no reflection code of its own --
        // unlike WPF, where the same feature needs an emitted PropertyEditor control.
        var detail = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            ToolbarVisible = false,
            PropertySort = PropertySort.NoSort,
        };

        grid.SelectionChanged += (_, _) => detail.SelectedObject = grid.CurrentRow?.DataBoundItem;
```

- [ ] **Step 6: Write the sidebar layout**

Create `AppScaffolding\Layouts\WinForms\WinFormsSidebarLayout.cs`, emitting a `MainForm` whose constructor builds the list and the host panel:

```csharp
namespace AppScaffolding.Layouts.WinForms;

/// <summary>A navigation list of entities docked left, the selected entity's pane filling the rest.</summary>
public sealed class WinFormsSidebarLayout : IAppLayout
{
    public string Key => AppLayoutCatalog.KeyFor(AppLayout.Sidebar);

    public string Description => AppLayoutCatalog.Descriptions[AppLayout.Sidebar];

    public IReadOnlyList<GeneratedFile> ViewFiles(LayoutContext context) =>
    [
        new("Views", "MainForm.cs", Source(context)),
    ];

    private static string Source(LayoutContext context)
    {
        var formColours = context.Palette is null
            ? string.Empty
            : $$"""

                    BackColor = ColorTranslator.FromHtml("{{context.Palette.Background}}");
                    ForeColor = ColorTranslator.FromHtml("{{context.Palette.Foreground}}");
            """;

        var formIcon = context.IconFileName is not null
                       && context.IconFileName.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
            ? $$"""

                    var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "{{AppCustomization.AssetsFolder}}", "{{context.IconFileName}}");
                    if (System.IO.File.Exists(iconPath))
                        Icon = new Icon(iconPath);
            """
            : string.Empty;

        return $$"""
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using {{context.RootNamespace}}.ViewModels;

namespace {{context.RootNamespace}}.Views;

/// <summary>A list of entities beside the selected one's records.</summary>
public sealed class MainForm : Form
{
    private readonly MainViewModel _model = new();
    private readonly Panel _host = new() { Dock = DockStyle.Fill };

    public MainForm()
    {
        Text = "{{context.TitleAttribute}}";
        Width = 900;
        Height = 600;
{{formColours}}{{formIcon}}
        var list = new ListBox
        {
            Dock = DockStyle.Left,
            Width = 180,
            DisplayMember = "Title",
            IntegralHeight = false,
        };

        foreach (var entity in _model.Entities)
            list.Items.Add(entity);

        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedItem is not IEntityViewModel entity)
                return;

            // One pane at a time: the old one is disposed rather than hidden, so its event
            // handlers stop firing when the selection moves on.
            foreach (Control existing in _host.Controls)
                existing.Dispose();

            _host.Controls.Clear();
            _host.Controls.Add(BuildPane(entity, _model));
        };

        // Added before the list so docking resolves the fill against the remaining space.
        Controls.Add(_host);
        Controls.Add(list);

        // Opens on the first entity rather than an empty panel, as every other layout does.
        if (list.Items.Count > 0)
            list.SelectedIndex = 0;
    }

{{WinFormsPaneCode.BuildPaneMethod(context.Palette)}}
}
""";
    }
}
```

- [ ] **Step 7: Register it, run the tests**

Append `new WinFormsSidebarLayout(),` to `WinFormsLayouts.All`.

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~WinFormsSidebarLayoutTests"`
Expected: PASS, 5 tests.

- [ ] **Step 8: Commit**

```bash
cd D:/SmartCodeGenerator-Repository
git add AppScaffolding/Layouts/WinForms CodeGenerator.Tests/App/WinFormsSidebarLayoutTests.cs CodeGenerator.Tests/App/ViewModelStubs.cs
git commit -m "feat(scaffold): WinForms sidebar layout"
```

---

### Task 10: The WinForms `master-detail` layout

**Files:**
- Create: `AppScaffolding\Layouts\WinForms\WinFormsMasterDetailLayout.cs`
- Modify: `AppScaffolding\Layouts\WinForms\WinFormsLayouts.cs`
- Test: `CodeGenerator.Tests\App\WinFormsMasterDetailLayoutTests.cs`

**Interfaces:**
- Consumes: `WinFormsPaneCode.BuildPaneMethod(palette, withPropertyGrid: true)`
- Produces: `WinFormsMasterDetailLayout`.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests\App\WinFormsMasterDetailLayoutTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.WinForms;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WinFormsMasterDetailLayoutTests
{
    private static string Source()
        => WinFormsLayouts.Resolve(AppLayout.MasterDetail).ViewFiles(
                new LayoutContext("Payroll.WinForms", "Payroll.WinForms", AppCustomization.Default, null))
            .Single(f => f.Name == "MainForm.cs").Content;

    [TestMethod]
    public void KeepsTheTabsAndAddsAPropertyGridUnderTheRecords()
    {
        var source = Source();

        StringAssert.Contains(source, "new TabControl");
        StringAssert.Contains(source, "new PropertyGrid");
        StringAssert.Contains(source, "detail.SelectedObject = grid.CurrentRow?.DataBoundItem");
    }

    [TestMethod]
    public void Compiles()
    {
        EmittedDesktopSource.AssertCompiles(
            EmittedSource.Flatten(Source(), ViewModelStubs.WinFormsHost));
    }

    [TestMethod]
    public void LeaksNoGeneratorImplementationDetails()
    {
        var source = Source();

        Assert.DoesNotContain("WinFormsPaneCode", source);
        Assert.DoesNotContain("WinFormsMainFormTemplate", source);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~WinFormsMasterDetailLayoutTests"`
Expected: FAIL — no `PropertyGrid` in the tabs output.

- [ ] **Step 3: Write the layout**

Create `AppScaffolding\Layouts\WinForms\WinFormsMasterDetailLayout.cs`. Same shell as `WinFormsSidebarLayout` (same usings, same `formColours`/`formIcon` fragments) with the constructor body building tabs over the pane, and `BuildPaneMethod(context.Palette, withPropertyGrid: true)` at the end of the class:

```csharp
        var tabs = new TabControl { Dock = DockStyle.Fill };

        foreach (var entity in _model.Entities)
        {
            var page = new TabPage(entity.Title);
            page.Controls.Add(BuildPane(entity, _model));
            tabs.TabPages.Add(page);
        }

        Controls.Add(tabs);
```

- [ ] **Step 4: Register it and run the tests**

Append `new WinFormsMasterDetailLayout(),` to `WinFormsLayouts.All`.

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~WinFormsMasterDetailLayoutTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
cd D:/SmartCodeGenerator-Repository
git add AppScaffolding/Layouts/WinForms CodeGenerator.Tests/App/WinFormsMasterDetailLayoutTests.cs
git commit -m "feat(scaffold): WinForms master-detail layout"
```

---

### Task 11: The WinForms `dashboard` layout

**Files:**
- Create: `AppScaffolding\Layouts\WinForms\WinFormsDashboardLayout.cs`
- Modify: `AppScaffolding\Layouts\WinForms\WinFormsLayouts.cs`
- Test: `CodeGenerator.Tests\App\WinFormsDashboardLayoutTests.cs`

**Interfaces:**
- Consumes: `WinFormsPaneCode.BuildPaneMethod(palette)`
- Produces: `WinFormsDashboardLayout`. This completes both registries.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests\App\WinFormsDashboardLayoutTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.WinForms;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WinFormsDashboardLayoutTests
{
    private static string Source()
        => WinFormsLayouts.Resolve(AppLayout.Dashboard).ViewFiles(
                new LayoutContext("Payroll.WinForms", "Payroll.WinForms", AppCustomization.Default, null))
            .Single(f => f.Name == "MainForm.cs").Content;

    [TestMethod]
    public void ShowsACardPerEntityWithALiveRowCount()
    {
        var source = Source();

        StringAssert.Contains(source, "new FlowLayoutPanel");
        StringAssert.Contains(source, "entity.Items.Cast<object>().Count()");

        // WinForms has no binding path to Items.Count, so the count is refreshed from the
        // collection's own change notification.
        StringAssert.Contains(source, "INotifyCollectionChanged");
    }

    [TestMethod]
    public void NavigatesToAnEntityAndBack()
    {
        var source = Source();

        StringAssert.Contains(source, "BuildPane(");
        StringAssert.Contains(source, "\"Back\"");
    }

    [TestMethod]
    public void Compiles()
    {
        EmittedDesktopSource.AssertCompiles(
            EmittedSource.Flatten(Source(), ViewModelStubs.WinFormsHost));
    }

    [TestMethod]
    public void EveryLayoutIsRegisteredForBothTargets()
    {
        // The registries fall back to Default for an unimplemented layout, which would make a
        // missing layout look like a working tabs app rather than a failure.
        foreach (var layout in Enum.GetValues<AppLayout>())
        {
            Assert.AreEqual(AppLayoutCatalog.KeyFor(layout), WinFormsLayouts.Resolve(layout).Key);
            Assert.AreEqual(AppLayoutCatalog.KeyFor(layout), Wpf.WpfLayouts.Resolve(layout).Key);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~WinFormsDashboardLayoutTests"`
Expected: FAIL — no `FlowLayoutPanel`, and the registry-completeness test fails for `dashboard`.

- [ ] **Step 3: Write the layout**

Create `AppScaffolding\Layouts\WinForms\WinFormsDashboardLayout.cs` — same shell as the sidebar, with this constructor body and card builder:

```csharp
        var cards = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var back = new Button { Text = "Back", Width = 80, Margin = new Padding(8), Visible = false };

        foreach (var entity in _model.Entities)
            cards.Controls.Add(BuildCard(entity, _model));

        back.Click += (_, _) =>
        {
            foreach (Control existing in _host.Controls)
                existing.Dispose();

            _host.Controls.Clear();
            _host.Visible = false;
            back.Visible = false;
            cards.Visible = true;
        };

        Controls.Add(_host);
        Controls.Add(back);
        Controls.Add(cards);
```

and the card builder, which is where the live count lives:

```csharp
    // A button rather than a styled panel: it is already focusable, keyboard-activatable and
    // themed by the OS, which a Panel with a Click handler is not.
    private Button BuildCard(IEntityViewModel entity, MainViewModel model)
    {
        var card = new Button
        {
            Width = 160,
            Height = 90,
            Margin = new Padding(8),
            TextAlign = ContentAlignment.MiddleCenter,
        };

        void Refresh() =>
            card.Text = entity.Title + Environment.NewLine + entity.Items.Cast<object>().Count();

        Refresh();

        // No binding path to a count in WinForms, so the card listens to the collection itself.
        if (entity.Items is INotifyCollectionChanged observable)
            observable.CollectionChanged += (_, _) => Refresh();

        card.Click += (_, _) =>
        {
            _host.Controls.Clear();
            _host.Controls.Add(BuildPane(entity, model));
            _host.Visible = true;
            _host.BringToFront();
        };

        return card;
    }
```

> The emitted file needs `using System.Linq;` for `Cast<object>().Count()` and `using System.Collections.Specialized;` for `INotifyCollectionChanged` — both are already in the shell's using list. The `Back` button's `Visible = true` is set in the card's Click handler; wire it by capturing the button, or by looking it up from `Controls`. Choose the capture: it is checked by the `Compiles` test either way.

- [ ] **Step 4: Register it and run the tests**

Append `new WinFormsDashboardLayout(),` to `WinFormsLayouts.All`.

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~WinFormsDashboardLayoutTests"`
Expected: PASS, 4 tests — including registry completeness for both targets.

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx`
Expected: PASS in full.

- [ ] **Step 5: Commit**

```bash
cd D:/SmartCodeGenerator-Repository
git add AppScaffolding/Layouts/WinForms CodeGenerator.Tests/App/WinFormsDashboardLayoutTests.cs
git commit -m "feat(scaffold): WinForms dashboard layout, completing both registries"
```

---

### Task 12: The inline `layout` option

**Files:**
- Modify: `CodeGenerator\App\InlineCustomization.cs:35-36` (`ValueKeys`) and its `ApplyValue` switch
- Modify: `CodeGenerator\App\BuildAppCommand.cs:137-160` (`Merge`)
- Test: `CodeGenerator.Tests\App\InlineCustomizationTests.cs` (add cases), `CodeGenerator.Tests\App\CustomizationMergeTests.cs` (add cases)

**Interfaces:**
- Consumes: `AppLayoutCatalog.TryResolve`, `AppCustomization.Layout`
- Produces: nothing new — `layout <key>` is understood on a `build app:` line, and `Merge` treats a named layout as typed-inline.

- [ ] **Step 1: Write the failing tests**

Add to `CodeGenerator.Tests\App\InlineCustomizationTests.cs` (match the existing file's helper style — read it first):

```csharp
    [TestMethod]
    public void Layout_IsLiftedOffTheLine()
    {
        var (remaining, customization) =
            InlineCustomization.Split("a shop layout sidebar", AppCustomization.Default);

        Assert.AreEqual("a shop", remaining);
        Assert.AreEqual(AppLayout.Sidebar, customization.Layout);
    }

    [TestMethod]
    public void Layout_ReadsTheHyphenatedMasterDetailKey()
    {
        var (remaining, customization) =
            InlineCustomization.Split("a shop layout master-detail", AppCustomization.Default);

        Assert.AreEqual("a shop", remaining);
        Assert.AreEqual(AppLayout.MasterDetail, customization.Layout);
    }

    [TestMethod]
    public void Layout_TabsIsRecordedAsTypedRatherThanTreatedAsAbsent()
    {
        var (_, customization) =
            InlineCustomization.Split("a shop layout tabs", AppCustomization.Default);

        // Not null: this is the whole reason Layout is nullable. See the Merge test.
        Assert.AreEqual(AppLayout.Tabs, customization.Layout);
        Assert.IsNotNull(customization.Layout);
    }

    [TestMethod]
    public void An_unusable_layout_leaves_the_line_alone()
    {
        var (remaining, customization) =
            InlineCustomization.Split("a shop layout kanban", AppCustomization.Default);

        // The `theme mauve` rule: the user's words are likelier prose than a typo to guess at.
        Assert.AreEqual("a shop layout kanban", remaining);
        Assert.IsNull(customization.Layout);
    }

    [TestMethod]
    public void Layout_CoexistsWithTheOtherOptions()
    {
        var (remaining, customization) = InlineCustomization.Split(
            "a shop company \"Codis Ltd\" layout dashboard theme dark", AppCustomization.Default);

        Assert.AreEqual("a shop", remaining);
        Assert.AreEqual(AppLayout.Dashboard, customization.Layout);
        Assert.AreEqual(AppTheme.Dark, customization.Theme);
        Assert.AreEqual("Codis Ltd", customization.Company);
    }
```

Add to `CodeGenerator.Tests\App\CustomizationMergeTests.cs`:

```csharp
    [TestMethod]
    public void An_inline_layout_beats_the_remembered_one()
    {
        var saved = AppCustomization.Default with { Layout = AppLayout.Dashboard };
        var inline = AppCustomization.Default with { Layout = AppLayout.Sidebar };

        Assert.AreEqual(AppLayout.Sidebar, BuildAppCommand.Merge(saved, inline).Layout);
    }

    [TestMethod]
    public void An_inline_tabs_beats_a_remembered_dashboard()
    {
        // The defect this test exists to prevent: `layout tabs` equals the DEFAULT layout, so a
        // differs-from-Default rule would read it as "not mentioned" and hand back the profile's
        // dashboard — silently ignoring what the user just typed.
        var saved = AppCustomization.Default with { Layout = AppLayout.Dashboard };
        var inline = AppCustomization.Default with { Layout = AppLayout.Tabs };

        Assert.AreEqual(AppLayout.Tabs, BuildAppCommand.Merge(saved, inline).Layout);
    }

    [TestMethod]
    public void A_remembered_layout_survives_a_line_that_names_none()
    {
        var saved = AppCustomization.Default with { Layout = AppLayout.Sidebar };

        Assert.AreEqual(AppLayout.Sidebar, BuildAppCommand.Merge(saved, AppCustomization.Default).Layout);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~InlineCustomizationTests|FullyQualifiedName~CustomizationMergeTests"`
Expected: FAIL — the layout tests fail (the words stay on the line, `Layout` is null) while every pre-existing case in both files passes.

- [ ] **Step 3: Teach the inline parser**

In `CodeGenerator\App\InlineCustomization.cs`, add `"layout"` to `ValueKeys`:

```csharp
    private static readonly string[] ValueKeys =
        ["company", "theme", "accent", "icon", "title", "namespace", "layout"];
```

and a case to `ApplyValue`, before `default`:

```csharp
            case "layout":
                // Refused rather than ignored when unusable, like `theme mauve`: the words are
                // more likely part of the description than a layout name we should guess at.
                return AppLayoutCatalog.TryResolve(value, out var layout)
                    ? customization with { Layout = layout }
                    : null;
```

- [ ] **Step 4: Teach `Merge`**

Read `BuildAppCommand.Merge` (lines 137-160) and add the layout to the layering. A named layout is `not null`, which is exact — do **not** compare against `defaults.Layout`:

```csharp
            // `is not null` rather than a differs-from-default comparison: `layout tabs` is
            // typeable and EQUALS the default, so comparing values would read it as unmentioned.
            Layout = inline.Layout ?? saved.Layout,
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~InlineCustomizationTests|FullyQualifiedName~CustomizationMergeTests"`
Expected: PASS, all cases including the pre-existing ones.

- [ ] **Step 6: Commit**

```bash
cd D:/SmartCodeGenerator-Repository
git add CodeGenerator/App/InlineCustomization.cs CodeGenerator/App/BuildAppCommand.cs CodeGenerator.Tests/App
git commit -m "feat(cli): inline layout option for build app"
```

---

### Task 13: The wizard prompt, the profile, and the summary

**Files:**
- Modify: `CodeGenerator\App\CustomizationWizard.cs` (`Run` signature and the new first prompt)
- Modify: `CodeGenerator\App\BuildAppWizard.cs:98` (pass the target)
- Modify: `CodeGenerator\App\CustomizationDefaults.cs` (`Profile.Layout`, `Load`, `Save`)
- Modify: `CodeGenerator\App\CustomizationSummary.cs`
- Test: `CodeGenerator.Tests\App\CustomizationWizardTests.cs`, `CustomizationDefaultsTests.cs`, `CustomizationSummaryTests.cs` (read each first; add cases)

**Interfaces:**
- Consumes: `AppLayouts.Supports(string)`, `AppLayoutCatalog.PromptOptions`/`TryResolve`/`KeyFor`
- Produces: `CustomizationWizard.Run(TextReader, TextWriter, AppCustomization, ITargetScaffolder?)` — a fourth parameter, defaulted to null so existing test callers still compile.

- [ ] **Step 1: Write the failing tests**

Add to `CodeGenerator.Tests\App\CustomizationWizardTests.cs`:

```csharp
    [TestMethod]
    public void AsksForTheLayoutFirstOnADesktopTarget()
    {
        var reader = new StringReader(string.Join("\n", ["y", "sidebar", "", "", "", "", "", "", "", "", ""]));
        var writer = new StringWriter();

        var result = CustomizationWizard.Run(reader, writer, AppCustomization.Default, new WpfTarget());

        Assert.IsNotNull(result);
        Assert.AreEqual(AppLayout.Sidebar, result.Layout);

        var output = writer.ToString();

        // Structural, not decoration: it is asked before the company.
        Assert.IsLessThan(
            output.IndexOf("Company", StringComparison.Ordinal),
            output.IndexOf("Layout", StringComparison.Ordinal),
            "layout is the structural choice and is asked first");
        StringAssert.Contains(output, "tabs/sidebar/master-detail/dashboard");
    }

    [TestMethod]
    public void SkipsTheLayoutPromptForATargetWithNoWindow()
    {
        var reader = new StringReader(string.Join("\n", ["y", "", "", "", "", "", "", "", "", ""]));
        var writer = new StringWriter();

        var result = CustomizationWizard.Run(reader, writer, AppCustomization.Default, new ConsoleTarget());

        Assert.IsNotNull(result);
        Assert.IsNull(result.Layout);
        Assert.DoesNotContain("Layout", writer.ToString());
    }

    [TestMethod]
    public void AnUnusableLayoutAnswerKeepsWhatWasOffered()
    {
        var start = AppCustomization.Default with { Layout = AppLayout.Dashboard };
        var reader = new StringReader(string.Join("\n", ["y", "kanban", "", "", "", "", "", "", "", "", ""]));

        var result = CustomizationWizard.Run(
            new StringReader(reader.ReadToEnd()), new StringWriter(), start, new WpfTarget());

        Assert.AreEqual(AppLayout.Dashboard, result!.Layout);
    }
```

Add to `CustomizationDefaultsTests.cs`:

```csharp
    [TestMethod]
    public void Layout_RoundTripsThroughTheProfile()
    {
        var root = NewWorkspace();

        CustomizationDefaults.Save(root, AppCustomization.Default with { Layout = AppLayout.MasterDetail });

        Assert.AreEqual(AppLayout.MasterDetail, CustomizationDefaults.Load(root).Layout);
    }

    [TestMethod]
    public void AnUnrecognizedStoredLayoutFallsBack()
    {
        var root = NewWorkspace();
        var path = CustomizationDefaults.PathFor(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "Layout": "kanban" }""");

        // A hand-edited profile bypasses every other validation surface, so Load re-validates.
        Assert.IsNull(CustomizationDefaults.Load(root).Layout);
    }
```

Add to `CustomizationSummaryTests.cs`:

```csharp
    [TestMethod]
    public void NamesTheLayoutWhenOneWasChosen()
    {
        var lines = CustomizationSummary.Lines(AppCustomization.Default with { Layout = AppLayout.Dashboard });

        Assert.Contains("  Layout: dashboard", lines);
    }

    [TestMethod]
    public void SaysNothingAboutLayoutWhenNoneWasChosen()
    {
        Assert.IsEmpty(CustomizationSummary.Lines(AppCustomization.Default));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~CustomizationWizardTests|FullyQualifiedName~CustomizationDefaultsTests|FullyQualifiedName~CustomizationSummaryTests"`
Expected: FAIL to build — `Run` has no fourth parameter.

- [ ] **Step 3: Add the wizard prompt**

In `CustomizationWizard.Run`, add the parameter and ask the layout immediately after the gate, before Company:

```csharp
    public static AppCustomization? Run(
        TextReader reader, TextWriter writer, AppCustomization start,
        ITargetScaffolder? target = null)
```

and after `writer.WriteLine();` following the `IsYes(answer)` check:

```csharp
        // First, and only for a target that has a window: layout is the one structural choice in
        // a step that is otherwise decoration, and asking a console build for a window shape
        // would be nonsense.
        var layout = start.Layout;

        if (target is not null && AppLayouts.Supports(target.Key))
        {
            var answer = Ask(
                reader, writer, $"Layout [{AppLayoutCatalog.PromptOptions}]",
                layout is null ? AppLayoutCatalog.KeyFor(AppLayout.Tabs) : AppLayoutCatalog.KeyFor(layout.Value));

            if (answer is null) return null;

            // An unusable answer keeps the current value rather than re-prompting, as a malformed
            // accent does.
            layout = AppLayoutCatalog.TryResolve(answer, out var parsed) ? parsed : layout;
        }
```

and add `Layout = layout,` to the returned `start with { … }`.

- [ ] **Step 4: Pass the target from `BuildAppWizard`**

At `BuildAppWizard.cs:98`, the target chosen at line 81 is already in scope:

```csharp
        var customization = CustomizationWizard.Run(
            reader, writer, start ?? AppCustomization.Default, target);
```

- [ ] **Step 5: Add the profile field**

In `CustomizationDefaults.Profile`, add `public string? Layout { get; set; }`. In `Load`, add `Layout: ParseLayout(profile.Layout)`; in `Save`, `Layout = customization.Layout is null ? null : AppLayoutCatalog.KeyFor(customization.Layout.Value),`. Add the parser beside `ParseTheme`:

```csharp
    /// <summary>The stored layout, or null when absent or unrecognized.</summary>
    /// <remarks>
    /// Re-validated here for the reason <see cref="ValidatedPrefix"/> gives: a hand-edited profile
    /// bypasses the inline parser and the wizard, and Load otherwise assigns straight from JSON.
    /// </remarks>
    private static AppLayout? ParseLayout(string? value)
    {
        if (value is null)
            return null;

        return AppLayoutCatalog.TryResolve(value, out var layout) ? layout : null;
    }
```

- [ ] **Step 6: Add the summary line**

In `CustomizationSummary.Lines`, after the namespace-prefix line:

```csharp
        if (customization.Layout is not null)
            lines.Add($"  Layout: {AppLayoutCatalog.KeyFor(customization.Layout.Value)}");
```

- [ ] **Step 7: Run the tests to verify they pass, then the full suite**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~Customization"`
Expected: PASS.

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx`
Expected: PASS in full. Existing wizard tests feed a fixed number of answers; because the prompt is skipped when `target` is null (their case), none of them shifts. **If one fails on answer alignment, the default-null parameter is not doing its job — do not renumber the existing test's answers.**

- [ ] **Step 8: Commit**

```bash
cd D:/SmartCodeGenerator-Repository
git add CodeGenerator/App CodeGenerator.Tests/App
git commit -m "feat(cli): layout in the wizard, the profile and the review summary"
```

---

### Task 14: Help catalogue and the `approve`-time note

**Files:**
- Modify: `CodeGenerationEngine\Core\CommandDictionary.cs:538-551` (the customization entry)
- Modify: `CodeGenerator\App\BuildAppCommand.cs` (around the `IsPersistenceAllowed` check at line 425)
- Test: `CodeGenerator.Tests\Core\CommandDictionaryDataTests.cs` (read first — it audits the catalogue), `CodeGenerator.Tests\App\LayoutTargetNoticeTests.cs`

**Interfaces:**
- Consumes: `AppLayoutCatalog.HelpOptions`, `AppLayouts.Supports`
- Produces: `BuildAppCommand.LayoutNotice(ITargetScaffolder target, AppCustomization customization)` returning `string?` — internal and pure, for the reason `WhyLines` is.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests\App\LayoutTargetNoticeTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class LayoutTargetNoticeTests
{
    [TestMethod]
    public void SaysNothingWhenTheTargetCanUseTheLayout()
    {
        var customization = AppCustomization.Default with { Layout = AppLayout.Dashboard };

        Assert.IsNull(BuildAppCommand.LayoutNotice(new WpfTarget(), customization));
        Assert.IsNull(BuildAppCommand.LayoutNotice(new WinFormsTarget(), customization));
    }

    [TestMethod]
    public void SaysNothingWhenNoLayoutWasNamed()
    {
        Assert.IsNull(BuildAppCommand.LayoutNotice(new ConsoleTarget(), AppCustomization.Default));
    }

    [TestMethod]
    public void WarnsWhenALayoutWasNamedForATargetWithNoWindow()
    {
        // The wizard skips the prompt, but the target can still be changed at `approve` — which
        // is exactly why IsPersistenceAllowed is re-checked there too.
        var notice = BuildAppCommand.LayoutNotice(
            new ConsoleTarget(), AppCustomization.Default with { Layout = AppLayout.Sidebar });

        Assert.IsNotNull(notice);
        StringAssert.Contains(notice, "console");
        StringAssert.Contains(notice, "sidebar");
    }
}
```

Add to `CodeGenerator.Tests\Core\CommandDictionaryDataTests.cs`:

```csharp
    [TestMethod]
    public void TheCustomizationEntryDocumentsEveryLayoutKey()
    {
        var text = string.Join(
            "\n",
            CommandDictionary.All.Select(e => e.Description + " " + e.Example));

        foreach (var key in AppLayoutCatalog.Keys)
            StringAssert.Contains(text, key);
    }
```

> Read `CommandDictionaryDataTests` first: it may already expose the catalogue under a different member name than `All`. Use whatever the existing cases use.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~LayoutTargetNoticeTests|FullyQualifiedName~CommandDictionaryDataTests"`
Expected: FAIL to build (`LayoutNotice` missing), and the catalogue audit fails on `sidebar`.

- [ ] **Step 3: Write the notice**

In `CodeGenerator\App\BuildAppCommand.cs`, beside `IsPersistenceAllowed`:

```csharp
    /// <summary>
    /// What to tell the user when the target they approved cannot use the layout they named, or
    /// null when there is nothing to say.
    /// </summary>
    /// <remarks>
    /// Internal and pure for the same reason <see cref="WhyLines"/> is. The check belongs at
    /// `approve` rather than only in the wizard because the target can still be changed there —
    /// the same reason SQLite is re-checked at that point.
    /// </remarks>
    internal static string? LayoutNotice(ITargetScaffolder target, AppCustomization customization)
    {
        if (customization.Layout is null || AppLayouts.Supports(target.Key))
            return null;

        return $"The {AppLayoutCatalog.KeyFor(customization.Layout.Value)} layout applies to the "
             + $"wpf and winforms targets; the {target.Key} target has no window, so it is ignored.";
    }
```

Call it where the target is settled, beside the SQLite check at line 425 and in `ApproveInteractively`'s caller:

```csharp
                    var layoutNotice = LayoutNotice(chosenTarget, customization);

                    if (layoutNotice is not null)
                        Console.WriteLine(layoutNotice);
```

- [ ] **Step 4: Extend the help catalogue**

In `CommandDictionary.cs`, extend the customization entry's `Description` (currently ending with the `namespace <prefix>` clause) and give the layout its own sentence:

```csharp
                        + "namespace <prefix> puts the generated code under that namespace root "
                        + "(CodisLtd.Shop), leaving the project and assembly names alone; "
                        + "layout tabs|sidebar|master-detail|dashboard chooses the shape of the "
                        + "window (wpf and winforms only).",
            Example = "build app: a shop company \"Codis Ltd\" namespace CodisLtd theme dark layout sidebar"
```

- [ ] **Step 5: Run the tests to verify they pass, then the full suite**

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx --filter "FullyQualifiedName~LayoutTargetNoticeTests|FullyQualifiedName~CommandDictionaryDataTests"`
Expected: PASS.

Run: `dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx`
Expected: PASS in full.

- [ ] **Step 6: Commit**

```bash
cd D:/SmartCodeGenerator-Repository
git add CodeGenerationEngine/Core/CommandDictionary.cs CodeGenerator/App/BuildAppCommand.cs CodeGenerator.Tests
git commit -m "feat(cli): document layout in help and warn on a windowless target"
```

---

### Task 15: End-to-end verification

Unit tests cannot see a broken XAML tree. This task builds and runs the real thing. Nothing here is committed except the notes.

**Files:**
- Create: `docs/superpowers/plans/2026-08-13-app-layout-verification.md` (the results)

**Interfaces:**
- Consumes: everything.
- Produces: evidence. No code.

- [ ] **Step 1: Rebuild the CLI**

`dotnet test` leaves the CLI's own `bin` stale, so a verified fix can look dead:

```bash
cd D:/SmartCodeGenerator-Repository
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release
```

- [ ] **Step 2: Generate all eight apps under a SHORT path**

`MAX_PATH` under the scratchpad produces MSB3030 "file not found" on a file that plainly exists. Use `D:\lt` (layout test):

```bash
mkdir -p /d/lt
```

Drive the CLI with piped stdin — `CommandLineEditor` falls back to `ReadLine` when redirected, and a piped run never prompts, so the layout must be named inline. For each of `tabs sidebar master-detail dashboard`, and for each target, run a `build app:` line naming the layout, then `approve` with the target. Record the exact lines used in the notes file.

- [ ] **Step 3: Build every generated app**

```bash
for d in /d/lt/*/; do echo "== $d"; dotnet build "$d" -c Debug 2>&1 | tail -3; done
```

Expected: `Build succeeded` for all eight. A XAML error surfaces here and nowhere earlier.

- [ ] **Step 4: Run the two that carry real interaction risk**

Launch the themed WPF **dashboard** app and confirm: cards render one per entity, each count matches the seeded row count, clicking a card shows that entity's grid, Home returns to the cards, and adding a row updates the card count on return.

Launch the WPF **master-detail** app and confirm: selecting a grid row fills the form, editing a field writes through to the grid, Id is read-only, and Save persists.

Automated UIA is not worth building for this: it reports `Header.ToString()` rather than rendered text and needs a real dispatcher loop.

- [ ] **Step 5: Prove default neutrality against `HEAD`**

Substring tests cannot see a CRLF regression. Use the PR #78 technique:

```bash
cd D:/SmartCodeGenerator-Repository
git worktree add D:/wt-head main
```

Write a .NET 10 file-based app (`#:project <tree>/AppScaffolding.csproj`) that dumps every emitted file for a fixed model with `AppCustomization.Default`, run it once per tree into two output folders, then:

```bash
diff -r /d/lt-neutral-head /d/lt-neutral-branch && echo "BYTE-IDENTICAL"
```

Expected: no differences at all. Then remove the worktree: `git worktree remove D:/wt-head`.

- [ ] **Step 6: Full Release suite with warnings as errors, the way CI runs it**

```bash
cd D:/SmartCodeGenerator-Repository
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release
```

`--no-incremental` matters: without it `dotnet build` prints "Build succeeded" without rerunning the analyzers.

- [ ] **Step 7: Write up and commit the evidence**

Record in `docs/superpowers/plans/2026-08-13-app-layout-verification.md`: the eight CLI lines used, the build results, what the two hand-run apps did, the `diff -r` result, and the Release test count.

```bash
cd D:/SmartCodeGenerator-Repository
git add docs/superpowers/plans/2026-08-13-app-layout-verification.md
git commit -m "docs: end-to-end verification of the layout feature"
```

- [ ] **Step 8: Clean up**

```bash
rm -rf /d/lt /d/lt-neutral-head /d/lt-neutral-branch
git worktree list   # confirm D:/wt-head is gone
```

---

## Self-Review

**Spec coverage.** Every section of the design has a task: the four layouts and full WinForms parity (Tasks 4–11), `AppLayout?` and the `Merge` exactness argument (Tasks 2, 12), `LayoutContext`/`IAppLayout`/the registries (Tasks 4, 5), the shared pane and its accepted duplication (Tasks 6, 9), `PropertyEditor` and the third `IsDisplayable` copy (Task 8), all five CLI surfaces (Tasks 12–14), the three error-handling cases (Tasks 12, 13, 14), and the six-part testing plan (Tasks 1–15, with the real builds, hand-runs and `diff -r` proof in Task 15).

**One spec correction, folded into Task 9.** The spec says WinForms "already factors its pane into a `BuildTab` method the new containers can call". `BuildTab` returns a `TabPage`, which a sidebar or dashboard cannot host, so the accurate statement is that its *body* is reusable but has to be re-emitted as a `Panel`-returning `BuildPane`. Task 9 says so, and Step 1 of that task tells the implementer to copy rather than paraphrase.

**One thing the spec assumed that the plan proves instead.** The spec asserts the new emitted C# can be Roslyn compile-tested. That was not true of the existing harness — the test project targets `net10.0`, so WPF and WinForms types are unreachable. Task 1 builds the harness that makes it true, using the WindowsDesktop reference pack, and its fourth test is the control that keeps the other three from being vacuous. This was verified by probe before the plan was written: 0 errors with the refs, 18 without.

**Placeholder scan.** One deliberate marker survives: `WinFormsPaneCode.BuildPaneMethod` in Task 9 Step 5 throws `NotImplementedException` with an instruction to copy the body verbatim from `WinFormsMainFormTemplate`. Restating ~80 lines of emitted control code here would invite a paraphrase, and a paraphrase is a behaviour change in the generated app. The `Compiles` test is what makes the copy verifiable rather than trusted. Every other step carries its actual content.

**Type consistency.** `AppLayout` / `AppCustomization.Layout` / `EffectiveLayout` (Task 2) are used under those names throughout. `AppLayoutCatalog.KeyFor`/`TryResolve`/`PromptOptions`/`HelpOptions`/`Descriptions`/`Keys` (Task 3) match every later call site. `LayoutContext(RootNamespace, ProjectName, Customization, Palette)` with `Themed`/`TitleAttribute`/`IconFileName` (Task 4) matches all eight layouts. `EntityPaneXaml.Resource(ThemePalette?, bool withEditor)` and its `EntityPane` key match Tasks 6, 7 and 8. `WinFormsPaneCode.BuildPaneMethod(ThemePalette?, bool withPropertyGrid)` matches Tasks 9, 10 and 11. `AppLayouts.Supports(string)` (Task 5) matches Tasks 13 and 14. `EmittedDesktopSource.AssertCompiles` and `ViewModelStubs.WinFormsHost` match every compile test that uses them.
