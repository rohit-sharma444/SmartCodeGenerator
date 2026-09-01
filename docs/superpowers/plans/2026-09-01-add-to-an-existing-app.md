# Adding to an App You Already Generated — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run `build app` again with two more classes in the description, answer `add` at the overwrite guard, and have only those two classes written — every other generated file left byte-identical on disk.

**Architecture:** `ClassSnapshots.Read`, scoped to the app's `Models` folder, says what is already there. A pure `ModelDelta` classifies each proposed class as New, Unchanged or Changed. `OverwritePrompt` gains an `Add` answer, offered only when something is new. The write is done by **scaffolding into a short temporary root and copying only the chosen files across** — see *The implementation choice this plan makes* below, which is a plan-level decision the spec deliberately left open.

**Tech Stack:** .NET 10, Roslyn (already referenced), MSTest (CodeGenerator.Tests).

**Spec:** `docs/superpowers/specs/2026-09-01-add-to-an-existing-app-design.md`

## The implementation choice this plan makes

The spec says which files an add must write. It does not say how, and the two available ways differ enormously in risk.

**Rejected: thread a filter through the write path.** Add something like `OnlyClasses` to `ScaffoldRequest` and have `AppScaffolder` skip per-class files for classes not in it. That path is shared by seven targets, and a filter honoured for WPF but missed for Blazor is a silent defect — a file not written is a compile error at best and a missing screen at worst. The spec names this as the main risk: *"the blast radius is the write path every target shares"*.

**Chosen: scaffold to a temp root, copy what is wanted.** `AppScaffolder` is not touched at all. Generate the full application into a short temporary folder exactly as today, then copy across only the new classes' files plus the entity-enumerating file. Every other file in the real folder is untouched **by construction**, not by getting a filter right seven times — which is precisely what the spec's central test asserts.

This is safe because generated **content does not depend on the output folder**: `AppScaffolder` derives `projectName` from `GeneratedProjectPath.ProjectName(appName, target)` and `rootNamespace` from `customization.RootNamespaceFor(projectName)`. Same app name, same model, same customization ⇒ byte-identical files, wherever they are written. **Task 4 Step 2 verifies exactly that before anything relies on it.**

Its costs, stated: the app is generated twice (once to temp, once already in the user's folder from the earlier run), which includes a `dotnet new`, so an add is slower than a plain generate. And the temp root must be **short** — see the constraint below.

## Global Constraints

- Target framework `net10.0`, `Nullable=enable`, `ImplicitUsings=enable` from `Directory.Build.props`.
- **No new package.**
- Tests here are **MSTest** (`[TestClass]`, `[TestMethod]`, `Assert.AreEqual`). MSTEST0037 rejects `Assert.AreEqual(n, x.Count)` — use `Assert.HasCount(n, x)` — and rejects `Assert.IsFalse(s.Contains(x))` — use `Assert.DoesNotContain(x, s)`.
- New types are `internal`; `CodeGenerator.csproj` has `<InternalsVisibleTo Include="CodeGenerator.Tests" />`. `ModelDelta` is the exception — it lives in `AppScaffolding`, which has no such attribute, so it is `public` there like `EntityRoles`.
- **A generated-app probe must live under a SHORT path.** `Path.GetTempPath()` plus a GUID plus a project name plus `Models/Whatever.cs` blows `MAX_PATH`, and the tell is `MSB3030` claiming a file is missing that exists. Use `C:\cg-t\<8 hex chars>` and delete it in cleanup.
- CI runs `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-restore -warnaserror` then `dotnet test … -c Release --no-build`. A warning is an error.
- **A catalog `Example` is printed on one line: `Example.Length + 11 < 81`, so ≤ 69 characters.**
- **`docs/commands.md` and the manual's chapter-18 appendix are BOTH generated from `CommandDictionary`**, with a different test filter each. Regenerate both, then rebuild the `.docx`.
- **`Assert.AreEqual` on two arrays compares REFERENCES in MSTest**, so it fails on identical
  content. Use `CollectionAssert.AreEqual` for every `byte[]` and `string[]` comparison in
  this plan - and there are three.
- Grep test output for `"^  Failed "` as well as `Failed!` — the summary matches `Failed!` and the per-test line does not.
- Branch is `add-to-an-existing-app`. Commit after every task.

---

### Task 1: What is new, what is not

**Files:**
- Create: `AppScaffolding/Incremental/ModelDelta.cs`
- Test: `CodeGenerator.Tests/App/Incremental/ModelDeltaTests.cs`

**Interfaces:**
- Consumes: `ClassSnapshot` (`CodeGenerationEngine.Core.Relationships`), `ClassDefinition` (`CodeGenerationEngine.Models`).
- Produces, in namespace `AppScaffolding.Incremental`:
  - `public enum ClassFate { New, Unchanged, Changed }`
  - `public sealed record ClassDelta(string Name, ClassFate Fate)`
  - `public static class ModelDelta` with
    `public static IReadOnlyList<ClassDelta> Between(IReadOnlyList<ClassSnapshot> existing, IReadOnlyList<ClassDefinition> proposed)`
    and `public static IReadOnlyList<string> NewNames(IReadOnlyList<ClassDelta> delta)`

`AppScaffolding` already references the engine, so both input types are reachable. Public, not internal, because `AppScaffolding` has no `InternalsVisibleTo`.

- [ ] **Step 1: Write the failing test**

```csharp
using AppScaffolding.Incremental;
using CodeGenerationEngine.Core.Relationships;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App.Incremental;

/// <summary>
/// Which of the proposed classes are already on disk. Pure: no filesystem, no scaffolder.
/// </summary>
[TestClass]
public class ModelDeltaTests
{
    private static ClassSnapshot OnDisk(string name, params string[] properties)
        => new(name, $"{name}.cs",
            [.. properties.Select(p => new PropertyDefinition { Name = p, Type = "string" })]);

    private static ClassDefinition Proposed(string name, params string[] properties)
    {
        var definition = new ClassDefinition { Name = name };

        foreach (var property in properties)
            definition.Properties.Add(new PropertyDefinition { Name = property, Type = "string" });

        return definition;
    }

    [TestMethod]
    public void A_class_not_on_disk_is_new()
    {
        var delta = ModelDelta.Between([OnDisk("Customer", "Name")], [Proposed("Supplier", "Name")]);

        Assert.AreEqual(ClassFate.New, delta.Single(d => d.Name == "Supplier").Fate);
    }

    [TestMethod]
    public void The_same_class_with_the_same_properties_is_unchanged()
    {
        var delta = ModelDelta.Between(
            [OnDisk("Customer", "Name", "Email")], [Proposed("Customer", "Name", "Email")]);

        Assert.AreEqual(ClassFate.Unchanged, delta.Single().Fate);
    }

    [TestMethod]
    public void The_same_class_with_different_properties_has_changed()
    {
        var delta = ModelDelta.Between(
            [OnDisk("Customer", "Name")], [Proposed("Customer", "Name", "Email")]);

        Assert.AreEqual(ClassFate.Changed, delta.Single().Fate);
    }

    /// <summary>Property ORDER is not a change: nothing downstream depends on it, and a reordered
    /// list would report churn nobody can act on.</summary>
    [TestMethod]
    public void Reordered_properties_are_not_a_change()
    {
        var delta = ModelDelta.Between(
            [OnDisk("Customer", "Email", "Name")], [Proposed("Customer", "Name", "Email")]);

        Assert.AreEqual(ClassFate.Unchanged, delta.Single().Fate);
    }

    /// <summary>A class name is also a file name, and Windows file names are case-insensitive —
    /// so `customer` on disk and `Customer` proposed are the same class, not a new one.</summary>
    [TestMethod]
    public void A_name_matches_however_it_is_cased()
    {
        var delta = ModelDelta.Between([OnDisk("customer", "Name")], [Proposed("Customer", "Name")]);

        Assert.AreEqual(ClassFate.Unchanged, delta.Single().Fate);
    }

    /// <summary>
    /// Removing is out of scope, and a class the user wrote by hand is not a discrepancy — it is
    /// their code. Reporting it would invite a "shall I delete this?" the tool must never ask.
    /// </summary>
    [TestMethod]
    public void A_class_on_disk_that_the_model_does_not_have_is_not_reported()
    {
        var delta = ModelDelta.Between(
            [OnDisk("Customer", "Name"), OnDisk("MyOwnHelper", "Thing")],
            [Proposed("Customer", "Name")]);

        Assert.HasCount(1, delta);
        Assert.AreEqual("Customer", delta.Single().Name);
    }

    [TestMethod]
    public void Nothing_on_disk_makes_everything_new()
    {
        var delta = ModelDelta.Between([], [Proposed("A", "X"), Proposed("B", "Y")]);

        Assert.HasCount(2, delta);
        Assert.IsTrue(delta.All(d => d.Fate == ClassFate.New));
    }

    [TestMethod]
    public void NewNames_are_the_new_ones_in_order()
    {
        var delta = ModelDelta.Between(
            [OnDisk("Customer", "Name")],
            [Proposed("Customer", "Name"), Proposed("Supplier", "Name"), Proposed("Order", "Placed")]);

        // CollectionAssert, not Assert.AreEqual: MSTest compares arrays by REFERENCE, so
        // AreEqual on two arrays fails even when their contents match.
        CollectionAssert.AreEqual(
            new[] { "Supplier", "Order" }, ModelDelta.NewNames(delta).ToArray());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ModelDeltaTests"`
Expected: FAIL to compile — `AppScaffolding.Incremental` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using CodeGenerationEngine.Core.Relationships;
using CodeGenerationEngine.Models;

namespace AppScaffolding.Incremental;

/// <summary>What a proposed class turns out to be, against what is already on disk.</summary>
public enum ClassFate
{
    /// <summary>No class of this name is there.</summary>
    New,

    /// <summary>There, with the same properties.</summary>
    Unchanged,

    /// <summary>There, with different properties. Adding will not reconcile it.</summary>
    Changed
}

public sealed record ClassDelta(string Name, ClassFate Fate);

/// <summary>
/// Which of a proposed model's classes are already in a generated app.
/// </summary>
/// <remarks>
/// Pure, so it can be asserted without a filesystem or a scaffolder — the same reason
/// <see cref="Screens.EntityRoles"/> and <c>ScreenOverrides</c> are pure.
/// <para>
/// Compared on property NAMES, not types or order. A type change is worth knowing about, but
/// comparing types would have every run of a differently-versioned generator report churn nobody
/// can act on, and this exists to decide what to write rather than to audit.
/// </para>
/// </remarks>
public static class ModelDelta
{
    public static IReadOnlyList<ClassDelta> Between(
        IReadOnlyList<ClassSnapshot> existing,
        IReadOnlyList<ClassDefinition> proposed)
    {
        // Case-insensitive: a class name is also a file name, and Windows file names are.
        var onDisk = new Dictionary<string, ClassSnapshot>(StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in existing)
            onDisk[snapshot.Name] = snapshot;

        var delta = new List<ClassDelta>();

        foreach (var definition in proposed)
        {
            if (!onDisk.TryGetValue(definition.Name, out var snapshot))
            {
                delta.Add(new ClassDelta(definition.Name, ClassFate.New));
                continue;
            }

            delta.Add(new ClassDelta(
                definition.Name,
                SameProperties(snapshot, definition) ? ClassFate.Unchanged : ClassFate.Changed));
        }

        // Classes on disk that the model does not have are deliberately absent. Removing is out
        // of scope, and a class the user wrote by hand is not a discrepancy - it is their code.
        return delta;
    }

    public static IReadOnlyList<string> NewNames(IReadOnlyList<ClassDelta> delta)
        => [.. delta.Where(d => d.Fate == ClassFate.New).Select(d => d.Name)];

    private static bool SameProperties(ClassSnapshot snapshot, ClassDefinition definition)
    {
        var there = snapshot.Properties
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var wanted = definition.Properties
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return there.SetEquals(wanted);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ModelDeltaTests"`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Incremental/ModelDelta.cs CodeGenerator.Tests/App/Incremental/ModelDeltaTests.cs
git commit -m "feat: which proposed classes are already in the app"
```

---

### Task 2: A fourth answer at the guard

**Files:**
- Modify: `CodeGenerator/App/OverwritePrompt.cs`
- Test: `CodeGenerator.Tests/App/OverwritePromptTests.cs`

**Interfaces:**
- Consumes: `ClassDelta`, `ClassFate` (Task 1).
- Produces: `OverwriteAction.Add`, and a new overload
  `static IReadOnlyList<string> Warning(string projectFolder, IReadOnlyList<ClassDelta> delta)`.

The existing `Warning(string)` stays and keeps its current text, because a target with no delta to report (or a caller that has none) must not have its message changed.

- [ ] **Step 1: Write the failing test**

Append to `OverwritePromptTests`:

```csharp
    [TestMethod]
    public void Add_is_an_answer()
    {
        Assert.AreEqual(OverwriteAction.Add, OverwritePrompt.Parse("a"));
        Assert.AreEqual(OverwriteAction.Add, OverwritePrompt.Parse("add"));
    }

    /// <summary>
    /// An answer that would do nothing must not be on the menu. Offering "add" when the model has
    /// nothing new in it is a lie about what the tool can do.
    /// </summary>
    [TestMethod]
    public void Add_is_not_offered_when_nothing_is_new()
    {
        var delta = new[]
        {
            new ClassDelta("Customer", ClassFate.Unchanged),
            new ClassDelta("Order", ClassFate.Changed),
        };

        var lines = string.Join("\n", OverwritePrompt.Warning(@"D:\Apps\Shop.Wpf", delta));

        Assert.DoesNotContain("[a]dd", lines);
        StringAssert.Contains(lines, "[o]verwrite");
    }

    [TestMethod]
    public void Add_is_offered_with_a_count_when_something_is_new()
    {
        var delta = new[]
        {
            new ClassDelta("Customer", ClassFate.Unchanged),
            new ClassDelta("Supplier", ClassFate.New),
            new ClassDelta("PurchaseOrder", ClassFate.New),
        };

        var lines = string.Join("\n", OverwritePrompt.Warning(@"D:\Apps\Shop.Wpf", delta));

        StringAssert.Contains(lines, "[a]dd the 2 new");
        StringAssert.Contains(lines, "3");   // this model has 3 classes
    }

    /// <summary>
    /// A changed class is named, because adding will not reconcile it and overwriting would. The
    /// prompt must not let someone choose "add" believing it will.
    /// </summary>
    [TestMethod]
    public void A_changed_class_is_named_and_its_fate_stated()
    {
        var delta = new[]
        {
            new ClassDelta("Customer", ClassFate.Changed),
            new ClassDelta("Supplier", ClassFate.New),
        };

        var lines = string.Join("\n", OverwritePrompt.Warning(@"D:\Apps\Shop.Wpf", delta));

        StringAssert.Contains(lines, "Customer");
        StringAssert.Contains(lines, "differs");
    }

    /// <summary>Enter still cancels: the effortless answer has to be the safe one, and a closed
    /// pipe is the absence of an answer rather than consent.</summary>
    [TestMethod]
    public void Enter_still_cancels_with_add_on_the_menu()
        => Assert.AreEqual(OverwriteAction.Cancel, OverwritePrompt.Parse(string.Empty));
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~OverwritePromptTests"`
Expected: FAIL to compile — no `OverwriteAction.Add`, no two-argument `Warning`.

- [ ] **Step 3: Write minimal implementation**

Add to `OverwriteAction`:

```csharp
    /// <summary>
    /// Write only the classes this model has that the app does not, plus whatever has to change
    /// for them to appear. Every other generated file is left as it is.
    /// </summary>
    Add,
```

Add to `Parse`, before the existing arms:

```csharp
            "a" or "add" => OverwriteAction.Add,
```

And the new overload beside the existing `Warning`:

```csharp
    /// <summary>
    /// As <see cref="Warning(string)"/>, also saying what this model has that the app does not —
    /// and offering to write just that.
    /// </summary>
    /// <remarks>
    /// <c>Add</c> appears only when something is new. An answer that would do nothing must not be
    /// on the menu.
    /// <para>
    /// A CHANGED class is named rather than glossed. Adding will not reconcile it and overwriting
    /// would, so someone choosing "add" has to be able to see that Customer stays as it is.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Warning(
        string projectFolder, IReadOnlyList<ClassDelta> delta)
    {
        var onDisk = delta.Count(d => d.Fate != ClassFate.New);
        var isNew = delta.Count(d => d.Fate == ClassFate.New);
        var changed = delta.Where(d => d.Fate == ClassFate.Changed).Select(d => d.Name).ToList();

        var lines = new List<string>
        {
            string.Empty,
            $"{projectFolder} already holds a generated project.",
            $"It has {onDisk} class(es); this model has {delta.Count}, of which {isNew} "
            + (isNew == 1 ? "is new." : "are new."),
        };

        if (changed.Count > 0)
        {
            lines.Add(
                $"{Count(changed.Count, "class")} differs: {string.Join(", ", changed)}. "
                + "Adding leaves them as they are.");
        }

        lines.AddRange(
        [
            string.Empty,
            "Regenerating overwrites its generated sources — Models, Data, Views and the",
            "*.Generated.cs validator halves. Your .Custom.cs rules, the project file and",
            "anything you added yourself are kept.",
            string.Empty,
            isNew > 0
                ? $"  [a]dd the {isNew} new   [o]verwrite all   [r]ename   [c]ancel   (Enter cancels)"
                : "  [o]verwrite   [r]ename   [c]ancel   (Enter cancels)",
        ]);

        return lines;
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}es";
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~OverwritePromptTests"`
Expected: PASS, all of them — including the pre-existing tests for the one-argument `Warning`, which must be unchanged.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/App/OverwritePrompt.cs CodeGenerator.Tests/App/OverwritePromptTests.cs
git commit -m "feat: the overwrite guard can offer to add just what is new"
```

---

### Task 3: Which files an add copies

**Files:**
- Create: `AppScaffolding/Incremental/IncrementalFiles.cs`
- Test: `CodeGenerator.Tests/App/Incremental/IncrementalFilesTests.cs`

**Interfaces:**
- Consumes: `ITargetScaffolder`, `PersistenceKind`.
- Produces: `public static class IncrementalFiles` with
  `public static bool ShouldCopy(string relativePath, IReadOnlyList<string> newClassNames, ITargetScaffolder target, PersistenceKind persistence)`.

Pure — it decides on a **relative path string**, so every rule is testable with no filesystem at all. This is the whole of the "which files" decision, in one place, matching the spec's three rules.

- [ ] **Step 1: Write the failing test**

```csharp
using AppScaffolding;
using AppScaffolding.Incremental;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App.Incremental;

/// <summary>
/// The three rules from the design, as a decision on one relative path.
/// </summary>
[TestClass]
public class IncrementalFilesTests
{
    private static readonly string[] TwoNew = ["Supplier", "PurchaseOrder"];

    private static bool Copy(string path, PersistenceKind persistence = PersistenceKind.Json)
        => IncrementalFiles.ShouldCopy(path, TwoNew, new WpfTarget(), persistence);

    [TestMethod]
    public void A_new_class_model_is_copied()
        => Assert.IsTrue(Copy(@"Models\Supplier.cs"));

    [TestMethod]
    public void An_existing_class_model_is_not()
        => Assert.IsFalse(Copy(@"Models\Customer.cs"));

    /// <summary>Every file a new class owns, not only its model — the screen, the view model, the
    /// validator, the repository and the seed all carry its name.</summary>
    [TestMethod]
    public void Every_file_named_for_a_new_class_is_copied()
    {
        Assert.IsTrue(Copy(@"Views\SupplierScreen.xaml"));
        Assert.IsTrue(Copy(@"Views\SupplierScreen.xaml.cs"));
        Assert.IsTrue(Copy(@"ViewModels\SupplierViewModel.cs"));
        Assert.IsTrue(Copy(@"Validation\SupplierValidator.Generated.cs"));
        Assert.IsTrue(Copy(@"Data\Seed\PurchaseOrder.json"));
    }

    [TestMethod]
    public void No_file_named_for_an_existing_class_is_copied()
    {
        Assert.IsFalse(Copy(@"Views\CustomerScreen.xaml"));
        Assert.IsFalse(Copy(@"ViewModels\CustomerViewModel.cs"));
        Assert.IsFalse(Copy(@"Data\Seed\Customer.json"));
    }

    /// <summary>
    /// Rule 2. MainViewModel is the single list the shell and dashboard are built from, so it has
    /// to change for a new screen to exist at all.
    /// </summary>
    [TestMethod]
    public void The_entity_list_is_copied_for_a_target_with_screens()
        => Assert.IsTrue(Copy(@"ViewModels\MainViewModel.cs"));

    [TestMethod]
    public void The_entity_list_is_not_copied_for_a_target_with_no_screens()
        => Assert.IsFalse(IncrementalFiles.ShouldCopy(
            @"ViewModels\MainViewModel.cs", TwoNew, new ConsoleTarget(), PersistenceKind.Json));

    /// <summary>Rule 3, and independent of rule 2: a Web API has no screens and still has a
    /// context.</summary>
    [TestMethod]
    public void The_context_is_copied_only_under_sqlite()
    {
        Assert.IsTrue(Copy(@"Data\AppDbContext.cs", PersistenceKind.Sqlite));
        Assert.IsFalse(Copy(@"Data\AppDbContext.cs"));

        Assert.IsTrue(IncrementalFiles.ShouldCopy(
            @"Data\AppDbContext.cs", TwoNew, new WebApiTarget(), PersistenceKind.Sqlite));
    }

    /// <summary>
    /// Everything else is left alone. This is the assertion that makes the feature what it is —
    /// a shell, a project file, a hand-written custom validator, all untouched.
    /// </summary>
    [TestMethod]
    public void Nothing_else_is_copied()
    {
        Assert.IsFalse(Copy(@"ViewModels\ShellViewModel.cs"));
        Assert.IsFalse(Copy(@"MainWindow.xaml"));
        Assert.IsFalse(Copy(@"App.xaml.cs"));
        Assert.IsFalse(Copy(@"Shop.Wpf.csproj"));
        Assert.IsFalse(Copy(@"Validation\CustomerValidator.Custom.cs"));
        Assert.IsFalse(Copy(@"Auth\AppRole.cs"));
    }

    /// <summary>
    /// A class called Order must not make OrderLine look like its file. The match is on the file
    /// name's leading identifier, not a substring.
    /// </summary>
    [TestMethod]
    public void A_longer_name_beginning_with_a_new_one_is_not_matched()
    {
        var justOrder = new[] { "Order" };

        Assert.IsTrue(IncrementalFiles.ShouldCopy(
            @"Models\Order.cs", justOrder, new WpfTarget(), PersistenceKind.Json));

        Assert.IsFalse(IncrementalFiles.ShouldCopy(
            @"Models\OrderLine.cs", justOrder, new WpfTarget(), PersistenceKind.Json));
    }

    /// <summary>Paths arrive from Directory enumeration, so the separator is whatever the platform
    /// used; the rules must not depend on it.</summary>
    [TestMethod]
    public void A_forward_slash_path_reads_the_same()
        => Assert.IsTrue(Copy("Models/Supplier.cs"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~IncrementalFilesTests"`
Expected: FAIL to compile — `IncrementalFiles` does not exist.

**Before implementing, confirm the real file names** by generating one app and listing its files (`dotnet run` the CLI, or read `AppScaffolder`'s write calls). The names above are what this codebase appears to emit; if a screen is `SupplierView.xaml` rather than `SupplierScreen.xaml` the rule is unaffected — it matches the leading identifier — but the test should name the real files.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace AppScaffolding.Incremental;

/// <summary>
/// Whether one generated file belongs to an incremental add.
/// </summary>
/// <remarks>
/// The whole of the "which files" decision, in one pure function over a RELATIVE PATH — so every
/// rule is assertable with no filesystem, no scaffolder and no target actually writing anything.
/// <para>
/// Three rules, from the design. Always the files a new class owns; the entity-enumerating file
/// when the target has screens, because that list is what a screen has to appear in; and the
/// context under SQLite, independently, because a Web API has no screens and still has one.
/// </para>
/// </remarks>
public static class IncrementalFiles
{
    /// <summary>The file that enumerates entities, per target family. Rule 2.</summary>
    private const string DesktopEntityList = "MainViewModel.cs";

    /// <summary>Rule 3.</summary>
    private const string Context = "AppDbContext.cs";

    public static bool ShouldCopy(
        string relativePath,
        IReadOnlyList<string> newClassNames,
        ITargetScaffolder target,
        PersistenceKind persistence)
    {
        var fileName = Path.GetFileName(Normalized(relativePath));

        // Rule 3 first: it does not depend on the target's screens, and a context named after no
        // class would otherwise fall through to rule 1 and be refused.
        if (persistence == PersistenceKind.Sqlite
            && string.Equals(fileName, Context, StringComparison.OrdinalIgnoreCase))
            return true;

        // Rule 2.
        if (target.HasScreens
            && string.Equals(fileName, DesktopEntityList, StringComparison.OrdinalIgnoreCase))
            return true;

        // Rule 1.
        return newClassNames.Any(name => BelongsTo(fileName, name));
    }

    /// <summary>
    /// Whether <paramref name="fileName"/> is one of <paramref name="className"/>'s files.
    /// </summary>
    /// <remarks>
    /// The leading identifier, not a substring: a new class called Order must not claim
    /// <c>OrderLine.cs</c>. The character after the name has to be a '.' or an upper-case letter,
    /// which covers <c>Order.cs</c>, <c>OrderScreen.xaml</c>, <c>OrderViewModel.cs</c> and
    /// <c>OrderValidator.Generated.cs</c> while refusing <c>OrderLine</c> — whose next character
    /// is 'L', upper-case, so the rule needs the whole remainder to be a KNOWN suffix rather than
    /// merely capitalised.
    /// </remarks>
    private static bool BelongsTo(string fileName, string className)
    {
        if (!fileName.StartsWith(className, StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = fileName[className.Length..];

        if (rest.StartsWith('.'))
            return true;

        // Everything the generator appends to a class name. Anything else - OrderLine's "Line" -
        // is a different class, not a file of this one.
        foreach (var suffix in Suffixes)
            if (rest.StartsWith(suffix, StringComparison.Ordinal))
                return true;

        return false;
    }

    /// <summary>What the generator appends to a class name when it names a file after it.</summary>
    private static readonly string[] Suffixes =
        ["Screen", "View", "ViewModel", "Validator", "Repository", "Store", "Form", "Page"];

    private static string Normalized(string path) => path.Replace('/', Path.DirectorySeparatorChar);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~IncrementalFilesTests"`
Expected: PASS, 11 tests.

**If `Suffixes` turns out to be missing one the generator uses**, a file of a new class is silently left behind — which is the defect shape this whole plan is guarding against. Task 4 Step 3's on-disk test is what catches it: it asserts every file the full generate produced for a new class was copied.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Incremental/IncrementalFiles.cs CodeGenerator.Tests/App/Incremental/IncrementalFilesTests.cs
git commit -m "feat: which generated files an add is allowed to write"
```

---

### Task 4: Writing the add

**Files:**
- Create: `AppScaffolding/Incremental/IncrementalScaffolder.cs`
- Test: `CodeGenerator.Tests/App/Incremental/IncrementalScaffolderTests.cs`

**Interfaces:**
- Consumes: `AppScaffolder`, `ScaffoldRequest`, `ScaffoldResult`, `IncrementalFiles` (Task 3), `ModelDelta` (Task 1).
- Produces: `public sealed record IncrementalResult(bool Success, IReadOnlyList<string> Written, int UntouchedFiles, string Message)` and
  `public static class IncrementalScaffolder` with
  `public static IncrementalResult Add(ScaffoldRequest request, IReadOnlyList<string> newClassNames)`.

**How it works, and why:** scaffold the full application into a short temporary root using the *same* `ScaffoldRequest` (same app name, same model, same customization), then copy across only the paths `IncrementalFiles.ShouldCopy` allows. `AppScaffolder` is not modified, so no target's write path can be broken by this feature. The existing folder's other files are untouched **by construction**.

- [ ] **Step 1: Write the failing test**

```csharp
using AppScaffolding;
using AppScaffolding.Incremental;
using AppScaffolding.Targets;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App.Incremental;

/// <summary>
/// The claim the whole feature rests on: an add writes the new classes and leaves everything else
/// byte-identical.
/// </summary>
/// <remarks>
/// A real generate to a real folder. Under C:\cg-t\ and not the temp path, because a GUID plus a
/// project name plus Models\Whatever.cs blows MAX_PATH and the tell is MSB3030 claiming a file is
/// missing that is plainly there.
/// </remarks>
[TestClass]
public class IncrementalScaffolderTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(@"C:\cg-t", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A generated project's obj folder can hold a lock briefly. A leftover under
            // C:\cg-t is not worth failing a green test over.
        }
    }

    private static CandidateDomainModel Model(params string[] names)
    {
        var model = new CandidateDomainModel();

        foreach (var name in names)
        {
            var entity = new CandidateEntity { Name = name, Provenance = Provenance.Wiki };
            entity.Fields.Add(new CandidateField
            {
                Name = "Name", Type = "string", Provenance = Provenance.Wiki
            });
            model.Entities.Add(entity);
        }

        return model;
    }

    [TestMethod]
    public void An_add_writes_the_new_class_and_leaves_the_rest_alone()
    {
        var target = new WpfTarget();

        // The app as it stands.
        var first = new AppScaffolder().Scaffold(
            ScaffoldRequest.For(Model("Customer", "Order"), "Shop", _root, target));

        Assert.IsTrue(first.Success, first.Message);

        var folder = GeneratedProjectPath.Folder(_root, "Shop", target);
        var customerPath = Path.Combine(folder, "Models", "Customer.cs");

        Assert.IsTrue(File.Exists(customerPath));

        var customerBefore = File.ReadAllBytes(customerPath);
        var before = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length;

        // The same app, with one more class.
        var result = IncrementalScaffolder.Add(
            ScaffoldRequest.For(Model("Customer", "Order", "Supplier"), "Shop", _root, target),
            ["Supplier"]);

        Assert.IsTrue(result.Success, result.Message);

        // The new class is there.
        Assert.IsTrue(File.Exists(Path.Combine(folder, "Models", "Supplier.cs")));

        // THE ASSERTION THIS FEATURE EXISTS FOR.
        // CollectionAssert, because Assert.AreEqual on two byte[] compares references and
        // would fail even on identical content - a false red that would send someone
        // hunting a bug in the copy instead of in the test.
        CollectionAssert.AreEqual(
            customerBefore,
            File.ReadAllBytes(customerPath),
            "an existing class was rewritten by an add");

        Assert.IsGreaterThan(before, 
            Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public void The_entity_list_is_rewritten_so_the_new_screen_exists()
    {
        var target = new WpfTarget();

        new AppScaffolder().Scaffold(
            ScaffoldRequest.For(Model("Customer"), "Shop", _root, target));

        var folder = GeneratedProjectPath.Folder(_root, "Shop", target);
        var list = Path.Combine(folder, "ViewModels", "MainViewModel.cs");

        Assert.IsTrue(File.Exists(list), "the entity list is not where this test expects it");

        IncrementalScaffolder.Add(
            ScaffoldRequest.For(Model("Customer", "Supplier"), "Shop", _root, target),
            ["Supplier"]);

        StringAssert.Contains(File.ReadAllText(list), "Supplier");
    }

    /// <summary>
    /// Every file the full generate produced for the new class must have been copied. This is what
    /// catches a missing entry in IncrementalFiles.Suffixes - a file left behind is silent
    /// otherwise, until the project does not compile.
    /// </summary>
    [TestMethod]
    public void No_file_of_a_new_class_is_left_behind()
    {
        var target = new WpfTarget();

        new AppScaffolder().Scaffold(
            ScaffoldRequest.For(Model("Customer"), "Shop", _root, target));

        var folder = GeneratedProjectPath.Folder(_root, "Shop", target);

        IncrementalScaffolder.Add(
            ScaffoldRequest.For(Model("Customer", "Supplier"), "Shop", _root, target),
            ["Supplier"]);

        // A full generate of the same model, elsewhere, is the reference.
        var reference = Path.Combine(_root, "ref");
        Directory.CreateDirectory(reference);

        new AppScaffolder().Scaffold(
            ScaffoldRequest.For(Model("Customer", "Supplier"), "Shop", reference, target));

        var referenceFolder = GeneratedProjectPath.Folder(reference, "Shop", target);

        var missing = Directory
            .GetFiles(referenceFolder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(referenceFolder, f))
            .Where(r => Path.GetFileName(r).StartsWith("Supplier", StringComparison.Ordinal))
            .Where(r => !File.Exists(Path.Combine(folder, r)))
            .ToList();

        Assert.IsEmpty(missing, $"files of the new class were not copied: {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void Adding_nothing_writes_nothing()
    {
        var target = new WpfTarget();

        new AppScaffolder().Scaffold(
            ScaffoldRequest.For(Model("Customer"), "Shop", _root, target));

        var result = IncrementalScaffolder.Add(
            ScaffoldRequest.For(Model("Customer"), "Shop", _root, target), []);

        Assert.IsEmpty(result.Written);
    }
}
```

- [ ] **Step 2: Verify the assumption this whole approach rests on**

Before implementing, prove that generated content does not depend on the output folder. A throwaway test, or a one-off run:

```csharp
    [TestMethod]
    public void Generated_content_does_not_depend_on_the_output_folder()
    {
        var target = new WpfTarget();
        var a = Path.Combine(_root, "a");
        var b = Path.Combine(_root, "b");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);

        new AppScaffolder().Scaffold(ScaffoldRequest.For(Model("Customer"), "Shop", a, target));
        new AppScaffolder().Scaffold(ScaffoldRequest.For(Model("Customer"), "Shop", b, target));

        var one = Path.Combine(GeneratedProjectPath.Folder(a, "Shop", target), "Models", "Customer.cs");
        var two = Path.Combine(GeneratedProjectPath.Folder(b, "Shop", target), "Models", "Customer.cs");

        CollectionAssert.AreEqual(File.ReadAllBytes(one), File.ReadAllBytes(two));
    }
```

Run it. **If it fails, stop and report** — the temp-and-copy approach is invalid and the plan needs revisiting, because a copied file would differ from what a full generate in place would have written. Keep this test: it is the guard on the approach, not scaffolding for it.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace AppScaffolding.Incremental;

/// <summary>What an add did.</summary>
public sealed record IncrementalResult(
    bool Success,
    IReadOnlyList<string> Written,
    int UntouchedFiles,
    string Message);

/// <summary>
/// Writes only what an application is missing.
/// </summary>
/// <remarks>
/// Scaffolds the whole application into a temporary root and copies across only the files
/// <see cref="IncrementalFiles.ShouldCopy"/> allows. <see cref="AppScaffolder"/> is deliberately
/// not modified: its write path is shared by seven targets, and a filter honoured for one and
/// missed for another is a silent defect — a file not written is a compile error at best and a
/// missing screen at worst. Copying instead makes "every other file is untouched" true by
/// construction rather than by getting a filter right seven times.
/// <para>
/// This relies on generated CONTENT being independent of the output folder, which it is:
/// <c>projectName</c> comes from the app name and <c>rootNamespace</c> from that, never from the
/// path. There is a test named for that assumption; it is a guard on this approach, not scaffolding.
/// </para>
/// </remarks>
public static class IncrementalScaffolder
{
    public static IncrementalResult Add(
        ScaffoldRequest request, IReadOnlyList<string> newClassNames)
    {
        var destination = GeneratedProjectPath.Folder(
            request.WorkspaceRoot, request.AppName, request.Target);

        var before = CountFiles(destination);

        if (newClassNames.Count == 0)
            return new IncrementalResult(true, [], before, "Nothing new to add.");

        // Short, because a GUID plus a project name plus Models\Whatever.cs blows MAX_PATH.
        var staging = Path.Combine(@"C:\cg-t", Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(staging);

            var staged = new AppScaffolder().Scaffold(request with { WorkspaceRoot = staging });

            if (!staged.Success)
                return new IncrementalResult(false, [], before, staged.Message);

            var source = GeneratedProjectPath.Folder(staging, request.AppName, request.Target);

            var written = Copy(source, destination, newClassNames, request);

            return new IncrementalResult(
                true,
                written,
                Math.Max(0, before - written.Count(w => File.Exists(w))),
                $"Added {newClassNames.Count} class(es).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new IncrementalResult(false, [], before, ex.Message);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    private static List<string> Copy(
        string source, string destination, IReadOnlyList<string> newClassNames,
        ScaffoldRequest request)
    {
        var written = new List<string>();

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);

            if (!IncrementalFiles.ShouldCopy(
                    relative, newClassNames, request.Target, request.Persistence))
                continue;

            var target = Path.Combine(destination, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);

            written.Add(target);
        }

        return written;
    }

    private static int CountFiles(string folder)
        => Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length
            : 0;

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A generated project's obj folder can hold a lock. A leftover staging folder is not
            // worth failing an add that has already written its files.
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~IncrementalScaffolderTests"`
Expected: PASS, 5 tests. These run a real `dotnet new` twice per test, so they are slow — a minute or more each is normal.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Incremental/IncrementalScaffolder.cs CodeGenerator.Tests/App/Incremental/IncrementalScaffolderTests.cs
git commit -m "feat: an add writes the new classes and nothing else"
```

---

### Task 5: The generated project still compiles

**Files:**
- Test: `CodeGenerator.Tests/App/Incremental/IncrementalCompileTests.cs`

**Interfaces:** none. This task adds only tests.

**Why its own task:** this is the check that catches the defect class the spec names as the main risk — a composition file that does not know about the new class, or a missing `DbSet`, compiles to a broken app. This codebase's standing rule is that emitted code is proved by compiling it, never by substring.

- [ ] **Step 1: Write the test**

```csharp
using AppScaffolding;
using AppScaffolding.Incremental;
using AppScaffolding.Targets;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App.Incremental;

/// <summary>
/// An added class has to leave a project that still builds — for EVERY target with screens, not
/// just the one the feature was demonstrated on. Composition differs per target, and a rule right
/// for WPF and wrong for Blazor is exactly the defect shape this repo keeps producing.
/// </summary>
[TestClass]
public class IncrementalCompileTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(@"C:\cg-t", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
    }

    [TestMethod]
    public void A_wpf_app_still_builds_after_an_add() => StillBuilds(new WpfTarget());

    [TestMethod]
    public void A_winforms_app_still_builds_after_an_add() => StillBuilds(new WinFormsTarget());

    [TestMethod]
    public void A_blazor_app_still_builds_after_an_add() => StillBuilds(new BlazorTarget());

    private void StillBuilds(ITargetScaffolder target)
    {
        var first = new AppScaffolder().Scaffold(
            ScaffoldRequest.For(Model("Customer"), "Shop", _root, target));

        Assert.IsTrue(first.Success, first.Message);

        var result = IncrementalScaffolder.Add(
            ScaffoldRequest.For(Model("Customer", "Supplier"), "Shop", _root, target),
            ["Supplier"]);

        Assert.IsTrue(result.Success, result.Message);

        var csproj = GeneratedProjectPath.CsprojPath(_root, "Shop", target);

        // Use whatever this suite already uses to build a generated project - see
        // GeneratedProjectBuildTests. Do not invent a second way to run dotnet build.
        var build = GeneratedProjectBuild.Run(csproj);

        Assert.IsTrue(build.Success, $"{target.Key} did not build after an add:\n{build.Output}");
    }

    private static CandidateDomainModel Model(params string[] names)
    {
        var model = new CandidateDomainModel();

        foreach (var name in names)
        {
            var entity = new CandidateEntity { Name = name, Provenance = Provenance.Wiki };
            entity.Fields.Add(new CandidateField
            {
                Name = "Name", Type = "string", Provenance = Provenance.Wiki
            });
            model.Entities.Add(entity);
        }

        return model;
    }
}
```

- [ ] **Step 2: Find the existing build helper and use it**

`GeneratedProjectBuild.Run` above is a placeholder name. Read `CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs` and use whatever it already uses to compile a generated project. **Do not add a second way to run `dotnet build`** — this suite already has one, and two would drift.

- [ ] **Step 3: Run the tests**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~IncrementalCompileTests"`
Expected: PASS for all three targets. These are slow — three generates plus three builds.

**If one target fails and the others pass**, that is the per-target seam the spec warned about. The fix belongs in `IncrementalFiles` — that target's entity-enumerating file is not `MainViewModel.cs` — not in `IncrementalScaffolder`.

- [ ] **Step 4: Commit**

```bash
git add CodeGenerator.Tests/App/Incremental/IncrementalCompileTests.cs
git commit -m "test: a project still builds after an add, on every target with screens"
```

---

### Task 6: Offering it, and reporting it

**Files:**
- Modify: `CodeGenerator/App/BuildAppCommand.cs`
- Test: `CodeGenerator.Tests/App/Incremental/AddReportTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `internal static class AddReport` with
  `static IReadOnlyList<string> Lines(IncrementalResult result, string projectFolder)`.

The report is its own pure type so its wording is testable; the console loop that prints it is not.

- [ ] **Step 1: Write the failing test**

```csharp
using AppScaffolding.Incremental;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App.Incremental;

[TestClass]
public class AddReportTests
{
    [TestMethod]
    public void Names_every_file_written()
    {
        var result = new IncrementalResult(
            true,
            [@"D:\Apps\Shop.Wpf\Models\Supplier.cs", @"D:\Apps\Shop.Wpf\ViewModels\MainViewModel.cs"],
            41,
            "Added 1 class(es).");

        var text = string.Join("\n", AddReport.Lines(result, @"D:\Apps\Shop.Wpf"));

        StringAssert.Contains(text, @"Models\Supplier.cs");
        StringAssert.Contains(text, @"ViewModels\MainViewModel.cs");
    }

    /// <summary>
    /// The half a generator never usually says, and the half a user actually wants verified.
    /// </summary>
    [TestMethod]
    public void Says_how_many_files_it_did_not_touch()
    {
        var result = new IncrementalResult(
            true, [@"D:\Apps\Shop.Wpf\Models\Supplier.cs"], 41, "Added 1 class(es).");

        var text = string.Join("\n", AddReport.Lines(result, @"D:\Apps\Shop.Wpf"));

        StringAssert.Contains(text, "41");
        StringAssert.Contains(text, "untouched");
    }

    [TestMethod]
    public void A_failure_says_what_went_wrong_and_claims_nothing()
    {
        var result = new IncrementalResult(false, [], 41, "Access to the path is denied.");

        var text = string.Join("\n", AddReport.Lines(result, @"D:\Apps\Shop.Wpf"));

        StringAssert.Contains(text, "Access to the path is denied.");
        Assert.DoesNotContain("untouched", text);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~AddReportTests"`
Expected: FAIL to compile — `AddReport` does not exist.

- [ ] **Step 3: Write the report**

```csharp
using AppScaffolding.Incremental;

namespace CodeGenerator.App;

/// <summary>
/// What an add did, for the console.
/// </summary>
/// <remarks>
/// Its own type so the wording is testable — the loop that prints it is not. The untouched count
/// is here deliberately: it is the half a user wants verified and the half a generator never
/// usually says.
/// </remarks>
internal static class AddReport
{
    public static IReadOnlyList<string> Lines(IncrementalResult result, string projectFolder)
    {
        if (!result.Success)
            return [string.Empty, $"  Nothing was added: {result.Message}"];

        var lines = new List<string> { string.Empty, "  Written:" };

        foreach (var file in result.Written)
            lines.Add("    " + Path.GetRelativePath(projectFolder, file));

        lines.Add(string.Empty);
        lines.Add($"  {result.UntouchedFiles} file(s) untouched.");

        return lines;
    }
}
```

- [ ] **Step 4: Wire the Add branch into the command**

In `BuildAppCommand`, the overwrite loop is around line 845:

```csharp
        while (GeneratedProjectPath.Exists(workspaceRoot, name, target))
```

Inside it, the warning call becomes the two-argument overload, with the delta read from disk:

```csharp
            var existing = CodeGenerationEngine.Core.Relationships.ClassSnapshots.Read(
                Path.Combine(
                    GeneratedProjectPath.Folder(workspaceRoot, name, target), "Models"));

            var delta = AppScaffolding.Incremental.ModelDelta.Between(
                existing, DomainModelMapper.ToClassDefinitions(model));

            foreach (var line in OverwritePrompt.Warning(
                         GeneratedProjectPath.Folder(workspaceRoot, name, target), delta))
                Console.WriteLine(line);
```

and a new arm beside `Overwrite` and `Cancel`:

```csharp
            if (action == OverwriteAction.Add)
            {
                var added = AppScaffolding.Incremental.IncrementalScaffolder.Add(
                    request, AppScaffolding.Incremental.ModelDelta.NewNames(delta));

                foreach (var line in AddReport.Lines(
                             added,
                             GeneratedProjectPath.Folder(workspaceRoot, name, target)))
                    Console.WriteLine(line);

                return;
            }
```

**Read the surrounding code before writing this.** The loop's exact shape — where `request` is built, whether `model` is in scope, how `Overwrite` breaks out — decides where these lines go. The names above are what this plan expects; the file is the authority.

**`Add` must be unreachable on a piped run.** `AskOverwriteAction` already handles redirected stdin (the loop checks `Console.IsInputRedirected` at line 847); make sure a piped run cannot fall into the `Add` arm, for the same reason a piped run does not read a profile.

- [ ] **Step 5: Verify the piped paths are untouched**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~BuildAppDataSourceTests|FullyQualifiedName~BuildAppWizardTests|FullyQualifiedName~OverwritePromptTests"`
Expected: PASS, unchanged.

- [ ] **Step 6: Run it, do not only test it**

Generate an app, then run again with one more class and answer `a`:

```
cd C:\cg-t\demo
build app: a shop that sells books
approve wpf
build app: a shop that sells books and has suppliers
approve wpf
   -> [a]dd the 1 new
```

Check the report names the files, the untouched count is plausible, and `Models\Customer.cs` has
the same timestamp it had before. Three defects on the SQL branch were found this way and none by
a test.

- [ ] **Step 7: Commit**

```bash
git add CodeGenerator/App/AddReport.cs CodeGenerator/App/BuildAppCommand.cs CodeGenerator.Tests/App/Incremental/AddReportTests.cs
git commit -m "feat: answering add writes only what is new, and says what it left alone"
```

---

### Task 7: The documentation

**Files:**
- Modify: `CodeGenerationEngine/Core/CommandDictionary.cs`
- Regenerate: `docs/commands.md`
- Modify: `docs/user-manual-source.html`
- Regenerate: `docs/SmartCodeGenerator-User-Manual.docx`

- [ ] **Step 1: Update the catalog**

Add to the `Build App` category:

```csharp
        new()
        {
            Category = "Build App",
            Command = "approve (when the app already exists)",
            Description = "Offers to add only what is new. When the project folder already holds "
                        + "a generated app, the guard now reads the classes already there and "
                        + "compares them with the model you just reviewed: 'It has 8 class(es); "
                        + "this model has 10, of which 2 are new.' Answering 'a' writes those 2 "
                        + "classes and the one file that lists the entities so their screens "
                        + "appear - and nothing else, so every other generated file is left "
                        + "byte-identical. The run says how many files it did not touch. A class "
                        + "that exists with DIFFERENT fields is named and left alone: adding will "
                        + "not reconcile it, overwriting would. 'a' is offered only when something "
                        + "is actually new, and Enter still cancels.",
            Example = "approve wpf"
        },
```

`Example` is `approve wpf` — 11 characters, well inside the 69-character budget.

- [ ] **Step 2: Regenerate both artefacts**

```
UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~CommandReferenceDocTests"
UPDATE_MANUAL_APPENDIX=1 dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ManualAppendix_MatchesTheCatalog"
```

Then confirm clean:

```
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ManualAppendixDocTests|FullyQualifiedName~CommandReferenceDocTests|FullyQualifiedName~CommandHelpRenderingTests"
```

- [ ] **Step 3: Write the manual section**

In the chapter that covers approving and regenerating (the one holding the existing overwrite
guidance), after that section:

```html
<h3>Adding to an app you already made</h3>

<p>Run <code>build app</code> again with more in the description, and when the guard notices the
   project is already there it now says what is different:</p>

<pre>D:\SampleProjects\Shop.Wpf already holds a generated project.
It has 8 class(es); this model has 10, of which 2 are new.

  [a]dd the 2 new   [o]verwrite all   [r]ename   [c]ancel   (Enter cancels)</pre>

<p>Answering <code>a</code> writes those two classes &mdash; model, screen, validator, seed &mdash;
   and the one file that lists the entities, so the new screens appear in the rail. <strong>Nothing
   else is written.</strong> The run tells you how many files it left alone, because that is the
   half worth checking:</p>

<pre>  Written:
    Models\Supplier.cs
    Views\SupplierScreen.xaml
    ViewModels\MainViewModel.cs

  41 file(s) untouched.</pre>

<p>It knows what is already there by <strong>reading the generated code</strong>, not by consulting
   a file it wrote earlier. That means it works on apps made by any earlier version of the tool,
   and it cannot be wrong about what is on disk.</p>

<div class="note"><p>A class that is already there with <em>different</em> fields is named and left
   alone. Adding will not reconcile it and overwriting would, so the choice stays yours. And
   <code>a</code> is offered only when the model really does contain something new &mdash; an
   answer that would do nothing is not put on the menu.</p></div>

<p>What it cannot recover from the code is your <em>answers</em>: the theme, the layout and the
   master/transaction split are not in the C#, so they come from the profile in
   <code>.codegen</code> or from what you chose on this run.</p>
```

- [ ] **Step 4: Rebuild and verify the document**

```
powershell -File tools/build-user-manual.ps1
powershell -File tools/verify-user-manual.ps1
```

Expected: a page count, then `All checks passed` with TOC links equal to heading bookmarks.

- [ ] **Step 5: Run the full suite as CI runs it**

```
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
```

Expected: `Build succeeded.` and every project green. The incremental tests add several real
generates and builds, so the suite will be noticeably slower than its usual eight minutes.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerationEngine/Core/CommandDictionary.cs docs/commands.md docs/user-manual-source.html docs/SmartCodeGenerator-User-Manual.docx
git commit -m "docs: adding to an app you already made"
```

---

## Self-review notes

**Spec coverage.** Reading what is there → 6 (scoped `ClassSnapshots.Read`). `ModelDelta` → 1. The fourth answer, and absent when nothing is new → 2. Which files get written, all three rules → 3. Writing them, and the byte-identical claim → 4. Compile check across targets → 5. The report naming written and untouched → 6. Docs → 7. Every spec section has a task.

**One decision this plan makes that the spec left open**, called out at the top: scaffold-to-temp-and-copy rather than a filter threaded through `AppScaffolder`. It is the lower-risk half of a genuine fork — the spec's own main risk is that write path — and Task 4 Step 2 tests the assumption it depends on before anything relies on it.

**Three places a task must read the code rather than trust this plan**, all flagged inline: the real generated file names in Task 3, the existing build helper in Task 5, and the exact shape of the overwrite loop in Task 6. None changes what is being built.

**Known weak point.** `IncrementalFiles.Suffixes` is a hand-written list of what the generator appends to a class name. A missing entry silently leaves a file behind. Task 4's `No_file_of_a_new_class_is_left_behind` is what catches it, by diffing against a full generate — that test is load-bearing, not decorative.

**Ordering.** Tasks 1, 2 and 3 are pure and independent. Task 4 needs 1 and 3. Task 5 needs 4. Task 6 needs everything. Task 7 is last so the catalog's final wording is what gets generated.
