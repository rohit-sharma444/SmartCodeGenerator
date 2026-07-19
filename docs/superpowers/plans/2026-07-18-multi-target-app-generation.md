# Multi-target App Generation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let `build app:` scaffold the discovered domain model as a Console app, Web API, Class library, or Background service in addition to the existing WPF app, chosen by a prompt on `approve`, with the WPF path and piped-input behavior unchanged.

**Architecture:** Introduce an `ITargetScaffolder` strategy. `AppScaffolder.Scaffold` writes the shared core (Models + JsonStore + typed Repositories) and delegates the project type, entry point, and front-end to the selected target. The existing WPF code moves behind `WpfTarget` verbatim (including `RelationshipResolver`, which turns out to be WPF-coupled). A `TargetScaffolders` registry maps a key to a target; `BuildAppCommand` prompts for it on `approve`.

**Tech Stack:** C# / .NET 10, Roslyn-based generation (existing), MSTest (`[TestClass]`/`[TestMethod]`, `StringAssert`, `Assert`, `CollectionAssert`).

## Global Constraints

- Target framework for every generated project: `net10.0` (copied from the current `Scaffold`).
- The WPF path must stay behavior-identical: same files, same namespaces, same `WpfProjectFixup`, same success message `Generated N class(es) + a runnable WPF CRUD app.`
- Piped / redirected CLI input must never prompt and must default to `wpf`, consuming no extra input line (existing scripts/tests unaffected).
- New generated projects live in `<workspaceRoot>/<AppName><FolderSuffix>` (WPF suffix is empty → unchanged).
- Test project: `CodeGenerator.Tests`, tests under `CodeGenerator.Tests/App/`. Production code under `AppScaffolding/` and `CodeGenerator/App/`.
- Follow existing template style: `public static class XxxTemplate` with `$$"""..."""` raw-string interpolation; entity-shaped templates take `(string appName, IReadOnlyList<string> entityNames)`.
- Build/test commands (from repo root `D:\SmartCodeGenerator-Repository`):
  - Build: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx`
  - Test: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`
  - Single test: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~<TestClass>.<TestMethod>"`

---

### Task 1: `ITargetScaffolder` foundation + extract `WpfTarget` + refactor `AppScaffolder`

Establishes the strategy seam and proves the WPF path is unchanged. `RelationshipResolver` moves into `WpfTarget` because it depends on `IEntityViewModel` (a WPF-path type).

**Files:**
- Create: `AppScaffolding/ITargetScaffolder.cs`
- Create: `AppScaffolding/GeneratedFile.cs`
- Create: `AppScaffolding/Targets/WpfTarget.cs`
- Modify: `AppScaffolding/AppScaffolder.cs` (rewrite `Scaffold` to delegate; add backward-compatible overload)
- Test: `CodeGenerator.Tests/App/WpfTargetTests.cs`

**Interfaces:**
- Produces:
  - `record GeneratedFile(string RelativeFolder, string Name, string Source)` — `RelativeFolder` is relative to the project root; `""` means the project root.
  - `interface ITargetScaffolder { string Key { get; } string Description { get; } DotNetProjectType ProjectType { get; } string FolderSuffix { get; } IReadOnlyList<GeneratedFile> FrontEndFiles(string projectName, IReadOnlyList<string> entityNames); void FixupProject(string outputFolder) {} }` (last member is a default no-op interface method).
  - `class WpfTarget : ITargetScaffolder` with `Key="wpf"`, `FolderSuffix=""`, `ProjectType=DotNetProjectType.Wpf`, `Description="a runnable WPF CRUD app"`.
  - `AppScaffolder.Scaffold(CandidateDomainModel model, string appName, string workspaceRoot, ITargetScaffolder target)` and overload `Scaffold(CandidateDomainModel model, string appName, string workspaceRoot)` ⇒ delegates with `new WpfTarget()`.
- Consumes: existing `DomainModelMapper`, `JsonStoreTemplate`, `RepositoryTemplate`, `RelationshipResolverTemplate`, `MainViewModelTemplate`, `ViewModelTemplates`, `MainWindowTemplate`, `WpfProjectFixup`, `DotNetProjectFactory`.

- [ ] **Step 1: Write the failing test** — `CodeGenerator.Tests/App/WpfTargetTests.cs`

```csharp
using AppScaffolding;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WpfTargetTests
{
    [TestMethod]
    public void Metadata_MatchesTheExistingWpfDefaults()
    {
        var target = new WpfTarget();

        Assert.AreEqual("wpf", target.Key);
        Assert.AreEqual(string.Empty, target.FolderSuffix);
        Assert.AreEqual(DotNetProjectType.Wpf, target.ProjectType);
        StringAssert.Contains(target.Description, "WPF");
    }

    [TestMethod]
    public void FrontEndFiles_EmitsTheRelationshipResolverViewModelsAndViews()
    {
        var files = new WpfTarget().FrontEndFiles("Payroll", new[] { "Employee", "Department" });
        var names = files.Select(f => f.RelativeFolder + "/" + f.Name).ToList();

        CollectionAssert.Contains(names, "Data/RelationshipResolver.cs");
        CollectionAssert.Contains(names, "ViewModels/ObservableObject.cs");
        CollectionAssert.Contains(names, "ViewModels/RelayCommand.cs");
        CollectionAssert.Contains(names, "ViewModels/IEntityViewModel.cs");
        CollectionAssert.Contains(names, "ViewModels/EntityViewModel.cs");
        CollectionAssert.Contains(names, "ViewModels/MainViewModel.cs");
        CollectionAssert.Contains(names, "Views/MainWindow.xaml");
        CollectionAssert.Contains(names, "Views/MainWindow.xaml.cs");

        var mainVm = files.Single(f => f.Name == "MainViewModel.cs").Source;
        StringAssert.Contains(mainVm, "new EntityViewModel<Employee>(\"Employee\"),");
        StringAssert.Contains(mainVm, "new EntityViewModel<Department>(\"Department\"),");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~WpfTargetTests"`
Expected: FAIL — `WpfTarget`, `ITargetScaffolder`, `GeneratedFile` do not exist (compile error).

- [ ] **Step 3: Create `GeneratedFile.cs`**

```csharp
namespace AppScaffolding;

/// <summary>A single file a target contributes: its folder relative to the project
/// root (<c>""</c> for the root itself), file name, and source text.</summary>
public sealed record GeneratedFile(string RelativeFolder, string Name, string Source);
```

- [ ] **Step 4: Create `ITargetScaffolder.cs`**

```csharp
using CodeGenerationEngine.Models;

namespace AppScaffolding;

/// <summary>
/// A pluggable output target for <see cref="AppScaffolder"/>. The scaffolder writes the
/// shared core (Models + JsonStore + typed Repositories); a target supplies the project
/// type, the entry point / front-end files, and any post-create project fixup.
/// </summary>
public interface ITargetScaffolder
{
    /// <summary>Stable lower-case key used in the CLI prompt and the folder suffix, e.g. "console".</summary>
    string Key { get; }

    /// <summary>Human phrase completing "Generated N class(es) + &lt;Description&gt;.".</summary>
    string Description { get; }

    /// <summary>The .NET project type this target creates.</summary>
    DotNetProjectType ProjectType { get; }

    /// <summary>Suffix appended after the app name for the project folder and name.
    /// Empty for WPF (so its output is unchanged), ".Console"/".Api"/".Lib"/".Worker" otherwise.</summary>
    string FolderSuffix { get; }

    /// <summary>Target-specific files, given the project name (already suffixed, used as the
    /// namespace root) and the mapped entity class names. The shared Models/ and Data/ files
    /// are written by <see cref="AppScaffolder"/> before this runs.</summary>
    IReadOnlyList<GeneratedFile> FrontEndFiles(string projectName, IReadOnlyList<string> entityNames);

    /// <summary>Optional post-create reconciliation of the files `dotnet new` emitted.
    /// Runs only against a project the scaffolder just created. No-op by default.</summary>
    void FixupProject(string outputFolder) { }
}
```

- [ ] **Step 5: Create `Targets/WpfTarget.cs`** (moves the WPF-specific files, including `RelationshipResolver`, out of `AppScaffolder`)

```csharp
using CodeGenerationEngine.Models;

namespace AppScaffolding.Targets;

/// <summary>The original output target: a runnable WPF CRUD app. Its file set and the
/// WpfProjectFixup are exactly what AppScaffolder produced before targets existed.</summary>
public sealed class WpfTarget : ITargetScaffolder
{
    public string Key => "wpf";
    public string Description => "a runnable WPF CRUD app";
    public DotNetProjectType ProjectType => DotNetProjectType.Wpf;
    public string FolderSuffix => "";

    public IReadOnlyList<GeneratedFile> FrontEndFiles(string projectName, IReadOnlyList<string> entityNames) => new List<GeneratedFile>
    {
        // RelationshipResolver depends on IEntityViewModel, so it is WPF-only, not shared core.
        new("Data", "RelationshipResolver.cs", RelationshipResolverTemplate.Source(projectName)),
        new("ViewModels", "ObservableObject.cs", ViewModelTemplates.ObservableObject(projectName)),
        new("ViewModels", "RelayCommand.cs", ViewModelTemplates.RelayCommand(projectName)),
        new("ViewModels", "IEntityViewModel.cs", ViewModelTemplates.EntityViewModelInterface(projectName)),
        new("ViewModels", "EntityViewModel.cs", ViewModelTemplates.EntityViewModel(projectName)),
        new("ViewModels", "MainViewModel.cs", MainViewModelTemplate.Build(projectName, entityNames)),
        new("Views", "MainWindow.xaml", MainWindowTemplate.Xaml(projectName)),
        new("Views", "MainWindow.xaml.cs", MainWindowTemplate.CodeBehind(projectName)),
    };

    public void FixupProject(string outputFolder) => WpfProjectFixup.Apply(outputFolder);
}
```

**Note for the implementer:** `WpfTarget` lives in namespace `AppScaffolding.Targets`. Add `using AppScaffolding.Targets;` where it is referenced (the test above imports `AppScaffolding`; add the `.Targets` using to the test too). Keep `RelationshipResolverTemplate` etc. in `AppScaffolding` (unchanged).

- [ ] **Step 6: Rewrite `AppScaffolder.Scaffold`** — replace the method body (lines ~52-130) and keep `WriteClasses`/`WriteSource` as-is. New version:

```csharp
    /// <summary>
    /// Creates a project named <c>&lt;appName&gt;&lt;target.FolderSuffix&gt;</c> under
    /// <paramref name="workspaceRoot"/> (reusing it if present) and writes the shared model +
    /// data core plus the front-end supplied by <paramref name="target"/>.
    /// </summary>
    public ScaffoldResult Scaffold(
        CandidateDomainModel model, string appName, string workspaceRoot, ITargetScaffolder target)
    {
        var classes = DomainModelMapper.ToClassDefinitions(model);

        var projectName = appName + target.FolderSuffix;
        var outputFolder = Path.Combine(workspaceRoot, projectName);
        var csprojPath = Path.Combine(outputFolder, projectName + ".csproj");

        if (!File.Exists(csprojPath))
        {
            var created = DotNetProjectFactory.CreateProject(new ProjectRequest
            {
                ProjectName = projectName,
                OutputFolder = outputFolder,
                ProjectType = target.ProjectType,
                Framework = "net10.0"
            });

            if (!created.Success)
                return new ScaffoldResult(
                    false, outputFolder, Array.Empty<string>(),
                    $"Project creation failed: {created.Message}");

            // Only ever touches files just created: the reuse path above skips it, so a
            // project the user has edited is left alone.
            target.FixupProject(outputFolder);
        }

        var modelsFolder = Path.Combine(outputFolder, "Models");
        var dataFolder = Path.Combine(outputFolder, "Data");

        var written = new List<string>(
            WriteClasses(modelsFolder, classes, $"{projectName}.Models"));
        var expected = classes.Count;

        var entityNames = classes.Select(c => c.Name).ToList();

        // Shared, target-agnostic core: JSON store + generic base repository + one typed
        // repository per entity.
        var files = new List<(string Folder, string Name, string Source)>
        {
            (dataFolder, "JsonStore.cs", JsonStoreTemplate.Source($"{projectName}.Data")),
            (dataFolder, "Repository.cs", RepositoryTemplate.Base(projectName)),
        };
        foreach (var name in entityNames)
            files.Add((dataFolder, $"{name}Repository.cs", RepositoryTemplate.Typed(projectName, name)));

        // Target-specific front-end / entry point.
        foreach (var file in target.FrontEndFiles(projectName, entityNames))
            files.Add((Path.Combine(outputFolder, file.RelativeFolder), file.Name, file.Source));

        foreach (var (folder, name, source) in files)
        {
            expected++;
            var path = WriteSource(folder, name, source);
            if (path is not null)
                written.Add(path);
        }

        var allWritten = written.Count == expected;

        var message = allWritten
            ? $"Generated {classes.Count} class(es) + {target.Description}."
            : $"Generated {written.Count} of {expected} file(s); " +
              "some files could not be written.";

        return new ScaffoldResult(allWritten, outputFolder, written, message);
    }

    /// <summary>Backward-compatible overload: scaffolds the WPF target (the historical default).</summary>
    public ScaffoldResult Scaffold(
        CandidateDomainModel model, string appName, string workspaceRoot)
        => Scaffold(model, appName, workspaceRoot, new Targets.WpfTarget());
```

Add `using AppScaffolding.Targets;` is not required here since the overload uses the fully-qualified `Targets.WpfTarget`. Leave existing `using` lines. Remove nothing else.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~WpfTargetTests"`
Expected: PASS (both methods).

- [ ] **Step 8: Run the full existing suite to confirm nothing regressed**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`
Expected: PASS — all pre-existing tests (`AppScaffolderWriteTests`, `MainViewModelTemplateTests`, `DomainModelMapperTests`, etc.) still green.

- [ ] **Step 9: Commit**

```bash
git add AppScaffolding/ITargetScaffolder.cs AppScaffolding/GeneratedFile.cs AppScaffolding/Targets/WpfTarget.cs AppScaffolding/AppScaffolder.cs CodeGenerator.Tests/App/WpfTargetTests.cs
git commit -m "Introduce ITargetScaffolder and extract WpfTarget"
```

---

### Task 2: Console target

A runnable console CRUD app: a generic reflection-driven `EntityMenu<T>` plus a `Program.cs` that registers one menu per entity.

**Files:**
- Create: `AppScaffolding/ConsoleMenuTemplate.cs`
- Create: `AppScaffolding/ConsoleProgramTemplate.cs`
- Create: `AppScaffolding/Targets/ConsoleTarget.cs`
- Test: `CodeGenerator.Tests/App/ConsoleTargetTests.cs`

**Interfaces:**
- Produces:
  - `ConsoleMenuTemplate.Source(string projectName)` → `Data`-adjacent generic menu at `EntityMenu.cs` (project root).
  - `ConsoleProgramTemplate.Build(string projectName, IReadOnlyList<string> entityNames)` → `Program.cs`.
  - `class ConsoleTarget : ITargetScaffolder` — `Key="console"`, `FolderSuffix=".Console"`, `ProjectType=DotNetProjectType.Console`, `Description="a runnable Console CRUD app"`. `FrontEndFiles` returns `EntityMenu.cs` and `Program.cs` at the project root (`RelativeFolder=""`).
- Consumes: `RepositoryTemplate` output (`Repository<T>`, `<Entity>Repository`) and `Models` written by `AppScaffolder`.

- [ ] **Step 1: Write the failing test** — `CodeGenerator.Tests/App/ConsoleTargetTests.cs`

```csharp
using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ConsoleTargetTests
{
    [TestMethod]
    public void Metadata_IsConsole()
    {
        var t = new ConsoleTarget();
        Assert.AreEqual("console", t.Key);
        Assert.AreEqual(".Console", t.FolderSuffix);
        Assert.AreEqual(DotNetProjectType.Console, t.ProjectType);
    }

    [TestMethod]
    public void FrontEndFiles_EmitsProgramAndMenuAtProjectRoot()
    {
        var files = new ConsoleTarget().FrontEndFiles("Shop", new[] { "Book", "Member" });
        var names = files.Select(f => f.RelativeFolder + "|" + f.Name).ToList();

        CollectionAssert.Contains(names, "|Program.cs");
        CollectionAssert.Contains(names, "|EntityMenu.cs");
    }

    [TestMethod]
    public void Program_RegistersOneMenuPerEntity()
    {
        var src = ConsoleProgramTemplate.Build("Shop", new[] { "Book", "Member" });

        StringAssert.Contains(src, "using Shop.Data;");
        StringAssert.Contains(src, "using Shop.Models;");
        StringAssert.Contains(src, "new EntityMenu<Book>(new BookRepository())");
        StringAssert.Contains(src, "new EntityMenu<Member>(new MemberRepository())");
    }

    [TestMethod]
    public void Menu_IsGenericAndSkipsGeneratedFields()
    {
        var src = ConsoleMenuTemplate.Source("Shop");

        StringAssert.Contains(src, "namespace Shop;");
        StringAssert.Contains(src, "public sealed class EntityMenu<T>");
        StringAssert.Contains(src, "Repository<T>");
        StringAssert.Contains(src, "\"CreatedOn\"");   // audit fields excluded from prompts
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ConsoleTargetTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Create `ConsoleMenuTemplate.cs`**

```csharp
namespace AppScaffolding;

/// <summary>Emits a generic, reflection-driven CRUD menu over one entity's typed repository.
/// Entity-independent: one file drives every entity's list/add/delete.</summary>
public static class ConsoleMenuTemplate
{
    public static string Source(string projectName) => $$"""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using {{projectName}}.Data;

namespace {{projectName}};

/// <summary>List / add / delete for one entity type, backed by its typed repository.</summary>
public sealed class EntityMenu<T> where T : class, new()
{
    private readonly Repository<T> _repository;

    public EntityMenu(Repository<T> repository) => _repository = repository;

    public void Run()
    {
        while (true)
        {
            Console.WriteLine($"\n{typeof(T).Name}: [l]ist  [a]dd  [d]elete  [b]ack");
            Console.Write("> ");
            switch (Console.ReadLine()?.Trim().ToLowerInvariant())
            {
                case "l": List(); break;
                case "a": Add(); break;
                case "d": Delete(); break;
                case "b": case "": case null: return;
                default: Console.WriteLine("Unrecognized command."); break;
            }
        }
    }

    private void List()
    {
        var all = _repository.GetAll();
        if (all.Count == 0) { Console.WriteLine("(none)"); return; }
        foreach (var item in all)
            Console.WriteLine(Describe(item));
    }

    private void Add()
    {
        var item = new T();
        foreach (var prop in Editable())
        {
            Console.Write($"{prop.Name} ({prop.PropertyType.Name}): ");
            var value = Parse(Console.ReadLine() ?? string.Empty, prop.PropertyType);
            if (value is not null) prop.SetValue(item, value);
        }
        _repository.Create(item);
        Console.WriteLine("Added.");
    }

    private void Delete()
    {
        Console.Write("Id to delete: ");
        if (int.TryParse(Console.ReadLine(), out var id) && _repository.Delete(id))
            Console.WriteLine("Deleted.");
        else
            Console.WriteLine("No such record.");
    }

    // Id is repository-assigned; audit fields are set on save; navigation properties are
    // resolved from foreign keys, not entered by hand.
    private static IEnumerable<PropertyInfo> Editable() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite
                        && p.Name is not ("Id" or "CreatedOn" or "ModifiedOn")
                        && IsScalar(p.PropertyType));

    private static bool IsScalar(Type type) =>
        type == typeof(string) || type == typeof(int) || type == typeof(decimal)
        || type == typeof(bool) || type == typeof(DateTime);

    private static object? Parse(string raw, Type type)
    {
        if (type == typeof(string)) return raw;
        if (raw.Length == 0) return null;
        if (type == typeof(int)) return int.TryParse(raw, out var i) ? i : null;
        if (type == typeof(decimal)) return decimal.TryParse(raw, out var d) ? d : null;
        if (type == typeof(bool)) return bool.TryParse(raw, out var b) ? b : null;
        if (type == typeof(DateTime)) return DateTime.TryParse(raw, out var dt) ? dt : null;
        return null;
    }

    private static string Describe(T item)
    {
        var id = typeof(T).GetProperty("Id");
        var props = (id is null ? Editable() : Editable().Prepend(id)).ToList();
        return string.Join("  ", props.Select(p => $"{p.Name}={p.GetValue(item)}"));
    }
}
""";
}
```

- [ ] **Step 4: Create `ConsoleProgramTemplate.cs`**

```csharp
namespace AppScaffolding;

/// <summary>Emits the console app entry point: a top-level menu registering one
/// <c>EntityMenu&lt;T&gt;</c> per discovered entity.</summary>
public static class ConsoleProgramTemplate
{
    public static string Build(string projectName, IReadOnlyList<string> entityNames)
    {
        var registrations = string.Join(",\n",
            entityNames.Select(name =>
                $"    (\"{name}\", () => new EntityMenu<{name}>(new {name}Repository()).Run())"));

        return $$"""
using {{projectName}};
using {{projectName}}.Data;
using {{projectName}}.Models;

var menus = new (string Name, Action Run)[]
{
{{registrations}}
};

while (true)
{
    Console.WriteLine("\nEntities:");
    for (var i = 0; i < menus.Length; i++)
        Console.WriteLine($"  {i + 1}. {menus[i].Name}");
    Console.WriteLine("  0. Exit");
    Console.Write("> ");

    var input = Console.ReadLine();
    if (input is null || input.Trim() == "0") break;
    if (int.TryParse(input, out var n) && n >= 1 && n <= menus.Length)
        menus[n - 1].Run();
    else
        Console.WriteLine("Unrecognized choice.");
}
""";
    }
}
```

**Note:** when `entityNames` is empty, `registrations` is `""`, producing an empty `Action[]` initializer — valid C#. No special-casing needed.

- [ ] **Step 5: Create `Targets/ConsoleTarget.cs`**

```csharp
using CodeGenerationEngine.Models;

namespace AppScaffolding.Targets;

/// <summary>A runnable console CRUD app over the discovered model.</summary>
public sealed class ConsoleTarget : ITargetScaffolder
{
    public string Key => "console";
    public string Description => "a runnable Console CRUD app";
    public DotNetProjectType ProjectType => DotNetProjectType.Console;
    public string FolderSuffix => ".Console";

    public IReadOnlyList<GeneratedFile> FrontEndFiles(string projectName, IReadOnlyList<string> entityNames) => new List<GeneratedFile>
    {
        new("", "EntityMenu.cs", ConsoleMenuTemplate.Source(projectName)),
        new("", "Program.cs", ConsoleProgramTemplate.Build(projectName, entityNames)),
    };
    // Default FixupProject no-op: our Program.cs overwrites the template's Program.cs.
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ConsoleTargetTests"`
Expected: PASS (all four).

- [ ] **Step 7: Commit**

```bash
git add AppScaffolding/ConsoleMenuTemplate.cs AppScaffolding/ConsoleProgramTemplate.cs AppScaffolding/Targets/ConsoleTarget.cs CodeGenerator.Tests/App/ConsoleTargetTests.cs
git commit -m "Add Console output target"
```

---

### Task 3: Web API target

A minimal-API `Program.cs` mapping CRUD endpoints per entity over the typed repositories.

**Files:**
- Create: `AppScaffolding/WebApiProgramTemplate.cs`
- Create: `AppScaffolding/Targets/WebApiTarget.cs`
- Test: `CodeGenerator.Tests/App/WebApiTargetTests.cs`

**Interfaces:**
- Produces:
  - `WebApiProgramTemplate.Build(string projectName, IReadOnlyList<string> entityNames)` → `Program.cs`.
  - `class WebApiTarget : ITargetScaffolder` — `Key="webapi"`, `FolderSuffix=".Api"`, `ProjectType=DotNetProjectType.WebApi`, `Description="a Web API with CRUD endpoints"`. `FrontEndFiles` returns `Program.cs` at project root.
- Consumes: `Repository<T>`/`<Entity>Repository`, `Models`. Relies on `Microsoft.NET.Sdk.Web` implicit usings for `WebApplication`/`Results`.

- [ ] **Step 1: Write the failing test** — `CodeGenerator.Tests/App/WebApiTargetTests.cs`

```csharp
using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WebApiTargetTests
{
    [TestMethod]
    public void Metadata_IsWebApi()
    {
        var t = new WebApiTarget();
        Assert.AreEqual("webapi", t.Key);
        Assert.AreEqual(".Api", t.FolderSuffix);
        Assert.AreEqual(DotNetProjectType.WebApi, t.ProjectType);
    }

    [TestMethod]
    public void FrontEndFiles_EmitsProgramAtProjectRoot()
    {
        var files = new WebApiTarget().FrontEndFiles("Shop", new[] { "Book" });
        Assert.IsTrue(files.Any(f => f.RelativeFolder == "" && f.Name == "Program.cs"));
    }

    [TestMethod]
    public void Program_MapsCrudEndpointsPerEntity()
    {
        var src = WebApiProgramTemplate.Build("Shop", new[] { "Book", "Member" });

        StringAssert.Contains(src, "using Shop.Data;");
        StringAssert.Contains(src, "using Shop.Models;");
        StringAssert.Contains(src, "MapEntity<Book>(app, \"book\", new BookRepository());");
        StringAssert.Contains(src, "MapEntity<Member>(app, \"member\", new MemberRepository());");
        StringAssert.Contains(src, "app.MapGet(");
        StringAssert.Contains(src, "app.MapPost(");
        StringAssert.Contains(src, "app.MapPut(");
        StringAssert.Contains(src, "app.MapDelete(");
        StringAssert.Contains(src, "app.Run();");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~WebApiTargetTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Create `WebApiProgramTemplate.cs`**

```csharp
namespace AppScaffolding;

/// <summary>Emits a minimal-API entry point mapping REST CRUD endpoints for each entity.</summary>
public static class WebApiProgramTemplate
{
    public static string Build(string projectName, IReadOnlyList<string> entityNames)
    {
        var registrations = string.Join("\n",
            entityNames.Select(name =>
                $"MapEntity<{name}>(app, \"{name.ToLowerInvariant()}\", new {name}Repository());"));

        return $$"""
using {{projectName}}.Data;
using {{projectName}}.Models;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

{{registrations}}

app.Run();

static void MapEntity<T>(WebApplication app, string route, Repository<T> repository)
    where T : class, new()
{
    app.MapGet($"/{route}", () => repository.GetAll());

    app.MapGet($"/{route}/{{id:int}}", (int id) =>
        repository.GetById(id) is { } found ? Results.Ok(found) : Results.NotFound());

    app.MapPost($"/{route}", (T item) =>
    {
        repository.Create(item);
        return Results.Created($"/{route}", item);
    });

    app.MapPut($"/{route}/{{id:int}}", (int id, T item) =>
        repository.Update(item) ? Results.NoContent() : Results.NotFound());

    app.MapDelete($"/{route}/{{id:int}}", (int id) =>
        repository.Delete(id) ? Results.NoContent() : Results.NotFound());
}
""";
    }
}
```

**Note:** with no entities, `registrations` is empty — `Program.cs` still compiles (builds the app, maps nothing, runs). The `id` route parameter on PUT is intentionally not used by `Repository.Update` (which matches on the body's `Id`); this is acceptable for a generated demo API.

- [ ] **Step 4: Create `Targets/WebApiTarget.cs`**

```csharp
using CodeGenerationEngine.Models;

namespace AppScaffolding.Targets;

/// <summary>An ASP.NET Core minimal API exposing CRUD endpoints over the discovered model.</summary>
public sealed class WebApiTarget : ITargetScaffolder
{
    public string Key => "webapi";
    public string Description => "a Web API with CRUD endpoints";
    public DotNetProjectType ProjectType => DotNetProjectType.WebApi;
    public string FolderSuffix => ".Api";

    public IReadOnlyList<GeneratedFile> FrontEndFiles(string projectName, IReadOnlyList<string> entityNames) => new List<GeneratedFile>
    {
        new("", "Program.cs", WebApiProgramTemplate.Build(projectName, entityNames)),
    };
    // Default FixupProject no-op: our Program.cs overwrites the template's; net10 webapi
    // emits no WeatherForecast.cs to remove.
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~WebApiTargetTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/WebApiProgramTemplate.cs AppScaffolding/Targets/WebApiTarget.cs CodeGenerator.Tests/App/WebApiTargetTests.cs
git commit -m "Add Web API output target"
```

---

### Task 4: Class library target

Models + Data only, no entry point. Deletes the template's `Class1.cs`.

**Files:**
- Create: `AppScaffolding/Targets/ClassLibraryTarget.cs`
- Test: `CodeGenerator.Tests/App/ClassLibraryTargetTests.cs`

**Interfaces:**
- Produces: `class ClassLibraryTarget : ITargetScaffolder` — `Key="classlib"`, `FolderSuffix=".Lib"`, `ProjectType=DotNetProjectType.ClassLibrary`, `Description="a class library (models + data only)"`. `FrontEndFiles` returns empty. `FixupProject` deletes `Class1.cs`.

- [ ] **Step 1: Write the failing test** — `CodeGenerator.Tests/App/ClassLibraryTargetTests.cs`

```csharp
using System.IO;
using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ClassLibraryTargetTests
{
    [TestMethod]
    public void Metadata_IsClassLibrary()
    {
        var t = new ClassLibraryTarget();
        Assert.AreEqual("classlib", t.Key);
        Assert.AreEqual(".Lib", t.FolderSuffix);
        Assert.AreEqual(DotNetProjectType.ClassLibrary, t.ProjectType);
    }

    [TestMethod]
    public void FrontEndFiles_IsEmpty_NoEntryPoint()
    {
        var files = new ClassLibraryTarget().FrontEndFiles("Shop", new[] { "Book" });
        Assert.HasCount(0, files);
    }

    [TestMethod]
    public void FixupProject_DeletesTheTemplateClass1()
    {
        var folder = Path.Combine(Path.GetTempPath(), "clib_" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        try
        {
            var class1 = Path.Combine(folder, "Class1.cs");
            File.WriteAllText(class1, "public class Class1 { }");

            new ClassLibraryTarget().FixupProject(folder);

            Assert.IsFalse(File.Exists(class1), "Class1.cs should have been deleted");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ClassLibraryTargetTests"`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Create `Targets/ClassLibraryTarget.cs`**

```csharp
using CodeGenerationEngine.Models;

namespace AppScaffolding.Targets;

/// <summary>The reusable core only: model classes plus JSON store and repositories, no runnable
/// front end.</summary>
public sealed class ClassLibraryTarget : ITargetScaffolder
{
    public string Key => "classlib";
    public string Description => "a class library (models + data only)";
    public DotNetProjectType ProjectType => DotNetProjectType.ClassLibrary;
    public string FolderSuffix => ".Lib";

    public IReadOnlyList<GeneratedFile> FrontEndFiles(string projectName, IReadOnlyList<string> entityNames)
        => Array.Empty<GeneratedFile>();

    public void FixupProject(string outputFolder)
    {
        // `dotnet new classlib` drops a placeholder Class1.cs; the shared core replaces it.
        var class1 = Path.Combine(outputFolder, "Class1.cs");
        try
        {
            if (File.Exists(class1))
                File.Delete(class1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leave it; a stray empty class compiles and is harmless.
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ClassLibraryTargetTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Targets/ClassLibraryTarget.cs CodeGenerator.Tests/App/ClassLibraryTargetTests.cs
git commit -m "Add Class library output target"
```

---

### Task 5: Background service (worker) target

A `Worker : BackgroundService` demonstration harness that exercises the repositories on startup, plus a host `Program.cs`.

**Files:**
- Create: `AppScaffolding/WorkerTemplate.cs`
- Create: `AppScaffolding/WorkerProgramTemplate.cs`
- Create: `AppScaffolding/Targets/WorkerTarget.cs`
- Test: `CodeGenerator.Tests/App/WorkerTargetTests.cs`

**Interfaces:**
- Produces:
  - `WorkerTemplate.Build(string projectName, IReadOnlyList<string> entityNames)` → `Worker.cs`.
  - `WorkerProgramTemplate.Source(string projectName)` → `Program.cs`.
  - `class WorkerTarget : ITargetScaffolder` — `Key="worker"`, `FolderSuffix=".Worker"`, `ProjectType=DotNetProjectType.WorkerService`, `Description="a background service that exercises the repositories"`. `FrontEndFiles` returns `Worker.cs` and `Program.cs` at project root.
- Consumes: `<Entity>Repository`; `Microsoft.NET.Sdk.Worker` implicit usings + `Microsoft.Extensions.Hosting` package for `BackgroundService`/`Host`.

- [ ] **Step 1: Write the failing test** — `CodeGenerator.Tests/App/WorkerTargetTests.cs`

```csharp
using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WorkerTargetTests
{
    [TestMethod]
    public void Metadata_IsWorker()
    {
        var t = new WorkerTarget();
        Assert.AreEqual("worker", t.Key);
        Assert.AreEqual(".Worker", t.FolderSuffix);
        Assert.AreEqual(DotNetProjectType.WorkerService, t.ProjectType);
    }

    [TestMethod]
    public void FrontEndFiles_EmitsWorkerAndProgram()
    {
        var files = new WorkerTarget().FrontEndFiles("Shop", new[] { "Book" });
        var names = files.Select(f => f.Name).ToList();
        CollectionAssert.Contains(names, "Worker.cs");
        CollectionAssert.Contains(names, "Program.cs");
    }

    [TestMethod]
    public void Worker_LogsEachEntityRepositoryCount()
    {
        var src = WorkerTemplate.Build("Shop", new[] { "Book", "Member" });

        StringAssert.Contains(src, "namespace Shop;");
        StringAssert.Contains(src, ": BackgroundService");
        StringAssert.Contains(src, "using Shop.Data;");
        StringAssert.Contains(src, "new BookRepository().GetAll().Count");
        StringAssert.Contains(src, "new MemberRepository().GetAll().Count");
    }

    [TestMethod]
    public void Program_RegistersTheWorker()
    {
        var src = WorkerProgramTemplate.Source("Shop");

        StringAssert.Contains(src, "using Shop;");
        StringAssert.Contains(src, "AddHostedService<Worker>()");
        StringAssert.Contains(src, ".Run();");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~WorkerTargetTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Create `WorkerTemplate.cs`**

```csharp
namespace AppScaffolding;

/// <summary>Emits a BackgroundService that, on start, reports each entity repository's record
/// count. A worker is not naturally CRUD, so this is an honest harness proving the headless
/// model + data core runs.</summary>
public static class WorkerTemplate
{
    public static string Build(string projectName, IReadOnlyList<string> entityNames)
    {
        var logs = string.Join("\n",
            entityNames.Select(name =>
                $"        _logger.LogInformation(\"{name}: {{Count}} record(s)\", new {name}Repository().GetAll().Count);"));

        return $$"""
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using {{projectName}}.Data;

namespace {{projectName}};

/// <summary>Exercises the generated repositories once at startup.</summary>
public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger) => _logger = logger;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Exercising the generated repositories:");
{{logs}}
        return Task.CompletedTask;
    }
}
""";
    }
}
```

**Note:** with no entities, `logs` is empty; `ExecuteAsync` still compiles (logs the header, returns).

- [ ] **Step 4: Create `WorkerProgramTemplate.cs`**

```csharp
namespace AppScaffolding;

/// <summary>Emits the worker host entry point registering <see cref="Worker"/>.</summary>
public static class WorkerProgramTemplate
{
    public static string Source(string projectName) => $$"""
using {{projectName}};
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Build().Run();
""";
}
```

- [ ] **Step 5: Create `Targets/WorkerTarget.cs`**

```csharp
using CodeGenerationEngine.Models;

namespace AppScaffolding.Targets;

/// <summary>A worker-service scaffold whose BackgroundService exercises the repositories.</summary>
public sealed class WorkerTarget : ITargetScaffolder
{
    public string Key => "worker";
    public string Description => "a background service that exercises the repositories";
    public DotNetProjectType ProjectType => DotNetProjectType.WorkerService;
    public string FolderSuffix => ".Worker";

    public IReadOnlyList<GeneratedFile> FrontEndFiles(string projectName, IReadOnlyList<string> entityNames) => new List<GeneratedFile>
    {
        new("", "Worker.cs", WorkerTemplate.Build(projectName, entityNames)),
        new("", "Program.cs", WorkerProgramTemplate.Source(projectName)),
    };
    // Default FixupProject no-op: our Worker.cs and Program.cs overwrite the template's.
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~WorkerTargetTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add AppScaffolding/WorkerTemplate.cs AppScaffolding/WorkerProgramTemplate.cs AppScaffolding/Targets/WorkerTarget.cs CodeGenerator.Tests/App/WorkerTargetTests.cs
git commit -m "Add Background service (worker) output target"
```

---

### Task 6: Target registry

A single lookup mapping keys → targets, with an ordered key list and a default. `BuildAppCommand` (Task 7) and the integration test (Task 8) consume it.

**Files:**
- Create: `AppScaffolding/Targets/TargetScaffolders.cs`
- Test: `CodeGenerator.Tests/App/TargetScaffoldersTests.cs`

**Interfaces:**
- Produces:
  - `static class TargetScaffolders` with:
    - `IReadOnlyList<string> Keys` — ordered: `wpf, console, webapi, classlib, worker`.
    - `IReadOnlyDictionary<string, ITargetScaffolder> ByKey` (case-insensitive).
    - `ITargetScaffolder Default` (the `wpf` entry).
    - `bool TryResolve(string key, out ITargetScaffolder target)`.

- [ ] **Step 1: Write the failing test** — `CodeGenerator.Tests/App/TargetScaffoldersTests.cs`

```csharp
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

[TestClass]
public class TargetScaffoldersTests
{
    [TestMethod]
    public void Keys_AreTheFiveTargetsInOrder()
    {
        CollectionAssert.AreEqual(
            new[] { "wpf", "console", "webapi", "classlib", "worker" },
            TargetScaffolders.Keys.ToList());
    }

    [TestMethod]
    public void Default_IsWpf()
    {
        Assert.AreEqual("wpf", TargetScaffolders.Default.Key);
    }

    [TestMethod]
    public void TryResolve_IsCaseInsensitive()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("WebApi", out var t));
        Assert.AreEqual("webapi", t.Key);
    }

    [TestMethod]
    public void TryResolve_UnknownKey_ReturnsFalse()
    {
        Assert.IsFalse(TargetScaffolders.TryResolve("nonsense", out _));
    }

    [TestMethod]
    public void ByKey_HasAnEntryForEveryKey()
    {
        foreach (var key in TargetScaffolders.Keys)
            Assert.IsTrue(TargetScaffolders.ByKey.ContainsKey(key), $"missing {key}");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~TargetScaffoldersTests"`
Expected: FAIL — `TargetScaffolders` does not exist.

- [ ] **Step 3: Create `Targets/TargetScaffolders.cs`**

```csharp
namespace AppScaffolding.Targets;

/// <summary>The available output targets, keyed by their CLI key.</summary>
public static class TargetScaffolders
{
    private static readonly ITargetScaffolder[] All =
    {
        new WpfTarget(),
        new ConsoleTarget(),
        new WebApiTarget(),
        new ClassLibraryTarget(),
        new WorkerTarget(),
    };

    /// <summary>Target keys in display order; the first is the default.</summary>
    public static IReadOnlyList<string> Keys { get; } = All.Select(t => t.Key).ToList();

    public static IReadOnlyDictionary<string, ITargetScaffolder> ByKey { get; } =
        All.ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The historical default: the WPF app.</summary>
    public static ITargetScaffolder Default { get; } = All[0];

    /// <summary>Resolves a key (case-insensitively) to a target.</summary>
    public static bool TryResolve(string key, out ITargetScaffolder target)
        => ByKey.TryGetValue(key, out target!);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~TargetScaffoldersTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Targets/TargetScaffolders.cs CodeGenerator.Tests/App/TargetScaffoldersTests.cs
git commit -m "Add target registry"
```

---

### Task 7: Wire target selection into `build app:` (prompt on approve)

Adds the prompt-on-approve UX with a testable, IO-free resolver, plus the inline `approve <target>` shortcut, and the redirected-input default-to-wpf rule.

**Files:**
- Modify: `CodeGenerator/App/BuildAppCommand.cs` (the `approve` case; add `ResolveTarget`)
- Test: `CodeGenerator.Tests/App/BuildAppTargetSelectionTests.cs`

**Interfaces:**
- Produces: `internal static ITargetScaffolder? BuildAppCommand.ResolveTarget(string[] parts, bool inputRedirected, TextReader reader)`
  - `parts` is the split `approve` command (`parts[0]` == "approve"/"a").
  - Inline key present (`parts.Length >= 2`): resolve `parts[1]`; return `null` if unknown (caller shows an error, does not scaffold).
  - No inline key + `inputRedirected`: return `TargetScaffolders.Default` (no read).
  - No inline key + interactive: loop reading lines from `reader`; blank ⇒ `Default`; valid key ⇒ that target; unknown ⇒ re-prompt.
- Consumes: `TargetScaffolders` (Task 6), `AppScaffolder.Scaffold(model, appName, workspaceRoot, target)` (Task 1).

- [ ] **Step 1: Write the failing test** — `CodeGenerator.Tests/App/BuildAppTargetSelectionTests.cs`

```csharp
using System.IO;
using AppScaffolding.Targets;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class BuildAppTargetSelectionTests
{
    private static string[] Parts(params string[] p) => p;

    [TestMethod]
    public void InlineTarget_IsUsedDirectly()
    {
        var t = BuildAppCommand.ResolveTarget(Parts("approve", "console"), inputRedirected: true, TextReader.Null);
        Assert.AreEqual("console", t!.Key);
    }

    [TestMethod]
    public void InlineUnknownTarget_ReturnsNull()
    {
        var t = BuildAppCommand.ResolveTarget(Parts("approve", "bogus"), inputRedirected: false, TextReader.Null);
        Assert.IsNull(t);
    }

    [TestMethod]
    public void Redirected_NoInline_DefaultsToWpf_WithoutReading()
    {
        var t = BuildAppCommand.ResolveTarget(Parts("approve"), inputRedirected: true, TextReader.Null);
        Assert.AreEqual("wpf", t!.Key);
    }

    [TestMethod]
    public void Interactive_BlankLine_DefaultsToWpf()
    {
        var t = BuildAppCommand.ResolveTarget(Parts("approve"), inputRedirected: false, new StringReader("\n"));
        Assert.AreEqual("wpf", t!.Key);
    }

    [TestMethod]
    public void Interactive_ValidKey_IsChosen()
    {
        var t = BuildAppCommand.ResolveTarget(Parts("approve"), inputRedirected: false, new StringReader("webapi\n"));
        Assert.AreEqual("webapi", t!.Key);
    }

    [TestMethod]
    public void Interactive_RepromptsUntilValid()
    {
        var t = BuildAppCommand.ResolveTarget(Parts("approve"), inputRedirected: false, new StringReader("bad\nconsole\n"));
        Assert.AreEqual("console", t!.Key);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~BuildAppTargetSelectionTests"`
Expected: FAIL — `ResolveTarget` does not exist.

- [ ] **Step 3: Add `using` and `ResolveTarget` to `BuildAppCommand.cs`**

Add near the top with the other usings:

```csharp
using AppScaffolding.Targets;
```

Add this method to the class (e.g. after `RunReviewLoop`):

```csharp
    /// <summary>
    /// Resolves the output target for an <c>approve</c> command. An inline
    /// <c>approve &lt;target&gt;</c> wins (null when the key is unknown). With no inline key,
    /// redirected input never prompts and defaults to WPF (so piped scripts are unchanged);
    /// interactive input is prompted, blank meaning WPF, and re-prompts on an unknown key.
    /// </summary>
    internal static ITargetScaffolder? ResolveTarget(string[] parts, bool inputRedirected, TextReader reader)
    {
        if (parts.Length >= 2)
            return TargetScaffolders.TryResolve(parts[1], out var inline) ? inline : null;

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
```

**Note:** `System.IO` (for `TextReader`) is available via the project's implicit usings; if the file lacks it and does not compile, add `using System.IO;`.

- [ ] **Step 4: Update the `approve` case in `RunReviewLoop`** — replace the body of `case "a": case "approve":` (the block that currently calls `new AppScaffolder().Scaffold(model, appName, workspaceRoot)`) with:

```csharp
                case "a":
                case "approve":
                    if (model.Entities.Count == 0)
                    {
                        Console.WriteLine("Nothing to generate — the model is empty.");
                        return;
                    }

                    var target = ResolveTarget(parts, Console.IsInputRedirected, Console.In);
                    if (target is null)
                    {
                        Console.WriteLine(
                            $"Unknown target '{parts[1]}'. Valid: {string.Join(", ", TargetScaffolders.Keys)}");
                        break;
                    }

                    var scaffold = new AppScaffolder().Scaffold(model, appName, workspaceRoot, target);
                    if (scaffold.Success)
                    {
                        Console.WriteLine($"Generated project at {scaffold.ProjectPath}");
                        foreach (var file in scaffold.FilesWritten)
                            Console.WriteLine($"  {Path.GetRelativePath(scaffold.ProjectPath, file)}");
                    }
                    else
                    {
                        Console.WriteLine($"Generation failed: {scaffold.Message}");
                    }
                    return;
```

**Note:** the unknown-target branch uses `break` (stay in the review loop), matching the existing "Unrecognized command" behavior; only a successful/failed scaffold `return`s.

- [ ] **Step 5: Update the review-loop help line** — so users discover the shortcut. Change the first help `WriteLine` in `RunReviewLoop` from:

```csharp
            Console.WriteLine("  [a]pprove   add <name>   remove <name>   rename <old> <new>");
```
to:
```csharp
            Console.WriteLine("  [a]pprove [target]   add <name>   remove <name>   rename <old> <new>");
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~BuildAppTargetSelectionTests"`
Expected: PASS (all six).

- [ ] **Step 7: Run the full suite (guard existing behavior)**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`
Expected: PASS — everything green.

- [ ] **Step 8: Commit**

```bash
git add CodeGenerator/App/BuildAppCommand.cs CodeGenerator.Tests/App/BuildAppTargetSelectionTests.cs
git commit -m "Prompt for output target on approve; support approve <target>"
```

---

### Task 8: Integration test — generated non-WPF projects compile

Proves the generated console / webapi / classlib / worker projects actually build. Slow (runs `dotnet new` + restore + build per target); kept in its own class and category so it can be filtered.

**Files:**
- Create: `CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs`

**Interfaces:**
- Consumes: `AppScaffolder.Scaffold(model, appName, workspaceRoot, target)`, `TargetScaffolders`, `CandidateDomainModel`/`CandidateEntity`/`CandidateField`.

- [ ] **Step 1: Write the test** (it is the deliverable — no separate failing stub)

```csharp
using System.Diagnostics;
using System.IO;
using AppScaffolding;
using AppScaffolding.Targets;
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

    [TestMethod]
    [TestCategory("Integration")]
    [DataRow("console")]
    [DataRow("webapi")]
    [DataRow("classlib")]
    [DataRow("worker")]
    public void GeneratedProject_Builds(string targetKey)
    {
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
```

- [ ] **Step 2: Run the integration test**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~GeneratedProjectBuildTests"`
Expected: PASS for all four `DataRow`s. (May take a few minutes on first run due to NuGet restore.)

- [ ] **Step 3: Run the full suite**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs
git commit -m "Integration test: generated non-WPF projects build"
```

---

## End-to-end manual verification (after all tasks)

- [ ] Rebuild the CLI (its bin goes stale after `dotnet test`):
  `dotnet build CodeGenerator`
- [ ] Interactive run — confirm the prompt appears and a console app is generated:
  ```
  dotnet run --project CodeGenerator
  build app: a library that lends books to members
  approve console
  exit
  ```
  Expect a `<AppName>.Console` folder; `dotnet run` it to see the entity menu.
- [ ] Backward-compat — confirm piped input still yields the WPF app with no prompt:
  ```
  printf 'build app: a library that lends books to members\napprove\nexit\n' | dotnet run --project CodeGenerator
  ```
  Expect the original `<AppName>` WPF folder, no "Target [...]" prompt line.

---

## Self-Review

**Spec coverage:**
- Strategy architecture (`ITargetScaffolder`) → Task 1. ✅
- Shared core = Models + JsonStore + Repository(+typed) → Task 1 (`AppScaffolder`). ✅
- WPF unchanged / RelationshipResolver is WPF-only → Task 1 (`WpfTarget`) + Step 8 full-suite guard. ✅
- Console / Web API / Class library / Worker targets → Tasks 2–5. ✅
- Registry → Task 6. ✅
- Prompt-on-approve + inline `approve <target>` + redirected default-to-wpf → Task 7. ✅
- Folder suffixes → Task 1 (`projectName = appName + FolderSuffix`) + each target's `FolderSuffix`. ✅
- Testing: unit per target, shared-core guard (full suite), selection test, compile integration test → Tasks 1–8. ✅

**Placeholder scan:** No TBD/TODO; every code step shows complete source.

**Type consistency:** `ITargetScaffolder` members (`Key`, `Description`, `ProjectType`, `FolderSuffix`, `FrontEndFiles(string, IReadOnlyList<string>)`, `FixupProject(string)`) are used identically across Tasks 1–6. `GeneratedFile(RelativeFolder, Name, Source)` is constructed consistently. Template method names (`ConsoleProgramTemplate.Build`, `ConsoleMenuTemplate.Source`, `WebApiProgramTemplate.Build`, `WorkerTemplate.Build`, `WorkerProgramTemplate.Source`) match between their creating task and the target that calls them. `Scaffold(model, appName, workspaceRoot, target)` signature matches its callers in Tasks 7 and 8.
