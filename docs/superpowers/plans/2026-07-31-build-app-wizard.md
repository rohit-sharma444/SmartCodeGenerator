# `build app` Wizard and Application Type Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every interactive `build app` enters one wizard that asks for a description first, then the application type from a six-item arrow-key menu, then data — and a new Windows Forms target makes that menu honest.

**Architecture:** A new `BuildAppWizard` in the CLI layer replaces the inline `DataSourceInterview` call at the top of `BuildAppCommand.Handle`, and hands the chosen target down to the review loop so `approve` no longer prompts for one. The menu is split into a pure `SelectionState` (testable) and a thin `SelectionPrompt` console driver, with a numbered fallback for redirected output. The WinForms target reuses the existing view models — they are portable apart from `RelayCommand`, which gets a second, `CommandManager`-free variant emitted under the same class name.

**Tech Stack:** C# / .NET 10, MSTest (`CodeGenerator.Tests`), xUnit (`DataIngestion.Tests`), Roslyn for emitted-source compilation, `dotnet build`/`dotnet test` for generated-project integration tests.

## Global Constraints

- **Repo root is the parent directory**, not the tool cwd: paths below are relative to `D:\SmartCodeGenerator-Repository`. The solution is `SmartCodeGenerator/SmartCodeGenerator.slnx`.
- **`CodeGenerator.Tests` is MSTest** (`[TestClass]`, `[TestMethod]`, `Assert.IsTrue`, `StringAssert.Contains`, `CollectionAssert`). `DataIngestion.Tests` is xUnit. Do not mix.
- **Redirected stdin must behave exactly as it does today.** No existing piped or E2E test may be edited to accommodate the wizard. If one needs changing, the interactive-only boundary has leaked — stop and reconsider.
- **Every assertion goes red before it goes green.** Run the new test and see it fail for the stated reason first. An inverted `Assert.DoesNotContain`, or asserting a name the fixture itself declares, passes green while testing nothing.
- **Emitted C# is proved by compiling it**, never by substring alone. Substring tests are for wiring and intent; a real `dotnet build` is what proves the generated app is valid.
- **Generated projects go under `Path.GetTempPath()`**, never the scratchpad: the scratchpad path plus a generated project's nesting exceeds MAX_PATH, and MSBuild reports that as MSB3030 "file not found" on a file that is plainly there.
- **Final verification is the whole solution in Release with `-warnaserror`.** The `CodeGenerator`-only Debug build hides analyzer errors and runs one of the four test projects.
- **`docs/commands.md` is generated** from `CommandDictionary` and audited by `CommandReferenceDocTests`. Regenerate with `UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests`; never hand-edit it.
- **Target keys are stable**: `wpf`, `console`, `webapi`, `classlib`, `worker`, plus the new `winforms`. Menu labels are display-only.
- **`ITargetScaffolder` implementations are `public sealed class` in namespace `AppScaffolding.Targets`**; templates are `public static class` in namespace `AppScaffolding`.

---

## File Structure

**Created:**

| File | Responsibility |
| ---- | -------------- |
| `AppScaffolding/Targets/DesktopViewModelFiles.cs` | The view-model file set both desktop targets share, parameterized by which `RelayCommand` to emit |
| `AppScaffolding/Targets/WinFormsTarget.cs` | The WinForms target's metadata and file list |
| `AppScaffolding/WinFormsMainFormTemplate.cs` | Source of `Views/MainForm.cs` |
| `AppScaffolding/WinFormsProgramTemplate.cs` | Source of the WinForms `Program.cs` |
| `AppScaffolding/WinFormsProjectFixup.cs` | Deletes the `dotnet new winforms` template's `Form1` files |
| `CodeGenerator/App/SelectionState.cs` | Pure menu state: index, movement, lines to paint |
| `CodeGenerator/App/SelectionPrompt.cs` | Console driver for `SelectionState` (raw keys, repaint) |
| `CodeGenerator/App/ApplicationTypeMenu.cs` | The label→key table, index resolution, numbered fallback |
| `CodeGenerator/App/BuildAppWizard.cs` | The three-step wizard and its outcome record |

**Modified:**

| File | Change |
| ---- | ------ |
| `AppScaffolding/ViewModelTemplates.cs` | Add `PortableRelayCommand` |
| `AppScaffolding/Targets/WpfTarget.cs` | Use `DesktopViewModelFiles` |
| `AppScaffolding/Targets/TargetScaffolders.cs` | Register `WinFormsTarget` |
| `CodeGenerator/App/BuildAppCommand.cs` | Call the wizard; thread the target into the review loop; `approve sqlite`; widen `IsPersistenceAllowed` |
| `CodeGenerationEngine/Core/CommandDictionary.cs` | Help entries for the wizard, `winforms`, `approve sqlite` |
| `docs/commands.md` | Regenerated |
| `CodeGenerator/README.md`, `AppScaffolding/README.md` | Target list and wizard flow |

---

## Task 1: A portable RelayCommand template

`ViewModelTemplates.RelayCommand` routes `CanExecuteChanged` through `System.Windows.Input.CommandManager`, which lives in PresentationCore and exists only in a WPF project. A WinForms app referencing it does not compile. The WPF version stays exactly as it is — its comment explains that `CommandManager` is what re-enables Delete on selection change, and changing it would need a real dispatcher loop to re-verify. WinForms gets a second variant under the same class name, so `EntityViewModel` compiles against either.

**Files:**
- Modify: `AppScaffolding/ViewModelTemplates.cs`
- Test: `CodeGenerator.Tests/App/ViewModelTemplatesTests.cs`

**Interfaces:**
- Produces: `public static string PortableRelayCommand(string appName)` on `AppScaffolding.ViewModelTemplates` — emits a `public sealed class RelayCommand : ICommand` in `<appName>.ViewModels` with a plain `CanExecuteChanged` event and a public `RaiseCanExecuteChanged()`, and no `CommandManager` reference.

- [ ] **Step 1: Write the failing tests**

Append to `CodeGenerator.Tests/App/ViewModelTemplatesTests.cs`, inside the class:

```csharp
    [TestMethod]
    public void PortableRelayCommand_DoesNotDependOnWpf()
    {
        var source = ViewModelTemplates.PortableRelayCommand("Payroll");

        // CommandManager lives in PresentationCore. A WinForms project has no reference to it,
        // so its presence here is a compile error in the generated app, not a style question.
        Assert.IsFalse(
            source.Contains("CommandManager", StringComparison.Ordinal),
            "the portable RelayCommand must not reference CommandManager");
        Assert.IsFalse(
            source.Contains("using System.Windows.Input;\nusing System.Windows.Controls;",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public void PortableRelayCommand_IsTheSameClassUnderTheSameNamespace()
    {
        var source = ViewModelTemplates.PortableRelayCommand("Payroll");

        // EntityViewModel is shared between both desktop targets and says `new RelayCommand(...)`,
        // so the type it binds to must have the same name and namespace in either variant.
        StringAssert.Contains(source, "namespace Payroll.ViewModels;");
        StringAssert.Contains(source, "public sealed class RelayCommand : ICommand");
        StringAssert.Contains(source, "public bool CanExecute(object? parameter)");
        StringAssert.Contains(source, "public void Execute(object? parameter)");
    }

    [TestMethod]
    public void PortableRelayCommand_LetsTheCallerRaiseCanExecuteChanged()
    {
        var source = ViewModelTemplates.PortableRelayCommand("Payroll");

        // With no CommandManager there is no global requery, so the event has to be raisable —
        // otherwise it is a dead member and nothing can ever re-evaluate CanExecute.
        StringAssert.Contains(source, "public void RaiseCanExecuteChanged()");
        StringAssert.Contains(source, "CanExecuteChanged?.Invoke(this, EventArgs.Empty)");
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd /d/SmartCodeGenerator-Repository
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~PortableRelayCommand"
```

Expected: compile error — `'ViewModelTemplates' does not contain a definition for 'PortableRelayCommand'`.

- [ ] **Step 3: Add the template**

In `AppScaffolding/ViewModelTemplates.cs`, after the existing `RelayCommand` method:

```csharp
    /// <summary>
    /// Source of <c>ViewModels/RelayCommand.cs</c> for a target with no WPF reference.
    /// </summary>
    /// <remarks>
    /// Same class, same namespace, same members as <see cref="RelayCommand"/> — deliberately, so
    /// the shared <c>EntityViewModel</c> compiles against either. The difference is
    /// <c>CanExecuteChanged</c>: the WPF variant routes it through
    /// <c>CommandManager.RequerySuggested</c>, which lives in PresentationCore and does not exist
    /// in a WinForms project. Here it is a plain event the view raises for itself.
    /// </remarks>
    public static string PortableRelayCommand(string appName) => $$"""
using System;
using System.Windows.Input;

namespace {{appName}}.ViewModels;

/// <summary>An ICommand over a delegate pair.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    // No CommandManager outside WPF, so there is no global requery: the view raises this when
    // something it knows about changes (a grid selection, most often).
    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();
}
""";
```

`System.Windows.Input` stays: `ICommand` lives in `System.ObjectModel`, part of the shared framework, and is available to any .NET app.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~RelayCommand"
```

Expected: PASS, including the pre-existing `RelayCommand_RequeriesOnUiActivity` — the WPF variant is untouched.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/ViewModelTemplates.cs CodeGenerator.Tests/App/ViewModelTemplatesTests.cs
git commit -m "feat(scaffold): emit a RelayCommand that does not need WPF"
```

---

## Task 2: Share the desktop view-model file set

`WpfTarget.FrontEndFiles` lists six files that are not WPF-specific. Extract them so WinForms includes the same set, differing only in which `RelayCommand` is emitted.

**Files:**
- Create: `AppScaffolding/Targets/DesktopViewModelFiles.cs`
- Modify: `AppScaffolding/Targets/WpfTarget.cs`
- Test: `CodeGenerator.Tests/App/DesktopViewModelFilesTests.cs`

**Interfaces:**
- Consumes: `ViewModelTemplates.PortableRelayCommand` (Task 1).
- Produces: `public static List<GeneratedFile> DesktopViewModelFiles.Build(string projectName, IReadOnlyList<string> entityNames, string relayCommandSource)` — **public**, not internal: `AppScaffolding.csproj` has no `InternalsVisibleTo`, so an internal type here is invisible to `CodeGenerator.Tests`, and everything else in this assembly is public anyway. Returns a mutable `List` in this order: `Data/RelationshipResolver.cs`, `ViewModels/ObservableObject.cs`, `ViewModels/RelayCommand.cs`, `ViewModels/IEntityViewModel.cs`, `ViewModels/EntityViewModel.cs`, `ViewModels/MainViewModel.cs`. Callers append their own views to it.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/DesktopViewModelFilesTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

[TestClass]
public class DesktopViewModelFilesTests
{
    [TestMethod]
    public void Build_EmitsTheSixSharedFiles()
    {
        var files = DesktopViewModelFiles.Build(
            "Payroll", new[] { "Employee" }, ViewModelTemplates.RelayCommand("Payroll"));

        var names = files.Select(f => f.RelativeFolder + "/" + f.Name).ToList();

        CollectionAssert.AreEqual(
            new[]
            {
                "Data/RelationshipResolver.cs",
                "ViewModels/ObservableObject.cs",
                "ViewModels/RelayCommand.cs",
                "ViewModels/IEntityViewModel.cs",
                "ViewModels/EntityViewModel.cs",
                "ViewModels/MainViewModel.cs",
            },
            names);
    }

    [TestMethod]
    public void Build_EmitsTheRelayCommandItWasGiven()
    {
        var files = DesktopViewModelFiles.Build(
            "Payroll", new[] { "Employee" }, ViewModelTemplates.PortableRelayCommand("Payroll"));

        var relay = files.Single(f => f.Name == "RelayCommand.cs").Source;

        // The whole point of the parameter: WinForms must not receive the WPF variant.
        StringAssert.Contains(relay, "public void RaiseCanExecuteChanged()");
        Assert.IsFalse(relay.Contains("CommandManager", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Build_ReturnsAListTheCallerCanAppendViewsTo()
    {
        var files = DesktopViewModelFiles.Build(
            "Payroll", new[] { "Employee" }, ViewModelTemplates.RelayCommand("Payroll"));

        files.Add(new GeneratedFile("Views", "Anything.cs", "// x"));

        Assert.HasCount(7, files);
    }

    [TestMethod]
    public void Build_RegistersEveryEntityWithTheMainViewModel()
    {
        var files = DesktopViewModelFiles.Build(
            "Payroll", new[] { "Employee", "Department" },
            ViewModelTemplates.RelayCommand("Payroll"));

        var mainVm = files.Single(f => f.Name == "MainViewModel.cs").Source;

        StringAssert.Contains(mainVm, "new EntityViewModel<Employee>(\"Employee\"),");
        StringAssert.Contains(mainVm, "new EntityViewModel<Department>(\"Department\"),");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~DesktopViewModelFilesTests"
```

Expected: compile error — `The name 'DesktopViewModelFiles' does not exist`.

- [ ] **Step 3: Create the shared set**

Create `AppScaffolding/Targets/DesktopViewModelFiles.cs`:

```csharp
namespace AppScaffolding.Targets;

/// <summary>
/// The view-model files both desktop targets emit. Nothing here is WPF-specific: these types use
/// only <c>System.ComponentModel</c> and <c>System.Windows.Input.ICommand</c>, which every .NET
/// app has, and <c>RelationshipResolver</c> is pure reflection over <c>IEntityViewModel</c>.
/// </summary>
/// <remarks>
/// <paramref name="relayCommandSource"/> is the one thing that genuinely differs: WPF's
/// <c>RelayCommand</c> requeries through <c>CommandManager</c> (PresentationCore), WinForms' does
/// not. Both emit the same class into the same file, so <c>EntityViewModel</c> is shared unchanged.
/// </remarks>
public static class DesktopViewModelFiles
{
    /// <summary>A fresh mutable list, so a target can append its own views to it.</summary>
    public static List<GeneratedFile> Build(
        string projectName, IReadOnlyList<string> entityNames, string relayCommandSource) =>
    [
        new("Data", "RelationshipResolver.cs", RelationshipResolverTemplate.Source(projectName)),
        new("ViewModels", "ObservableObject.cs", ViewModelTemplates.ObservableObject(projectName)),
        new("ViewModels", "RelayCommand.cs", relayCommandSource),
        new("ViewModels", "IEntityViewModel.cs", ViewModelTemplates.EntityViewModelInterface(projectName)),
        new("ViewModels", "EntityViewModel.cs", ViewModelTemplates.EntityViewModel(projectName)),
        new("ViewModels", "MainViewModel.cs", MainViewModelTemplate.Build(projectName, entityNames)),
    ];
}
```

- [ ] **Step 4: Point WpfTarget at it**

Replace the `FrontEndFiles` method body in `AppScaffolding/Targets/WpfTarget.cs`:

```csharp
    public IReadOnlyList<GeneratedFile> FrontEndFiles(string projectName, IReadOnlyList<string> entityNames)
    {
        // Shared with WinFormsTarget. The WPF variant of RelayCommand requeries through
        // CommandManager, which is what re-enables Delete when the grid selection changes.
        var files = DesktopViewModelFiles.Build(
            projectName, entityNames, ViewModelTemplates.RelayCommand(projectName));

        files.Add(new("Views", "MainWindow.xaml", MainWindowTemplate.Xaml(projectName)));
        files.Add(new("Views", "MainWindow.xaml.cs", MainWindowTemplate.CodeBehind(projectName)));

        return files;
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~DesktopViewModelFilesTests|FullyQualifiedName~WpfTargetTests"
```

Expected: PASS. `WpfTargetTests.FrontEndFiles_EmitsTheRelationshipResolverViewModelsAndViews` must pass **without being edited** — it is the regression guard proving the refactor emits the same set.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/Targets/DesktopViewModelFiles.cs AppScaffolding/Targets/WpfTarget.cs \
        CodeGenerator.Tests/App/DesktopViewModelFilesTests.cs
git commit -m "refactor(scaffold): share the desktop view-model file set"
```

---

## Task 3: WinForms project fixup

`dotnet new winforms` writes its own `Form1`. Left in place it is dead code that still compiles into the app; the equivalent for WPF (`MainWindow.xaml`) actually breaks the build, and this mirrors that cleanup.

**Files:**
- Create: `AppScaffolding/WinFormsProjectFixup.cs`
- Test: `CodeGenerator.Tests/App/WinFormsProjectFixupTests.cs`

**Interfaces:**
- Produces: `public static void WinFormsProjectFixup.Apply(string projectFolder)` — best-effort and idempotent; deletes `Form1.cs`, `Form1.Designer.cs`, `Form1.resx` if present.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/WinFormsProjectFixupTests.cs`:

```csharp
using System.IO;
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WinFormsProjectFixupTests
{
    private static string NewProjectFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wffixup_" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Form1.cs"), "// template");
        File.WriteAllText(Path.Combine(folder, "Form1.Designer.cs"), "// template");
        File.WriteAllText(Path.Combine(folder, "Program.cs"), "// ours, overwritten already");
        return folder;
    }

    [TestMethod]
    public void Apply_DeletesTheTemplatesForm()
    {
        var folder = NewProjectFolder();
        try
        {
            WinFormsProjectFixup.Apply(folder);

            Assert.IsFalse(File.Exists(Path.Combine(folder, "Form1.cs")));
            Assert.IsFalse(File.Exists(Path.Combine(folder, "Form1.Designer.cs")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void Apply_LeavesOurOwnFilesAlone()
    {
        var folder = NewProjectFolder();
        try
        {
            WinFormsProjectFixup.Apply(folder);

            // Program.cs is ours (it overwrote the template's), so the fixup must not touch it.
            Assert.IsTrue(File.Exists(Path.Combine(folder, "Program.cs")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void Apply_IsIdempotentAndSurvivesAMissingForm()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wffixup_" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        try
        {
            // Newer SDK templates may not emit Form1.resx at all. A fixup that throws on a file
            // that was never there would fail every generation.
            WinFormsProjectFixup.Apply(folder);
            WinFormsProjectFixup.Apply(folder);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~WinFormsProjectFixupTests"
```

Expected: compile error — `The name 'WinFormsProjectFixup' does not exist`.

- [ ] **Step 3: Create the fixup**

Create `AppScaffolding/WinFormsProjectFixup.cs`:

```csharp
namespace AppScaffolding;

/// <summary>
/// Reconciles the files `dotnet new winforms` emits with the foldered layout this scaffolder
/// writes. Runs only against a project this scaffolder just created.
/// </summary>
/// <remarks>
/// Unlike the WPF case, the template's form does not break the build — no duplicated x:Class —
/// so this is about not shipping a dead Form1 alongside the real Views/MainForm. Program.cs needs
/// no repointing: ours overwrote the template's, the way ConsoleTarget and WorkerTarget do.
/// </remarks>
public static class WinFormsProjectFixup
{
    // Form1.resx is listed because older SDK templates emit one; newer ones may not, and
    // deleting a file that was never there has to be a no-op.
    private static readonly string[] TemplateFiles =
        ["Form1.cs", "Form1.Designer.cs", "Form1.resx"];

    /// <summary>Deletes the template's own form. Best-effort and idempotent.</summary>
    public static void Apply(string projectFolder)
    {
        foreach (var name in TemplateFiles)
            Delete(Path.Combine(projectFolder, name));
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leave it; the build error that follows is clearer than a crash here.
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~WinFormsProjectFixupTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/WinFormsProjectFixup.cs CodeGenerator.Tests/App/WinFormsProjectFixupTests.cs
git commit -m "feat(scaffold): remove the winforms template's own form"
```

---

## Task 4: The WinForms form and entry point templates

The view. A `TabControl` with one tab per `IEntityViewModel`, each a `DataGridView` plus Add/Delete/Save and a status label — the WPF window's shape, ported.

**Files:**
- Create: `AppScaffolding/WinFormsMainFormTemplate.cs`
- Create: `AppScaffolding/WinFormsProgramTemplate.cs`
- Test: `CodeGenerator.Tests/App/WinFormsTemplateTests.cs`

**Interfaces:**
- Produces: `public static string WinFormsMainFormTemplate.Source(string appName)` — source of `Views/MainForm.cs`, declaring `public sealed class MainForm : Form` in `<appName>.Views`.
- Produces: `public static string WinFormsProgramTemplate.Source(string appName)` — source of `Program.cs`, declaring `internal static class Program` in `<appName>` with an `[STAThread] Main`.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/App/WinFormsTemplateTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WinFormsTemplateTests
{
    [TestMethod]
    public void MainForm_LivesInTheAppsViewsNamespace()
    {
        var source = WinFormsMainFormTemplate.Source("Payroll");

        StringAssert.Contains(source, "namespace Payroll.Views;");
        StringAssert.Contains(source, "public sealed class MainForm : Form");
        StringAssert.Contains(source, "using Payroll.ViewModels;");
    }

    [TestMethod]
    public void MainForm_NamesNoEntity()
    {
        // The same property the WPF view has: one tab per IEntityViewModel, so the view is
        // identical whatever the discovered model turned out to be.
        var source = WinFormsMainFormTemplate.Source("Payroll");

        StringAssert.Contains(source, "foreach (var entity in _model.Entities)");
        StringAssert.Contains(source, "new TabPage(entity.Title)");
    }

    [TestMethod]
    public void MainForm_RepublishesTheCollectionWhenItChanges()
    {
        var source = WinFormsMainFormTemplate.Source("Payroll");

        // DataGridView ignores INotifyCollectionChanged. Without this, Add puts a record in the
        // view model and nothing appears in the grid.
        StringAssert.Contains(source, "INotifyCollectionChanged");
        StringAssert.Contains(source, "binding.ResetBindings(false)");
    }

    [TestMethod]
    public void MainForm_WiresEveryCommandAndTheStatus()
    {
        var source = WinFormsMainFormTemplate.Source("Payroll");

        StringAssert.Contains(source, "entity.AddCommand.Execute(null)");
        StringAssert.Contains(source, "entity.DeleteCommand.Execute(null)");
        StringAssert.Contains(source, "entity.SaveCommand.Execute(null)");
        StringAssert.Contains(source, "status.Text = entity.Status");
    }

    [TestMethod]
    public void MainForm_EnablesDeleteFromTheSelection()
    {
        var source = WinFormsMainFormTemplate.Source("Payroll");

        // There is no CommandManager here, so the button's Enabled state is the view's job.
        StringAssert.Contains(source, "entity.Selected = grid.CurrentRow?.DataBoundItem");
        StringAssert.Contains(source, "delete.Enabled = entity.DeleteCommand.CanExecute(null)");
    }

    [TestMethod]
    public void MainForm_KeepsIdReadOnlyAndDropsNavigationColumns()
    {
        var source = WinFormsMainFormTemplate.Source("Payroll");

        StringAssert.Contains(source, "column.ReadOnly = true;");
        StringAssert.Contains(source, "grid.Columns.RemoveAt(i);");
        StringAssert.Contains(source, "DataGridViewComboBoxColumn");
    }

    [TestMethod]
    public void Program_RunsTheMainFormOnAnStaThread()
    {
        var source = WinFormsProgramTemplate.Source("Payroll");

        StringAssert.Contains(source, "namespace Payroll;");
        StringAssert.Contains(source, "[STAThread]");
        StringAssert.Contains(source, "Application.Run(new MainForm())");
        StringAssert.Contains(source, "using Payroll.Views;");

        // ApplicationConfiguration.Initialize() is source-generated and only exists when the
        // csproj opts in. The explicit pair always compiles.
        StringAssert.Contains(source, "Application.EnableVisualStyles()");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~WinFormsTemplateTests"
```

Expected: compile error — `The name 'WinFormsMainFormTemplate' does not exist`.

- [ ] **Step 3: Create the form template**

Create `AppScaffolding/WinFormsMainFormTemplate.cs`:

```csharp
namespace AppScaffolding;

/// <summary>
/// The WinForms view. Entirely static, like its WPF counterpart: a TabControl over
/// IEntityViewModel renders any entity set, so no entity name ever appears here.
/// </summary>
public static class WinFormsMainFormTemplate
{
    /// <summary>Source of <c>Views/MainForm.cs</c>.</summary>
    public static string Source(string appName) => $$"""
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using {{appName}}.ViewModels;

namespace {{appName}}.Views;

/// <summary>One tab per discovered entity, over the shared view models.</summary>
public sealed class MainForm : Form
{
    private readonly MainViewModel _model = new();

    public MainForm()
    {
        Text = "{{appName}}";
        Width = 900;
        Height = 600;

        var tabs = new TabControl { Dock = DockStyle.Fill };

        foreach (var entity in _model.Entities)
            tabs.TabPages.Add(BuildTab(entity, _model));

        Controls.Add(tabs);
    }

    // A TableLayoutPanel rather than three docked controls: docking several controls to the same
    // edge depends on z-order, which is easy to get subtly wrong, and explicit rows cannot.
    private static TabPage BuildTab(IEntityViewModel entity, MainViewModel model)
    {
        var page = new TabPage(entity.Title);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };

        // DataGridView does not listen to INotifyCollectionChanged, so the collection is
        // republished through a BindingSource whenever the view model adds or removes a record.
        // Cell edits need no help: the bound objects are the view model's own instances.
        var binding = new BindingSource { DataSource = entity.Items };
        grid.DataSource = binding;

        if (entity.Items is INotifyCollectionChanged observable)
            observable.CollectionChanged += (_, _) => binding.ResetBindings(false);

        // Columns are generated when the binding completes, and ResetBindings raises it again.
        // The rules are applied once: re-running them would rebuild the combo columns on
        // every add.
        var columnsAdjusted = false;
        grid.DataBindingComplete += (_, _) =>
        {
            if (columnsAdjusted)
                return;

            columnsAdjusted = true;
            ApplyColumnRules(grid, model);
        };

        var add = new Button { Text = "Add", Width = 80 };
        var delete = new Button { Text = "Delete", Width = 80, Enabled = false };
        var save = new Button { Text = "Save", Width = 80 };

        // Right-to-left flow, so adding Save first puts it at the right-hand edge and the row
        // reads Add / Delete / Save, as the WPF window does.
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(delete);
        buttons.Controls.Add(add);

        var status = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = entity.Status,
        };

        // No CommandManager outside WPF, so re-evaluating CanExecute is the view's job.
        grid.SelectionChanged += (_, _) =>
        {
            entity.Selected = grid.CurrentRow?.DataBoundItem;
            delete.Enabled = entity.DeleteCommand.CanExecute(null);
        };

        add.Click += (_, _) => entity.AddCommand.Execute(null);
        delete.Click += (_, _) => entity.DeleteCommand.Execute(null);
        save.Click += (_, _) => entity.SaveCommand.Execute(null);

        // Status is the only thing on the page that changes without a click of its own.
        if (entity is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == "Status")
                    status.Text = entity.Status;
            };
        }

        layout.Controls.Add(grid, 0, 0);
        layout.Controls.Add(buttons, 0, 1);
        layout.Controls.Add(status, 0, 2);

        page.Controls.Add(layout);
        return page;
    }

    // The same two rules the WPF code-behind applies while generating columns. DataGridView has
    // no per-column event, so the generated set is adjusted afterwards instead.
    private static void ApplyColumnRules(DataGridView grid, MainViewModel model)
    {
        var itemType = ItemType(grid);

        if (itemType is null)
            return;

        for (var i = grid.Columns.Count - 1; i >= 0; i--)
        {
            var column = grid.Columns[i];
            var property = itemType.GetProperty(column.DataPropertyName);

            if (property is null)
                continue;

            // Id is generated, so it is shown but never edited.
            if (string.Equals(column.DataPropertyName, "Id", StringComparison.OrdinalIgnoreCase))
            {
                column.ReadOnly = true;
                continue;
            }

            // A foreign key is shown as a combo box over the related entity's rows, so a user
            // picks a Customer rather than typing its id.
            if (TryReplaceWithRelationshipColumn(grid, i, itemType, model))
                continue;

            // A navigation property has no sensible column: it would render as its type name, or
            // as "System.Collections.Generic.List`1[Payment]". The foreign key beside it is the
            // editable, meaningful one.
            if (!IsDisplayable(property.PropertyType))
                grid.Columns.RemoveAt(i);
        }
    }

    private static bool TryReplaceWithRelationshipColumn(
        DataGridView grid, int index, Type itemType, MainViewModel model)
    {
        var name = grid.Columns[index].DataPropertyName;

        if (name.Length <= 2 || !name.EndsWith("Id", StringComparison.Ordinal))
            return false;

        if (itemType.GetProperty(name)?.PropertyType != typeof(int))
            return false;

        var navName = name.Substring(0, name.Length - 2);
        var nav = itemType.GetProperty(navName);

        if (nav is null || !IsEntityType(nav.PropertyType))
            return false;

        var tab = model.Entities.FirstOrDefault(vm => vm.Title == navName);

        if (tab is null)
            return false;

        var combo = new DataGridViewComboBoxColumn
        {
            HeaderText = navName,
            DataPropertyName = name,
            DataSource = tab.Items,
            ValueMember = "Id",
            DisplayMember = DisplayMember(nav.PropertyType),
        };

        grid.Columns.RemoveAt(index);
        grid.Columns.Insert(index, combo);
        return true;
    }

    // ObservableCollection<T> arrives here as the BindingSource's DataSource.
    private static Type? ItemType(DataGridView grid)
    {
        var source = (grid.DataSource as BindingSource)?.DataSource?.GetType();

        return source is not null && source.IsGenericType
            ? source.GetGenericArguments()[0]
            : null;
    }

    // An entity type is one of the app's own model classes: a reference type that is neither a
    // string nor a generic collection. (Navigation collections are dropped by IsDisplayable.)
    private static bool IsEntityType(Type type) =>
        type.IsClass && type != typeof(string) && !type.IsGenericType;

    // The same display convention the console picker uses: a Name or Title, else the first
    // string field, else the Id.
    private static string DisplayMember(Type type)
    {
        var display = type.GetProperty("Name") ?? type.GetProperty("Title")
            ?? type.GetProperties().FirstOrDefault(p => p.PropertyType == typeof(string));

        return display?.Name ?? "Id";
    }

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

- [ ] **Step 4: Create the entry-point template**

Create `AppScaffolding/WinFormsProgramTemplate.cs`:

```csharp
namespace AppScaffolding;

/// <summary>The WinForms entry point. Overwrites the template's own Program.cs.</summary>
public static class WinFormsProgramTemplate
{
    /// <summary>Source of <c>Program.cs</c>.</summary>
    public static string Source(string appName) => $$"""
using System;
using System.Windows.Forms;
using {{appName}}.Views;

namespace {{appName}};

internal static class Program
{
    // EnableVisualStyles + SetCompatibleTextRenderingDefault rather than the generated
    // ApplicationConfiguration.Initialize(): that class only exists when the csproj opts into the
    // WinForms source generator, and this pair compiles in any WinForms project.
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
""";
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~WinFormsTemplateTests"
```

Expected: PASS. These are substring tests — Task 7 is what proves the source compiles.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/WinFormsMainFormTemplate.cs AppScaffolding/WinFormsProgramTemplate.cs \
        CodeGenerator.Tests/App/WinFormsTemplateTests.cs
git commit -m "feat(scaffold): emit a WinForms CRUD form over the shared view models"
```

---

## Task 5: The WinForms target and its registration

**Files:**
- Create: `AppScaffolding/Targets/WinFormsTarget.cs`
- Modify: `AppScaffolding/Targets/TargetScaffolders.cs`
- Test: `CodeGenerator.Tests/App/WinFormsTargetTests.cs`
- Test: `CodeGenerator.Tests/App/TargetScaffoldersTests.cs`

**Interfaces:**
- Consumes: `DesktopViewModelFiles.Build` (Task 2), `WinFormsMainFormTemplate.Source` / `WinFormsProgramTemplate.Source` (Task 4), `WinFormsProjectFixup.Apply` (Task 3).
- Produces: `public sealed class WinFormsTarget : ITargetScaffolder` with `Key => "winforms"`, `FolderSuffix => ".WinForms"`, `ProjectType => DotNetProjectType.WinForms`. Registered **last** in `TargetScaffolders.All`, so `Default` stays `WpfTarget` and existing key order is otherwise unchanged.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/App/WinFormsTargetTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WinFormsTargetTests
{
    [TestMethod]
    public void Metadata_DescribesAWindowsFormsApp()
    {
        var target = new WinFormsTarget();

        Assert.AreEqual("winforms", target.Key);
        Assert.AreEqual(".WinForms", target.FolderSuffix);
        Assert.AreEqual(DotNetProjectType.WinForms, target.ProjectType);
        StringAssert.Contains(target.Description, "Windows Forms");
    }

    [TestMethod]
    public void FrontEndFiles_EmitsTheSharedViewModelsPlusTheFormAndEntryPoint()
    {
        var files = new WinFormsTarget().FrontEndFiles("Payroll", new[] { "Employee" });
        var names = files.Select(f => f.RelativeFolder + "/" + f.Name).ToList();

        CollectionAssert.Contains(names, "Data/RelationshipResolver.cs");
        CollectionAssert.Contains(names, "ViewModels/EntityViewModel.cs");
        CollectionAssert.Contains(names, "ViewModels/MainViewModel.cs");
        CollectionAssert.Contains(names, "Views/MainForm.cs");
        CollectionAssert.Contains(names, "/Program.cs");

        // No XAML: this is the whole difference from WpfTarget's file list.
        Assert.IsFalse(names.Any(n => n.EndsWith(".xaml", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void FrontEndFiles_EmitsTheRelayCommandThatDoesNotNeedWpf()
    {
        var files = new WinFormsTarget().FrontEndFiles("Payroll", new[] { "Employee" });
        var relay = files.Single(f => f.Name == "RelayCommand.cs").Source;

        // CommandManager is in PresentationCore; a WinForms project has no reference to it.
        Assert.IsFalse(relay.Contains("CommandManager", StringComparison.Ordinal));
    }
}
```

Then update `CodeGenerator.Tests/App/TargetScaffoldersTests.cs` — replace the `Keys_AreTheFiveTargetsInOrder` method with:

```csharp
    [TestMethod]
    public void Keys_AreTheSixTargetsInOrder()
    {
        // winforms is appended rather than inserted, so Default stays wpf and no existing key
        // changes position. The wizard's menu supplies its own display order.
        CollectionAssert.AreEqual(
            new[] { "wpf", "console", "webapi", "classlib", "worker", "winforms" },
            TargetScaffolders.Keys.ToList());
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~WinFormsTargetTests|FullyQualifiedName~TargetScaffoldersTests"
```

Expected: compile error — `The name 'WinFormsTarget' does not exist`.

- [ ] **Step 3: Create the target**

Create `AppScaffolding/Targets/WinFormsTarget.cs`:

```csharp
using CodeGenerationEngine.Models;

namespace AppScaffolding.Targets;

/// <summary>A runnable Windows Forms CRUD app over the discovered model.</summary>
/// <remarks>
/// Shares its whole presentation layer with <see cref="WpfTarget"/> — see
/// <see cref="DesktopViewModelFiles"/>. Only the view and the RelayCommand variant differ.
/// </remarks>
public sealed class WinFormsTarget : ITargetScaffolder
{
    public string Key => "winforms";
    public string Description => "a runnable Windows Forms CRUD app";
    public DotNetProjectType ProjectType => DotNetProjectType.WinForms;
    public string FolderSuffix => ".WinForms";

    public IReadOnlyList<GeneratedFile> FrontEndFiles(
        string projectName, IReadOnlyList<string> entityNames)
    {
        var files = DesktopViewModelFiles.Build(
            projectName, entityNames, ViewModelTemplates.PortableRelayCommand(projectName));

        files.Add(new("Views", "MainForm.cs", WinFormsMainFormTemplate.Source(projectName)));
        files.Add(new("", "Program.cs", WinFormsProgramTemplate.Source(projectName)));

        return files;
    }

    public void FixupProject(string outputFolder) => WinFormsProjectFixup.Apply(outputFolder);
}
```

- [ ] **Step 4: Register it**

In `AppScaffolding/Targets/TargetScaffolders.cs`, add to the end of the `All` initializer:

```csharp
    private static readonly ITargetScaffolder[] All =
    {
        new WpfTarget(),
        new ConsoleTarget(),
        new WebApiTarget(),
        new ClassLibraryTarget(),
        new WorkerTarget(),
        // Appended, not inserted: Default is All[0] and every existing key keeps its position.
        new WinFormsTarget(),
    };
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~WinFormsTargetTests|FullyQualifiedName~TargetScaffoldersTests"
```

Expected: PASS, including `Default_IsWpf`.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/Targets/WinFormsTarget.cs AppScaffolding/Targets/TargetScaffolders.cs \
        CodeGenerator.Tests/App/WinFormsTargetTests.cs CodeGenerator.Tests/App/TargetScaffoldersTests.cs
git commit -m "feat(scaffold): add the winforms target"
```

---

## Task 6: Prove the generated WinForms app compiles

Substring tests cannot see what matters in emitted code. A missing `where T : class, new()` is CS0452; a WPF type in a WinForms project is CS0246; neither is visible to `StringAssert`.

**Files:**
- Modify: `CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs`

**Interfaces:**
- Consumes: the `winforms` key registered in Task 5.

- [ ] **Step 1: Add the failing case**

In `CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs`, add a `DataRow` to `GeneratedProject_Builds` and widen the Windows guard:

```csharp
    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("console")]
    [DataRow("webapi")]
    [DataRow("classlib")]
    [DataRow("worker")]
    [DataRow("wpf")]
    [DataRow("winforms")]
    public void GeneratedProject_Builds(string targetKey)
    {
        // wpf is TargetScaffolders.Default -- what `build app:` ships unless told otherwise -- and
        // EntityViewModel<T> is the one place that resolves a validator outside Repository, under a
        // `where T : class, new()` constraint. A missing constraint is CS0452, which no substring
        // assertion can see, so the default target has to be compiled like the rest. winforms
        // shares those same view models, and additionally proves its RelayCommand variant really
        // is free of PresentationCore.
        if ((targetKey == "wpf" || targetKey == "winforms") && !OperatingSystem.IsWindows())
            Assert.Inconclusive($"The {targetKey} target needs the Windows desktop targeting pack.");
```

Leave the rest of the method body unchanged. Do **not** add `winforms` to `GeneratedTestProject_BuildsAndPasses` — `wpf` is excluded there too, and matching the existing desktop treatment keeps this task to one new claim.

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~GeneratedProject_Builds"
```

Expected: the five existing rows PASS and `winforms` FAILS. Read the `dotnet build` output in the failure message before fixing anything — the likely first failures are a WPF type reaching the WinForms compilation or a `Form1` conflict, and each has a different fix.

- [ ] **Step 3: Fix whatever the compiler reports**

Work from the compiler's own diagnostics, in `WinFormsMainFormTemplate`, `WinFormsProgramTemplate`, `WinFormsProjectFixup` or `WinFormsTarget`. Do not change `WpfTarget`, `ViewModelTemplates.RelayCommand`, `EntityViewModel` or `MainViewModelTemplate` — if a fix seems to need one of those, the shared-view-model decision is in question and that is worth raising rather than working around.

Known things to check first:
- `dotnet new winforms` sets `<UseWindowsForms>true</UseWindowsForms>` and a `-windows` TFM. If `DotNetProjectFactory` does not pass `winforms` through as expected, that is the bug rather than the template.
- If `Form1.cs` survives, `FixupProject` is not being called for this target.
- `System.Drawing` types (`ContentAlignment`) resolve only with WinForms enabled; a failure here means the project type is wrong, not the using.

- [ ] **Step 4: Run it to verify it passes**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~GeneratedProject_Builds"
```

Expected: all six rows PASS.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs AppScaffolding/
git commit -m "test(scaffold): compile the generated winforms app"
```

---

## Task 7: SQLite stays off both desktop targets

`IsPersistenceAllowed` hardcodes the single key `wpf`. WinForms shares the view models that read `JsonStore` directly, so `winforms` + `sqlite` would generate an app that silently bypasses the store it was told to use.

**Files:**
- Modify: `CodeGenerator/App/BuildAppCommand.cs`
- Test: `CodeGenerator.Tests/App/BuildAppPersistenceParsingTests.cs`

**Interfaces:**
- Produces: `internal static bool BuildAppCommand.IsPersistenceAllowed(ITargetScaffolder target, PersistenceKind persistence)` — unchanged signature, now false for `sqlite` on `wpf` **or** `winforms`.

- [ ] **Step 1: Write the failing test**

In `CodeGenerator.Tests/App/BuildAppPersistenceParsingTests.cs`, replace `IsPersistenceAllowed_RejectsSqliteForWpfOnly` with:

```csharp
    [TestMethod]
    public void IsPersistenceAllowed_RejectsSqliteForBothDesktopTargets()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("wpf", out var wpf));
        Assert.IsTrue(TargetScaffolders.TryResolve("winforms", out var winforms));
        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var console));

        // Both desktop targets share EntityViewModel, which reads JsonStore directly rather than
        // going through the store seam. Allowing sqlite here would generate an app that ignores it.
        Assert.IsFalse(BuildAppCommand.IsPersistenceAllowed(wpf, PersistenceKind.Sqlite));
        Assert.IsFalse(BuildAppCommand.IsPersistenceAllowed(winforms, PersistenceKind.Sqlite));

        Assert.IsTrue(BuildAppCommand.IsPersistenceAllowed(wpf, PersistenceKind.Json));
        Assert.IsTrue(BuildAppCommand.IsPersistenceAllowed(winforms, PersistenceKind.Json));
        Assert.IsTrue(BuildAppCommand.IsPersistenceAllowed(console, PersistenceKind.Sqlite));
    }
```

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~IsPersistenceAllowed"
```

Expected: FAIL — `Assert.IsFalse failed` on the `winforms` line.

- [ ] **Step 3: Widen the rule**

In `CodeGenerator/App/BuildAppCommand.cs`, replace `IsPersistenceAllowed`:

```csharp
    /// <summary>The targets whose view models read JsonStore directly, off the shared store
    /// seam, and so cannot honour a SQLite backend.</summary>
    private static readonly string[] JsonOnlyTargets = ["wpf", "winforms"];

    /// <summary>SQLite is available for every target except the two desktop ones, whose shared
    /// view models read JsonStore directly (off the shared seam).</summary>
    internal static bool IsPersistenceAllowed(ITargetScaffolder target, PersistenceKind persistence)
        => persistence != PersistenceKind.Sqlite
           || !JsonOnlyTargets.Contains(target.Key, StringComparer.OrdinalIgnoreCase);
```

And update the refusal message in `RunReviewLoop`:

```csharp
                    if (!IsPersistenceAllowed(target, persistence.Value))
                    {
                        Console.WriteLine(
                            "SQLite persistence isn't supported for the WPF or Windows Forms "
                            + "targets yet — they stay JSON.");
                        break;
                    }
```

- [ ] **Step 4: Run it to verify it passes**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~BuildAppPersistenceParsingTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/App/BuildAppCommand.cs CodeGenerator.Tests/App/BuildAppPersistenceParsingTests.cs
git commit -m "fix(cli): keep sqlite off both desktop targets"
```

---

## Task 8: SelectionState — the pure menu

**Files:**
- Create: `CodeGenerator/App/SelectionState.cs`
- Test: `CodeGenerator.Tests/App/SelectionStateTests.cs`

**Interfaces:**
- Produces: `internal sealed class SelectionState` in namespace `CodeGenerator.App`:
  - `SelectionState(IReadOnlyList<string> labels, int selected = 0)` — `selected` clamped into range.
  - `int Selected { get; }`
  - `void Move(ConsoleKey key)` — `UpArrow`/`DownArrow` only, clamped at both ends; any other key is ignored.
  - `IReadOnlyList<string> Lines()` — one line per label, the selected one prefixed `"  ❯ "` and the rest `"    "`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/SelectionStateTests.cs`:

```csharp
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class SelectionStateTests
{
    private static SelectionState ThreeItems() =>
        new(new[] { "First", "Second", "Third" });

    [TestMethod]
    public void StartsOnTheFirstItemByDefault()
    {
        Assert.AreEqual(0, ThreeItems().Selected);
    }

    [TestMethod]
    public void DownThenUp_ReturnsToWhereItStarted()
    {
        var state = ThreeItems();

        state.Move(ConsoleKey.DownArrow);
        Assert.AreEqual(1, state.Selected);

        state.Move(ConsoleKey.UpArrow);
        Assert.AreEqual(0, state.Selected);
    }

    [TestMethod]
    public void Up_ClampsAtTheTop()
    {
        var state = ThreeItems();

        state.Move(ConsoleKey.UpArrow);
        state.Move(ConsoleKey.UpArrow);

        // Not wrapping: a menu that jumps to the bottom when you overshoot the top reads as
        // a mis-press.
        Assert.AreEqual(0, state.Selected);
    }

    [TestMethod]
    public void Down_ClampsAtTheBottom()
    {
        var state = ThreeItems();

        for (var i = 0; i < 10; i++)
            state.Move(ConsoleKey.DownArrow);

        Assert.AreEqual(2, state.Selected);
    }

    [TestMethod]
    public void OtherKeys_ChangeNothing()
    {
        var state = ThreeItems();

        state.Move(ConsoleKey.LeftArrow);
        state.Move(ConsoleKey.Spacebar);

        Assert.AreEqual(0, state.Selected);
    }

    [TestMethod]
    public void Lines_MarkExactlyTheSelectedRow()
    {
        var state = ThreeItems();
        state.Move(ConsoleKey.DownArrow);

        var lines = state.Lines();

        Assert.HasCount(3, lines);
        Assert.AreEqual(1, lines.Count(l => l.Contains('❯')));
        StringAssert.Contains(lines[1], "❯ Second");

        // The unselected rows are padded to the same width, so the labels line up under the
        // marker instead of shifting left.
        Assert.AreEqual(lines[1].IndexOf("Second", StringComparison.Ordinal),
                        lines[0].IndexOf("First", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ASelectedIndexOutOfRange_IsClamped()
    {
        Assert.AreEqual(2, new SelectionState(new[] { "a", "b", "c" }, 99).Selected);
        Assert.AreEqual(0, new SelectionState(new[] { "a", "b", "c" }, -5).Selected);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~SelectionStateTests"
```

Expected: compile error — `The name 'SelectionState' does not exist`.

- [ ] **Step 3: Create it**

Create `CodeGenerator/App/SelectionState.cs`:

```csharp
namespace CodeGenerator.App;

/// <summary>
/// The decidable half of an arrow-key menu: which row is selected, how a key changes that, and
/// exactly what to paint. Holds no console reference, so the whole behaviour is testable — the
/// split <c>BuildAppCommand.ResolveTarget</c> and <c>DecisionReport</c> already use.
/// </summary>
internal sealed class SelectionState
{
    private const string SelectedPrefix = "  \u276f ";
    private const string UnselectedPrefix = "    ";

    private readonly IReadOnlyList<string> _labels;

    public SelectionState(IReadOnlyList<string> labels, int selected = 0)
    {
        _labels = labels;
        Selected = Math.Clamp(selected, 0, Math.Max(0, labels.Count - 1));
    }

    public int Selected { get; private set; }

    /// <summary>
    /// Moves the selection. Clamped rather than wrapping: overshooting the end and landing back
    /// at the start reads as a mis-press rather than a move.
    /// </summary>
    public void Move(ConsoleKey key)
    {
        Selected = key switch
        {
            ConsoleKey.UpArrow => Math.Max(0, Selected - 1),
            ConsoleKey.DownArrow => Math.Min(_labels.Count - 1, Selected + 1),
            _ => Selected,
        };
    }

    /// <summary>One line per label. Both prefixes are the same width, so the labels stay aligned
    /// as the marker moves.</summary>
    public IReadOnlyList<string> Lines()
        => _labels
            .Select((label, index) =>
                (index == Selected ? SelectedPrefix : UnselectedPrefix) + label)
            .ToList();
}
```

- [ ] **Step 4: Run it to verify it passes**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~SelectionStateTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/App/SelectionState.cs CodeGenerator.Tests/App/SelectionStateTests.cs
git commit -m "feat(cli): add the pure state behind an arrow-key menu"
```

---

## Task 9: The application-type menu and its numbered fallback

**Files:**
- Create: `CodeGenerator/App/ApplicationTypeMenu.cs`
- Test: `CodeGenerator.Tests/App/ApplicationTypeMenuTests.cs`

**Interfaces:**
- Consumes: `TargetScaffolders.TryResolve` (Task 5 registered `winforms`).
- Produces, in namespace `CodeGenerator.App`, `internal static class ApplicationTypeMenu`:
  - `const string Title = "Application type"`
  - `IReadOnlyList<string> Labels { get; }` — the six labels in spec order.
  - `ITargetScaffolder Resolve(int index)` — index into `Labels` → target.
  - `string LabelFor(ITargetScaffolder target)` — the label for a target, or its `Key` if it is not on the menu.
  - `int? ChooseNumbered(TextReader reader, TextWriter writer)` — the fallback prompt; index, or null to cancel.
  - `int? Choose()` — production entry point: `SelectionPrompt` when the console is real, `ChooseNumbered` when output is redirected. (Wired in Task 10.)

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/ApplicationTypeMenuTests.cs`:

```csharp
using AppScaffolding.Targets;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ApplicationTypeMenuTests
{
    [TestMethod]
    public void Labels_AreTheSixKindsOfAppInOrder()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "WPF (Windows desktop)",
                "Windows Desktop (WinForms)",
                "ASP.NET Web API",
                "Console",
                "Class Library",
                "Windows Service",
            },
            ApplicationTypeMenu.Labels.ToList());
    }

    [TestMethod]
    public void Resolve_MapsEveryLabelToItsTarget()
    {
        var keys = Enumerable.Range(0, ApplicationTypeMenu.Labels.Count)
            .Select(i => ApplicationTypeMenu.Resolve(i).Key)
            .ToList();

        CollectionAssert.AreEqual(
            new[] { "wpf", "winforms", "webapi", "console", "classlib", "worker" }, keys);
    }

    [TestMethod]
    public void LabelFor_NamesATargetTheReviewLoopCanShow()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("winforms", out var winforms));

        Assert.AreEqual("Windows Desktop (WinForms)", ApplicationTypeMenu.LabelFor(winforms));
    }

    [TestMethod]
    public void ChooseNumbered_ReturnsTheIndexTyped()
    {
        var writer = new StringWriter();

        // "2" is the second item, which is index 1. Users count from one.
        Assert.AreEqual(1, ApplicationTypeMenu.ChooseNumbered(new StringReader("2\n"), writer));
    }

    [TestMethod]
    public void ChooseNumbered_ListsEveryOptionWithItsNumber()
    {
        var writer = new StringWriter();
        ApplicationTypeMenu.ChooseNumbered(new StringReader("1\n"), writer);

        var written = writer.ToString();

        StringAssert.Contains(written, "1) WPF (Windows desktop)");
        StringAssert.Contains(written, "6) Windows Service");
        StringAssert.Contains(written, "[1-6]");
    }

    [TestMethod]
    public void ChooseNumbered_BlankTakesTheDefault()
    {
        var writer = new StringWriter();

        Assert.AreEqual(0, ApplicationTypeMenu.ChooseNumbered(new StringReader("\n"), writer));
    }

    [TestMethod]
    public void ChooseNumbered_ReAsksOnSomethingOutOfRange()
    {
        var writer = new StringWriter();

        var chosen = ApplicationTypeMenu.ChooseNumbered(new StringReader("9\nzero\n3\n"), writer);

        Assert.AreEqual(2, chosen);
        StringAssert.Contains(writer.ToString(), "Choose a number between 1 and 6");
    }

    [TestMethod]
    public void ChooseNumbered_CancelsOnCAndOnEndOfInput()
    {
        Assert.IsNull(ApplicationTypeMenu.ChooseNumbered(new StringReader("c\n"), new StringWriter()));

        // EOF is the absence of an answer, not the default answer — the rule
        // DataSourceInterview already applies.
        Assert.IsNull(ApplicationTypeMenu.ChooseNumbered(new StringReader(""), new StringWriter()));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~ApplicationTypeMenuTests"
```

Expected: compile error — `The name 'ApplicationTypeMenu' does not exist`.

- [ ] **Step 3: Create the menu**

Create `CodeGenerator/App/ApplicationTypeMenu.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.App;

/// <summary>
/// The kinds of app <c>build app</c> offers, and how a choice becomes a target.
/// </summary>
/// <remarks>
/// Labels are display-only and ordered for a reader; <see cref="TargetScaffolders"/> keeps its own
/// order and its keys are what <c>approve &lt;target&gt;</c> accepts. Keeping the two separate is
/// what lets the menu read well without renaming anything scriptable.
/// </remarks>
internal static class ApplicationTypeMenu
{
    private sealed record Choice(string Label, string Key);

    private static readonly Choice[] Choices =
    [
        new("WPF (Windows desktop)", "wpf"),
        new("Windows Desktop (WinForms)", "winforms"),
        new("ASP.NET Web API", "webapi"),
        new("Console", "console"),
        new("Class Library", "classlib"),
        new("Windows Service", "worker"),
    ];

    public const string Title = "Application type";

    public static IReadOnlyList<string> Labels { get; } =
        Choices.Select(c => c.Label).ToList();

    /// <summary>The target the row at <paramref name="index"/> selects.</summary>
    public static ITargetScaffolder Resolve(int index)
    {
        var key = Choices[index].Key;

        // Every key here is registered, so a miss is a wiring bug rather than user input.
        if (!TargetScaffolders.TryResolve(key, out var target))
            throw new InvalidOperationException($"No target registered for menu key '{key}'.");

        return target;
    }

    /// <summary>The menu label for a target, or its key if it is not on the menu.</summary>
    public static string LabelFor(ITargetScaffolder target)
        => Choices.FirstOrDefault(
               c => string.Equals(c.Key, target.Key, StringComparison.OrdinalIgnoreCase))
               ?.Label
           ?? target.Key;

    /// <summary>
    /// The fallback prompt: a numbered list read a line at a time. Used when the console cannot
    /// be repainted (redirected output makes the cursor APIs throw), and testable for the same
    /// reason.
    /// </summary>
    /// <returns>The chosen index, or null to cancel.</returns>
    public static int? ChooseNumbered(TextReader reader, TextWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine(Title + ":");

        for (var i = 0; i < Choices.Length; i++)
            writer.WriteLine($"  {i + 1}) {Choices[i].Label}");

        while (true)
        {
            writer.Write($"Choose [1-{Choices.Length}] (default 1, c to cancel): ");

            var line = reader.ReadLine();

            // EOF is the absence of an answer, not the default answer.
            if (line is null)
                return null;

            var text = line.Trim();

            if (text.Length == 0)
                return 0;

            if (string.Equals(text, "c", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "cancel", StringComparison.OrdinalIgnoreCase))
                return null;

            if (int.TryParse(text, out var number) && number >= 1 && number <= Choices.Length)
                return number - 1;

            writer.WriteLine($"  Choose a number between 1 and {Choices.Length}, or c to cancel.");
        }
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~ApplicationTypeMenuTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/App/ApplicationTypeMenu.cs CodeGenerator.Tests/App/ApplicationTypeMenuTests.cs
git commit -m "feat(cli): add the application-type menu and its numbered fallback"
```

---

## Task 10: SelectionPrompt — the console driver

The one piece with no unit test, for the reason `CommandLineEditor` has none: it exists to talk to a real console. Everything decidable was moved into `SelectionState` in Task 8 precisely so this file stays small enough to read.

**Files:**
- Create: `CodeGenerator/App/SelectionPrompt.cs`
- Modify: `CodeGenerator/App/ApplicationTypeMenu.cs` (add `Choose()`)

**Interfaces:**
- Consumes: `SelectionState` (Task 8).
- Produces: `internal static int? SelectionPrompt.Choose(string title, IReadOnlyList<string> labels, int defaultIndex)` — the chosen index, or null if the user pressed Esc or input ended.
- Produces: `internal static int? ApplicationTypeMenu.Choose()`.

- [ ] **Step 1: Create the driver**

Create `CodeGenerator/App/SelectionPrompt.cs`:

```csharp
namespace CodeGenerator.App;

/// <summary>
/// Paints a <see cref="SelectionState"/> and drives it from raw key presses. Enter commits, Esc
/// cancels.
/// </summary>
/// <remarks>
/// Deliberately thin and deliberately untested: it exists to talk to a real console, which is
/// exactly what a test cannot supply. Every decision it makes lives in
/// <see cref="SelectionState"/> instead. <see cref="CommandLineEditor"/> is untested for the same
/// reason.
/// </remarks>
internal static class SelectionPrompt
{
    /// <summary>The chosen index, or null if the user cancelled.</summary>
    public static int? Choose(string title, IReadOnlyList<string> labels, int defaultIndex)
    {
        var state = new SelectionState(labels, defaultIndex);

        Console.WriteLine();
        Console.WriteLine($"{title} (\u2191\u2193 then Enter, Esc to cancel):");
        Console.WriteLine();

        var top = Console.CursorTop;

        Paint(state, top);

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    // Leave the cursor below the list so whatever is written next does not
                    // overwrite the menu the user just answered.
                    MoveBelow(top, labels.Count);
                    return state.Selected;

                case ConsoleKey.Escape:
                    MoveBelow(top, labels.Count);
                    return null;

                default:
                    state.Move(key.Key);
                    Paint(state, top);
                    break;
            }
        }
    }

    private static void Paint(SelectionState state, int top)
    {
        var lines = state.Lines();

        for (var i = 0; i < lines.Count; i++)
        {
            var row = top + i;

            if (row >= Console.BufferHeight)
                break;

            Console.SetCursorPosition(0, row);

            // Blank the row first: the previous marker has to go, and the labels differ in length.
            Console.Write(new string(' ', Math.Max(0, Console.BufferWidth - 1)));
            Console.SetCursorPosition(0, row);
            Console.Write(lines[i]);
        }
    }

    private static void MoveBelow(int top, int count)
    {
        var row = Math.Min(top + count, Math.Max(0, Console.BufferHeight - 1));
        Console.SetCursorPosition(0, row);
        Console.WriteLine();
    }
}
```

- [ ] **Step 2: Add the production entry point**

Append to `ApplicationTypeMenu`:

```csharp
    /// <summary>
    /// Asks for an application type the best way this console allows: an arrow-key list normally,
    /// the numbered prompt when output is redirected — where <c>Console.SetCursorPosition</c> and
    /// <c>ReadKey</c> throw "handle is invalid", the same condition
    /// <see cref="CommandLineEditor"/> guards on.
    /// </summary>
    public static int? Choose()
        => Console.IsOutputRedirected
            ? ChooseNumbered(Console.In, Console.Out)
            : SelectionPrompt.Choose(Title, Labels, 0);
```

- [ ] **Step 3: Build to verify it compiles**

```bash
dotnet build CodeGenerator/CodeGenerator.csproj
```

Expected: build succeeded.

- [ ] **Step 4: Commit**

```bash
git add CodeGenerator/App/SelectionPrompt.cs CodeGenerator/App/ApplicationTypeMenu.cs
git commit -m "feat(cli): drive the application-type menu from arrow keys"
```

---

## Task 11: BuildAppWizard

The fix for the reported bug: bare `build app` asks for a description first, instead of interrogating the user about data and then printing a usage hint.

**Files:**
- Create: `CodeGenerator/App/BuildAppWizard.cs`
- Test: `CodeGenerator.Tests/App/BuildAppWizardTests.cs`

**Interfaces:**
- Consumes: `InlineBuildRequest(string Description, DataSourceSpec? Source, bool Normalize)` from `DataIngestion`; `DataSourceInterview.Resolve(TextReader, TextWriter)`; `ApplicationTypeMenu.Resolve(int)`.
- Produces, in namespace `CodeGenerator.App`:
  - `internal enum WizardDecision { Proceed, Cancelled }`
  - `internal sealed record WizardOutcome(WizardDecision Decision, string Description, ITargetScaffolder? Target, DataSourceSpec? Source, bool Normalize)`
  - `internal static WizardOutcome BuildAppWizard.Run(InlineBuildRequest request, TextReader reader, TextWriter writer, Func<int?> chooseApplicationType)`

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/App/BuildAppWizardTests.cs`:

```csharp
using CodeGenerator.App;
using DataIngestion;
using DataIngestion.Schema.Readers;

namespace CodeGenerator.Tests.App;

[TestClass]
public class BuildAppWizardTests
{
    private static InlineBuildRequest Bare() => new(string.Empty, null, false);

    private static string TempCsv()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        File.WriteAllText(path, "Id,Name\n1,Ada\n");
        return path;
    }

    /// <summary>
    /// The reported bug. Bare `build app` used to open the data interview and then, on "no",
    /// print a usage hint about a description it had never asked for.
    /// </summary>
    [TestMethod]
    public void BareBuildApp_AsksForTheDescriptionBeforeAskingAboutData()
    {
        var writer = new StringWriter();

        var outcome = BuildAppWizard.Run(
            Bare(), new StringReader("a library lending system\nn\n"), writer, () => 0);

        var written = writer.ToString();
        var description = written.IndexOf("Describe the application", StringComparison.Ordinal);
        var data = written.IndexOf("Do you have existing data?", StringComparison.Ordinal);

        Assert.IsTrue(description >= 0, "the description was never asked for");
        Assert.IsTrue(data >= 0, "the data question was never asked");
        Assert.IsTrue(data > description,
            $"the data question came first (description at {description}, data at {data})");

        Assert.AreEqual(WizardDecision.Proceed, outcome.Decision);
        Assert.AreEqual("a library lending system", outcome.Description);
    }

    [TestMethod]
    public void AnInlineDescription_IsAcceptedWithoutBeingAskedAgain()
    {
        var writer = new StringWriter();

        var outcome = BuildAppWizard.Run(
            new InlineBuildRequest("a bike shop", null, false),
            new StringReader("n\n"), writer, () => 0);

        Assert.AreEqual("a bike shop", outcome.Description);
        Assert.IsFalse(
            writer.ToString().Contains("Describe the application", StringComparison.Ordinal),
            "typing a description should stay a shortcut, not something to retype");
    }

    [TestMethod]
    public void TheChosenApplicationType_BecomesTheTarget()
    {
        var outcome = BuildAppWizard.Run(
            Bare(), new StringReader("a shop\nn\n"), new StringWriter(), () => 1);

        // Index 1 is "Windows Desktop (WinForms)".
        Assert.AreEqual("winforms", outcome.Target!.Key);
    }

    [TestMethod]
    public void ABlankDescription_IsAllowedSoDataCanSpeakForItself()
    {
        var path = TempCsv();
        try
        {
            var outcome = BuildAppWizard.Run(
                Bare(), new StringReader($"\ny\ncsv\n{path}\n"), new StringWriter(), () => 3);

            Assert.AreEqual(WizardDecision.Proceed, outcome.Decision);
            Assert.AreEqual(string.Empty, outcome.Description);
            Assert.AreEqual(DataSourceKind.Csv, outcome.Source!.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AnInlineSource_SkipsTheDataQuestion()
    {
        var path = TempCsv();
        var writer = new StringWriter();
        try
        {
            var outcome = BuildAppWizard.Run(
                new InlineBuildRequest("orders", new DataSourceSpec(DataSourceKind.Csv, path), true),
                new StringReader(""), writer, () => 0);

            Assert.AreEqual(WizardDecision.Proceed, outcome.Decision);
            Assert.AreEqual(path, outcome.Source!.Location);
            Assert.IsTrue(outcome.Normalize);
            Assert.IsFalse(
                writer.ToString().Contains("Do you have existing data?", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void CancellingAtTheDescription_Cancels()
    {
        var outcome = BuildAppWizard.Run(
            Bare(), new StringReader("c\n"), new StringWriter(), () => 0);

        Assert.AreEqual(WizardDecision.Cancelled, outcome.Decision);
    }

    [TestMethod]
    public void CancellingAtTheApplicationType_Cancels()
    {
        var outcome = BuildAppWizard.Run(
            Bare(), new StringReader("a shop\n"), new StringWriter(), () => null);

        Assert.AreEqual(WizardDecision.Cancelled, outcome.Decision);
    }

    [TestMethod]
    public void CancellingAtTheDataQuestion_Cancels()
    {
        var outcome = BuildAppWizard.Run(
            Bare(), new StringReader("a shop\ny\nc\n"), new StringWriter(), () => 0);

        // Not NoData: someone who backed out must not be handed a Wikipedia build they never
        // asked for.
        Assert.AreEqual(WizardDecision.Cancelled, outcome.Decision);
    }

    [TestMethod]
    public void EndOfInputAtTheDescription_Cancels()
    {
        var outcome = BuildAppWizard.Run(
            Bare(), new StringReader(""), new StringWriter(), () => 0);

        Assert.AreEqual(WizardDecision.Cancelled, outcome.Decision);
    }

    [TestMethod]
    public void TheApplicationType_IsAskedAfterTheDescriptionAndBeforeData()
    {
        var order = new List<string>();

        var outcome = BuildAppWizard.Run(
            Bare(),
            new StringReader("a shop\nn\n"),
            new StringWriter(),
            () => { order.Add("type"); return 0; });

        // The menu is a delegate, so its position in the flow is only observable this way.
        Assert.AreEqual(WizardDecision.Proceed, outcome.Decision);
        CollectionAssert.AreEqual(new[] { "type" }, order);
        Assert.AreEqual("a shop", outcome.Description);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~BuildAppWizardTests"
```

Expected: compile error — `The name 'BuildAppWizard' does not exist`.

- [ ] **Step 3: Create the wizard**

Create `CodeGenerator/App/BuildAppWizard.cs`:

```csharp
using AppScaffolding;
using DataIngestion;

namespace CodeGenerator.App;

/// <summary>What the wizard concluded.</summary>
internal enum WizardDecision
{
    /// <summary>Every question was answered. Build from <see cref="WizardOutcome"/>.</summary>
    Proceed,

    /// <summary>
    /// The user backed out, or stdin closed mid-wizard. Nothing is generated — deliberately not a
    /// "no data" answer, which would hand them a Wikipedia build they never asked for.
    /// </summary>
    Cancelled
}

/// <summary>
/// The answers a <c>build app</c> run needs. <c>Source</c> is null when the user has no data,
/// which is what selects the description-driven discovery path.
/// </summary>
internal sealed record WizardOutcome(
    WizardDecision Decision,
    string Description,
    ITargetScaffolder? Target,
    DataSourceSpec? Source,
    bool Normalize);

/// <summary>
/// Asks what to build, what kind of app it is, and whether there is data — in that order.
/// </summary>
/// <remarks>
/// Reads and writes through the <see cref="TextReader"/>/<see cref="TextWriter"/> it is given
/// rather than the console, so the whole flow is drivable from a test — the pattern
/// <c>DataSourceInterview</c> and <c>BuildAppCommand.ResolveTarget</c> established. The menu
/// arrives as a delegate for the same reason: it is the one step that needs raw key input.
/// <para>
/// The caller decides whether to run this at all: redirected stdin never prompts, and uses
/// <see cref="InlineDataSource"/> alone.
/// </para>
/// </remarks>
internal static class BuildAppWizard
{
    private static readonly WizardOutcome Cancelled =
        new(WizardDecision.Cancelled, string.Empty, null, null, false);

    public static WizardOutcome Run(
        InlineBuildRequest request,
        TextReader reader,
        TextWriter writer,
        Func<int?> chooseApplicationType)
    {
        var description = request.Description;

        // Asked first, and only when it was not already typed: `build app: a shop` has answered
        // this, and re-asking would make the shortcut pointless.
        if (description.Length == 0)
        {
            var typed = AskForDescription(reader, writer);

            if (typed is null)
                return Cancelled;

            description = typed;
        }

        // Before discovery, not at `approve`: a user who wants a Web API should not find out
        // after a Wikipedia fetch and a model review.
        var index = chooseApplicationType();

        if (index is null)
            return Cancelled;

        var target = ApplicationTypeMenu.Resolve(index.Value);

        var source = request.Source;

        if (source is null)
        {
            var outcome = DataSourceInterview.Resolve(reader, writer);

            if (outcome.Decision == InterviewDecision.Cancelled)
                return Cancelled;

            // NoData leaves this null, which is what selects the discovery path downstream.
            source = outcome.Source;
        }

        return new WizardOutcome(
            WizardDecision.Proceed, description, target, source, request.Normalize);
    }

    /// <summary>
    /// The typed description, <see cref="string.Empty"/> if the user skipped it, or null to
    /// cancel.
    /// </summary>
    /// <remarks>
    /// Blank is a real answer: `build app from csv orders.csv` with no description is a supported
    /// flow, so the prompt says so rather than rejecting an empty line.
    /// </remarks>
    private static string? AskForDescription(TextReader reader, TextWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine("Describe the application you want to build.");
        writer.WriteLine("(Enter to skip if you'll point at a data file instead, 'c' to cancel)");
        writer.Write("> ");

        var line = reader.ReadLine();

        // EOF is the absence of an answer, not an empty one. Conflating the two would run a whole
        // discovery pipeline off a closed pipe.
        if (line is null)
            return null;

        var text = line.Trim();

        return string.Equals(text, "c", StringComparison.OrdinalIgnoreCase)
               || string.Equals(text, "cancel", StringComparison.OrdinalIgnoreCase)
            ? null
            : text;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~BuildAppWizardTests"
```

Expected: PASS, all eleven.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/App/BuildAppWizard.cs CodeGenerator.Tests/App/BuildAppWizardTests.cs
git commit -m "feat(cli): ask for a description, then an app type, then data"
```

---

## Task 12: Wire the wizard into BuildAppCommand

**Files:**
- Modify: `CodeGenerator/App/BuildAppCommand.cs`
- Test: `CodeGenerator.Tests/App/BuildAppTargetSelectionTests.cs`
- Test: `CodeGenerator.Tests/App/BuildAppPersistenceParsingTests.cs`

**Interfaces:**
- Consumes: `BuildAppWizard.Run`, `WizardOutcome`, `ApplicationTypeMenu.Choose`, `ApplicationTypeMenu.LabelFor`.
- Produces:
  - `internal static ITargetScaffolder? ResolveTarget(string[] parts, bool inputRedirected, TextReader reader, ITargetScaffolder? preselected = null)` — the new parameter is **optional**, so the existing call sites and tests compile unchanged.
  - `internal static bool IsPersistenceToken(string token)` — true for `json`/`sqlite`, case-insensitively.
  - `ResolvePersistence` now also reads a lone `approve sqlite`.

- [ ] **Step 1: Write the failing tests**

Append to `CodeGenerator.Tests/App/BuildAppTargetSelectionTests.cs`, inside the class:

```csharp
    [TestMethod]
    public void APreselectedTarget_IsUsedWithoutPrompting()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("webapi", out var chosen));

        // TextReader.Null: if this prompted, it would read EOF and fall over rather than quietly
        // return the wizard's answer.
        var t = BuildAppCommand.ResolveTarget(
            Parts("approve"), inputRedirected: false, TextReader.Null, chosen);

        Assert.AreEqual("webapi", t!.Key);
    }

    [TestMethod]
    public void AnInlineTarget_StillOverridesThePreselectedOne()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("webapi", out var chosen));

        var t = BuildAppCommand.ResolveTarget(
            Parts("approve", "console"), inputRedirected: false, TextReader.Null, chosen);

        Assert.AreEqual("console", t!.Key);
    }

    [TestMethod]
    public void ALonePersistenceToken_IsNotMistakenForATarget()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var chosen));

        // `approve sqlite` used to be rejected as an unknown target. With the target already
        // settled by the wizard, the token can only be persistence.
        var t = BuildAppCommand.ResolveTarget(
            Parts("approve", "sqlite"), inputRedirected: false, TextReader.Null, chosen);

        Assert.AreEqual("console", t!.Key);
    }
```

Append to `CodeGenerator.Tests/App/BuildAppPersistenceParsingTests.cs`:

```csharp
    [TestMethod]
    public void ResolvePersistence_ReadsALoneSqliteToken()
    {
        // `approve sqlite` with the target already chosen by the wizard.
        Assert.AreEqual(
            PersistenceKind.Sqlite,
            BuildAppCommand.ResolvePersistence(new[] { "approve", "sqlite" }));

        Assert.AreEqual(
            PersistenceKind.Json,
            BuildAppCommand.ResolvePersistence(new[] { "approve", "json" }));
    }

    [TestMethod]
    public void ResolvePersistence_StillTreatsATargetAsATarget()
    {
        // A target in position 1 must not be read as a persistence token, or `approve console`
        // would become invalid.
        Assert.AreEqual(
            PersistenceKind.Json,
            BuildAppCommand.ResolvePersistence(new[] { "approve", "console" }));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~BuildAppTargetSelectionTests|FullyQualifiedName~BuildAppPersistenceParsingTests"
```

Expected: compile error on the four-argument `ResolveTarget`, then `ResolvePersistence_ReadsALoneSqliteToken` FAILS returning `Json`.

- [ ] **Step 3: Update the parsing helpers**

In `CodeGenerator/App/BuildAppCommand.cs`, replace `ResolveTarget` and `ResolvePersistence`:

```csharp
    /// <summary>
    /// Resolves the output target for an <c>approve</c> command. An inline
    /// <c>approve &lt;target&gt;</c> wins (null when the key is unknown); a
    /// <paramref name="preselected"/> target — the one the wizard asked for — is next. With
    /// neither, redirected input defaults to WPF (so piped scripts are unchanged) and interactive
    /// input is prompted, blank meaning WPF, re-prompting on an unknown key.
    /// </summary>
    internal static ITargetScaffolder? ResolveTarget(
        string[] parts, bool inputRedirected, TextReader reader,
        ITargetScaffolder? preselected = null)
    {
        // A lone json/sqlite is persistence, not a target: `approve sqlite` is only meaningful
        // because the target was already chosen, and reading it as a target key is what used to
        // reject it as unknown.
        if (parts.Length >= 2 && !IsPersistenceToken(parts[1]))
            return TargetScaffolders.TryResolve(parts[1], out var inline) ? inline : null;

        if (preselected is not null)
            return preselected;

        if (inputRedirected)
            return TargetScaffolders.Default;

        while (true)
        {
            Console.Write($"Target [{string.Join("/", TargetScaffolders.Keys)}] (default wpf): ");
            var line = reader.ReadLine()?.Trim() ?? string.Empty;

            if (line.Length == 0)
                return TargetScaffolders.Default;

            if (TargetScaffolders.TryResolve(line, out var chosen))
                return chosen;

            Console.WriteLine("Unknown target. Valid: " + string.Join(", ", TargetScaffolders.Keys));
        }
    }

    /// <summary>Whether <paramref name="token"/> names a persistence backend rather than a
    /// target.</summary>
    internal static bool IsPersistenceToken(string token)
        => string.Equals(token, "sqlite", StringComparison.OrdinalIgnoreCase)
           || string.Equals(token, "json", StringComparison.OrdinalIgnoreCase);

    /// <summary>The persistence backend an <c>approve</c> line selects. No token means JSON;
    /// <c>sqlite</c>/<c>json</c> select explicitly, in either the second or third position (the
    /// second is how <c>approve sqlite</c> reads once the wizard has settled the target); any
    /// other token is invalid (returns null).</summary>
    internal static PersistenceKind? ResolvePersistence(string[] parts)
    {
        var token = parts.Length >= 3
            ? parts[2]
            : parts.Length == 2 && IsPersistenceToken(parts[1]) ? parts[1] : null;

        if (token is null)
            return PersistenceKind.Json;

        return token.ToLowerInvariant() switch
        {
            "sqlite" => PersistenceKind.Sqlite,
            "json" => PersistenceKind.Json,
            _ => null,
        };
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~BuildAppTargetSelectionTests|FullyQualifiedName~BuildAppPersistenceParsingTests"
```

Expected: PASS, including every pre-existing test in both files.

- [ ] **Step 5: Call the wizard**

Replace `Handle` in `CodeGenerator/App/BuildAppCommand.cs`:

```csharp
    public void Handle(string input, string workspaceRoot)
    {
        var request = InlineDataSource.Parse(Tail(input));

        var description = request.Description;
        var source = request.Source;
        var normalize = request.Normalize;
        ITargetScaffolder? target = null;

        // Redirected stdin never prompts: existing piped scripts and E2E tests feed the review
        // loop directly, and a question at the front would break every one of them. They opt in
        // with the inline `from <format> <location>` form instead, and pick a target at `approve`.
        if (!Console.IsInputRedirected)
        {
            var outcome = BuildAppWizard.Run(
                request, Console.In, Console.Out, ApplicationTypeMenu.Choose);

            if (outcome.Decision == WizardDecision.Cancelled)
            {
                Console.WriteLine("Cancelled. Nothing was generated.");
                return;
            }

            description = outcome.Description;
            target = outcome.Target;
            source = outcome.Source;
            normalize = outcome.Normalize;
        }

        if (source is null)
        {
            HandleFromDescription(description, workspaceRoot, target);
            return;
        }

        HandleFromData(description, source, normalize, workspaceRoot, target);
    }
```

- [ ] **Step 6: Thread the target through**

Change the three private methods' signatures and their `RunReviewLoop` calls:

```csharp
    private static void HandleFromDescription(
        string description, string workspaceRoot, ITargetScaffolder? target)
```

…and its `RunReviewLoop` call:

```csharp
        RunReviewLoop(
            result.Model, DomainAppName.FromDescription(description), workspaceRoot,
            ImportedData.None, target);
```

```csharp
    private static void HandleFromData(
        string description, DataSourceSpec source, bool normalize, string workspaceRoot,
        ITargetScaffolder? target)
```

…and its call:

```csharp
        RunReviewLoop(model, AppNameFor(description, source), workspaceRoot, imported, target);
```

```csharp
    private static void RunReviewLoop(
        CandidateDomainModel model, string appName, string workspaceRoot,
        ImportedData importedData, ITargetScaffolder? target)
```

Inside the loop, after `PrintModel(model);`, show what `approve` will build:

```csharp
            // The target was settled before discovery ran, so what `approve` will produce has to
            // be visible here rather than being a surprise.
            if (target is not null)
                Console.WriteLine($"  Target: {ApplicationTypeMenu.LabelFor(target)}");
```

And in the `approve` case, pass it to `ResolveTarget` — rename the local so it does not shadow the parameter:

```csharp
                    var chosenTarget = ResolveTarget(
                        parts, Console.IsInputRedirected, Console.In, target);
                    if (chosenTarget is null)
                    {
                        Console.WriteLine(
                            $"Unknown target '{parts[1]}'. Valid: {string.Join(", ", TargetScaffolders.Keys)}");
                        break;
                    }
```

Then replace every later use of `target` in that case block with `chosenTarget` — `IsPersistenceAllowed(chosenTarget, ...)` and the `.Scaffold(..., chosenTarget, ...)` call.

- [ ] **Step 7: Run the whole CLI suite**

```bash
dotnet test CodeGenerator.Tests
```

Expected: PASS. Every pre-existing test must be green **without edits** — in particular any piped/E2E `build app` test, which is the guard that the wizard stayed on the interactive side.

If `ProcessRunnerTests` alone reddens, check it against a stash first: adding tests to this project is known to trip it in Debug through CPU starvation rather than through any change here.

- [ ] **Step 8: Commit**

```bash
git add CodeGenerator/App/BuildAppCommand.cs CodeGenerator.Tests/App/
git commit -m "feat(cli): run the wizard for every interactive build app"
```

---

## Task 13: Verify it by hand in the real CLI

`dotnet test` leaves the CLI's own `bin` stale, so a green suite can sit alongside a dead command. The arrow-key prompt has no unit test by design, which makes this the only place it is exercised.

**Files:** none — verification only.

- [ ] **Step 1: Rebuild the CLI**

```bash
cd /d/SmartCodeGenerator-Repository
dotnet build CodeGenerator/CodeGenerator.csproj -c Debug
```

- [ ] **Step 2: Run it and drive the wizard by hand**

Run the built `CodeGenerator` executable in a real terminal (not piped — piping is exactly the path that skips the wizard) and check each of these:

1. `build app` → the description prompt appears **first**, before any data question.
2. ↑/↓ moves the marker; it stops at both ends rather than wrapping.
3. Enter commits and the data question follows.
4. The review loop header shows `Target: <label>` matching what was picked.
5. Esc at the menu prints `Cancelled. Nothing was generated.` and generates nothing.
6. `build app: a library lending system` goes straight to the menu, with no description prompt.
7. Picking `Windows Desktop (WinForms)`, then `approve`, generates a `*.WinForms` project.
8. `approve sqlite` on a desktop target reports the JSON-only refusal rather than "Unknown target 'sqlite'".
9. `build app`, blank description (Enter), any type, then "no data" → the existing `Usage: build app: <description of the system you want>` hint. This is the one path the spec keeps as an error, because there is genuinely nothing to build from; it must not crash or start a discovery run on an empty description.

- [ ] **Step 3: Confirm the generated WinForms app runs**

```bash
dotnet run --project <workspace>/<Name>.WinForms
```

Expected: a window with one tab per entity, the grid populated, and Add/Delete/Save working. Delete is disabled until a row is selected — the check that the `CommandManager`-free `RelayCommand` really is wired up by the form.

- [ ] **Step 4: Record anything that needs fixing**

Fix in the relevant task's files and re-run its tests. Do not commit a fix without a test unless the defect is purely in `SelectionPrompt`, the one file with no unit coverage; say so in the commit message if that is the case.

---

## Task 14: Documentation

`docs/commands.md` is generated from `CommandDictionary` and audited by `CommandReferenceDocTests`, so the catalog is edited and the doc regenerated.

**Files:**
- Modify: `CodeGenerationEngine/Core/CommandDictionary.cs`
- Modify: `docs/commands.md` (regenerated, never hand-edited)
- Modify: `CodeGenerator/README.md`
- Modify: `AppScaffolding/README.md`

- [ ] **Step 1: Update the three `build app` catalog entries**

In `CodeGenerationEngine/Core/CommandDictionary.cs`:

```csharp
        new()
        {
            Category = "Build App",
            Command = "build app: <description>",
            Description = "Describes a system in plain English, then asks which kind of app to "
                        + "build and whether you have data, discovers a domain model, and opens a "
                        + "review loop before any code is written.",
            Example = "build app: a library that lends books to members"
        },
        new()
        {
            Category = "Build App",
            Command = "build app",
            Description = "Starts the same guided flow, asking for the description first, then the "
                        + "application type, then whether you already have data.",
            Example = "build app"
        },
```

Leave the `build app: <description> from <format> <location>` entry's text as it is — that form still skips the data question — and update the `approve` entry:

```csharp
        new()
        {
            Category = "Build App",
            Command = "approve [target] [sqlite]",
            Description = "Review loop: generates the app using the type chosen at the start. "
                        + "Name a target to override it: wpf (default), winforms, console, webapi, "
                        + "classlib, worker. Add 'sqlite' for EF Core persistence (JSON is the "
                        + "default; the wpf and winforms targets are JSON-only). Alias: a.",
            Example = "approve webapi sqlite"
        },
```

- [ ] **Step 2: Regenerate the command reference**

```bash
cd /d/SmartCodeGenerator-Repository
UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~CommandReferenceDocTests"
```

- [ ] **Step 3: Verify the audit passes on the committed doc**

```bash
dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~CommandReferenceDocTests"
```

Expected: PASS without `UPDATE_COMMAND_DOCS` set. Check `git diff docs/commands.md` shows only the intended rows.

- [ ] **Step 4: Update the two READMEs**

In `CodeGenerator/README.md`, find where the `build app` flow and the target list appear and make them say:

> An interactive `build app` asks three questions before any work starts: what the application is
> for, which kind of app to build, and whether you already have data. The kinds are WPF (Windows
> desktop), Windows Desktop (WinForms), ASP.NET Web API, Console, Class Library and Windows
> Service; the menu is arrow-key driven, and falls back to a numbered prompt when output is
> redirected. Typing a description inline (`build app: a library that lends books`) answers the
> first question and starts at the second.
>
> Targets: `wpf` (default), `winforms`, `console`, `webapi`, `classlib`, `worker`. `approve
> <target>` at the review loop overrides the type chosen at the start. SQLite persistence is
> available for every target except `wpf` and `winforms`, which are JSON-only.
>
> Piped or redirected input skips the wizard entirely and uses the inline form
> (`build app: <description> from <format> <location>`), picking a target at `approve`.

In `AppScaffolding/README.md`, add to the target list:

> `WinFormsTarget` — a runnable Windows Forms CRUD app. Shares its whole presentation layer with
> `WpfTarget` through `DesktopViewModelFiles`; the only differences are the view (`Views/MainForm.cs`
> plus its own `Program.cs`, instead of `MainWindow.xaml`) and which `RelayCommand` variant it
> emits, since WPF's requeries through `CommandManager` and WinForms has no PresentationCore.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/CommandDictionary.cs docs/commands.md \
        CodeGenerator/README.md AppScaffolding/README.md
git commit -m "docs: describe the build app wizard and the winforms target"
```

---

## Task 15: Full verification

**Files:** none — verification only.

- [ ] **Step 1: Build the whole solution the way CI does**

```bash
cd /d/SmartCodeGenerator-Repository
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror
```

Expected: build succeeded, 0 warnings. The `CodeGenerator`-only Debug build does not surface analyzer errors, so a clean local `dotnet test` proves nothing here.

- [ ] **Step 2: Run every test project**

```bash
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release
```

Expected: all four test projects green, and the count at or above the 1657 that were passing before this work — new tests added, none removed or edited to fit.

- [ ] **Step 3: Confirm no existing test was edited to accommodate the wizard**

```bash
git diff main --stat -- '*Tests*'
```

Review the list. Legitimate edits are exactly these three, all named in the plan:
- `TargetScaffoldersTests.cs` — the key list gained `winforms`.
- `BuildAppPersistenceParsingTests.cs` — the WPF-only rule became the two-desktop-target rule.
- `GeneratedProjectBuildTests.cs` — a `winforms` row and the widened Windows guard.

Any other edited test file means the interactive-only boundary leaked and needs investigating before this merges.

- [ ] **Step 4: Report**

State plainly what passes, what was verified by hand versus by test, and anything left undone. `SelectionPrompt` has no unit test by design — say so, and say that Task 13 is what covered it.
