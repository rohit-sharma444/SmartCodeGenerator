# `build app:` Scaffolding Phase Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the `build app:` **approve** action generate a real .NET console project — one C# class per approved entity — instead of printing a stub message.

**Architecture:** A new class library `AppScaffolding` bridges DomainDiscovery (`CandidateDomainModel`) and CodeGenerationEngine (`ClassDefinition`, `DotNetProjectFactory`, `RoslynClassGenerator`). It exposes name derivation, model→class mapping, class-file writing, and a `Scaffold` orchestrator. `BuildAppCommand`'s approve branch calls it.

**Tech Stack:** C# / .NET 10 (`net10.0`). Tests in `CodeGenerator.Tests` use **MSTest**.

## Global Constraints

- All projects target `net10.0`.
- New project `AppScaffolding` references **both** `DomainDiscovery` and `CodeGenerationEngine`; its root namespace is `AppScaffolding`.
- Do NOT add a DomainDiscovery→CodeGenerationEngine (or reverse) dependency — the bridge lives only in `AppScaffolding`.
- `CodeGenerator.Tests` is MSTest: `[TestClass]` / `[TestMethod]`, `Assert.*`, `CollectionAssert.*`. The test project has `ImplicitUsings` enabled and a global `using Microsoft.VisualStudio.TestTools.UnitTesting;`.
- Run all commands from the repository root: `D:\SmartCodeGenerator-Repository`.
- Generated project type is **console**, framework **net10.0**. `RoslynClassGenerator` emits classes into the fixed `GeneratedCode` namespace (existing behavior — do not change it).
- Commit after each task. Work stays on the current branch `feat/build-app-scaffolding`.

## Reference: existing signatures (do not redefine)

- `DomainDiscovery.Model.CandidateDomainModel` → `List<CandidateEntity> Entities`.
- `CandidateEntity` → `required string Name`, `List<CandidateField> Fields`, `Provenance Provenance`.
- `CandidateField` → `required string Name`, `required string Type`, `Provenance Provenance`.
- `CodeGenerationEngine.Models.ClassDefinition` → `string Name`, `List<PropertyDefinition> Properties`.
- `PropertyDefinition` → `string Name`, `string Type`.
- `CodeGenerationEngine.Models.ProjectRequest` → `ProjectName`, `OutputFolder`, `DotNetProjectType ProjectType`, `string Framework`.
- `CodeGenerationEngine.Models.DotNetProjectType.Console`.
- `CodeGenerationEngine.Factory.DotNetProjectFactory.CreateProject(ProjectRequest) : ProjectMetaData` (`ProjectMetaData` has `bool Success`, `string Message`).
- `CodeGenerationEngine.Core.RoslynClassGenerator.Generate(ClassDefinition) : string`.

---

### Task 1: `AppScaffolding` project + `DomainAppName`

**Files:**
- Create: `AppScaffolding/AppScaffolding.csproj`
- Create: `AppScaffolding/DomainAppName.cs`
- Modify: `SmartCodeGenerator/SmartCodeGenerator.slnx`
- Modify: `CodeGenerator.Tests/CodeGenerator.Tests.csproj` (add reference to `AppScaffolding`)
- Test: `CodeGenerator.Tests/App/DomainAppNameTests.cs`

**Interfaces:**
- Produces: `AppScaffolding.DomainAppName.FromDescription(string description) : string`.

- [ ] **Step 1: Create the project and wire references**

Create `AppScaffolding/AppScaffolding.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\DomainDiscovery\DomainDiscovery.csproj" />
    <ProjectReference Include="..\CodeGenerationEngine\CodeGenerationEngine.csproj" />
  </ItemGroup>

</Project>
```

In `SmartCodeGenerator/SmartCodeGenerator.slnx`, add this line inside `<Solution>` (keep the list tidy, alphabetical-ish):

```xml
  <Project Path="../AppScaffolding/AppScaffolding.csproj" />
```

In `CodeGenerator.Tests/CodeGenerator.Tests.csproj`, add to the existing `<ItemGroup>` that contains the `<ProjectReference>` elements:

```xml
    <ProjectReference Include="..\AppScaffolding\AppScaffolding.csproj" />
```

- [ ] **Step 2: Write the failing test**

Create `CodeGenerator.Tests/App/DomainAppNameTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class DomainAppNameTests
{
    [TestMethod]
    public void FromDescription_PascalCasesWords()
        => Assert.AreEqual("PayrollApplication",
            DomainAppName.FromDescription("payroll application"));

    [TestMethod]
    public void FromDescription_BlankOrNonAlphanumeric_ReturnsGeneratedApp()
    {
        Assert.AreEqual("GeneratedApp", DomainAppName.FromDescription(""));
        Assert.AreEqual("GeneratedApp", DomainAppName.FromDescription("   "));
        Assert.AreEqual("GeneratedApp", DomainAppName.FromDescription("!!!"));
    }

    [TestMethod]
    public void FromDescription_StartingWithDigit_StartsWithLetter()
    {
        var name = DomainAppName.FromDescription("123 app");
        Assert.IsTrue(char.IsLetter(name[0]), $"expected letter start, got '{name}'");
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~DomainAppNameTests"`
Expected: FAILS to build/compile — `DomainAppName` does not exist yet.

- [ ] **Step 4: Implement `DomainAppName`**

Create `AppScaffolding/DomainAppName.cs`:

```csharp
using System.Text;

namespace AppScaffolding;

/// <summary>Derives a PascalCase .NET project name from free-text.</summary>
public static class DomainAppName
{
    public static string FromDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "GeneratedApp";

        var sb = new StringBuilder();
        bool startOfWord = true;

        foreach (var ch in description)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(startOfWord ? char.ToUpperInvariant(ch) : ch);
                startOfWord = false;
            }
            else
            {
                startOfWord = true;
            }
        }

        if (sb.Length == 0)
            return "GeneratedApp";

        // A project/type name must not start with a digit.
        if (char.IsDigit(sb[0]))
            sb.Insert(0, "App");

        return sb.ToString();
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~DomainAppNameTests"`
Expected: PASS (3/3).

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/AppScaffolding.csproj AppScaffolding/DomainAppName.cs SmartCodeGenerator/SmartCodeGenerator.slnx CodeGenerator.Tests/CodeGenerator.Tests.csproj CodeGenerator.Tests/App/DomainAppNameTests.cs
git commit -m "Add AppScaffolding project and DomainAppName derivation"
```

---

### Task 2: `DomainModelMapper`

**Files:**
- Create: `AppScaffolding/DomainModelMapper.cs`
- Modify: `CodeGenerator.Tests/CodeGenerator.Tests.csproj` (add reference to `DomainDiscovery`)
- Test: `CodeGenerator.Tests/App/DomainModelMapperTests.cs`

**Interfaces:**
- Consumes: `CandidateDomainModel`, `CandidateEntity`, `CandidateField` (DomainDiscovery.Model); `ClassDefinition`, `PropertyDefinition` (CodeGenerationEngine.Models).
- Produces: `AppScaffolding.DomainModelMapper.ToClassDefinitions(CandidateDomainModel model) : IReadOnlyList<ClassDefinition>`.

- [ ] **Step 1: Add the DomainDiscovery reference to the test project**

In `CodeGenerator.Tests/CodeGenerator.Tests.csproj`, add to the same `<ItemGroup>` with the other `<ProjectReference>` elements:

```xml
    <ProjectReference Include="..\DomainDiscovery\DomainDiscovery.csproj" />
```

- [ ] **Step 2: Write the failing tests**

Create `CodeGenerator.Tests/App/DomainModelMapperTests.cs`:

```csharp
using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class DomainModelMapperTests
{
    [TestMethod]
    public void ToClassDefinitions_MapsFieldsToProperties()
    {
        var model = new CandidateDomainModel();
        var employee = new CandidateEntity { Name = "Employee" };
        employee.Fields.Add(new CandidateField { Name = "Salary", Type = "decimal" });
        employee.Fields.Add(new CandidateField { Name = "FullName", Type = "string" });
        model.Entities.Add(employee);

        var classes = DomainModelMapper.ToClassDefinitions(model);

        Assert.AreEqual(1, classes.Count);
        Assert.AreEqual("Employee", classes[0].Name);
        CollectionAssert.AreEqual(
            new[] { "Salary", "FullName" },
            classes[0].Properties.Select(p => p.Name).ToList());
        Assert.AreEqual("decimal", classes[0].Properties[0].Type);
    }

    [TestMethod]
    public void ToClassDefinitions_FieldlessEntity_GetsIntId()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Payroll" });

        var classes = DomainModelMapper.ToClassDefinitions(model);

        Assert.AreEqual(1, classes[0].Properties.Count);
        Assert.AreEqual("Id", classes[0].Properties[0].Name);
        Assert.AreEqual("int", classes[0].Properties[0].Type);
    }

    [TestMethod]
    public void ToClassDefinitions_MultipleEntities_PreservesNamesAndOrder()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Payroll" });
        model.Entities.Add(new CandidateEntity { Name = "Application" });

        var classes = DomainModelMapper.ToClassDefinitions(model);

        CollectionAssert.AreEqual(
            new[] { "Payroll", "Application" },
            classes.Select(c => c.Name).ToList());
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~DomainModelMapperTests"`
Expected: FAILS to compile — `DomainModelMapper` does not exist yet.

- [ ] **Step 4: Implement `DomainModelMapper`**

Create `AppScaffolding/DomainModelMapper.cs`:

```csharp
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace AppScaffolding;

/// <summary>Maps an approved domain model to CodeGenerationEngine class definitions.</summary>
public static class DomainModelMapper
{
    public static IReadOnlyList<ClassDefinition> ToClassDefinitions(CandidateDomainModel model)
    {
        var classes = new List<ClassDefinition>();

        foreach (var entity in model.Entities)
        {
            var definition = new ClassDefinition { Name = entity.Name };

            if (entity.Fields.Count == 0)
            {
                // Field-less entities get a primary key so the class isn't empty.
                definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
            }
            else
            {
                foreach (var field in entity.Fields)
                    definition.Properties.Add(
                        new PropertyDefinition { Name = field.Name, Type = field.Type });
            }

            classes.Add(definition);
        }

        return classes;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~DomainModelMapperTests"`
Expected: PASS (3/3).

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/DomainModelMapper.cs CodeGenerator.Tests/CodeGenerator.Tests.csproj CodeGenerator.Tests/App/DomainModelMapperTests.cs
git commit -m "Add DomainModelMapper (entities to class definitions)"
```

---

### Task 3: `AppScaffolder.WriteClasses`

**Files:**
- Create: `AppScaffolding/AppScaffolder.cs`
- Test: `CodeGenerator.Tests/App/AppScaffolderWriteTests.cs`

**Interfaces:**
- Consumes: `ClassDefinition` (CodeGenerationEngine.Models), `RoslynClassGenerator.Generate` (CodeGenerationEngine.Core).
- Produces: `AppScaffolding.AppScaffolder` (instance class) with
  `IReadOnlyList<string> WriteClasses(string outputFolder, IReadOnlyList<ClassDefinition> classes)` —
  writes one `<Name>.cs` per class into `outputFolder`, returns the paths successfully written; per-file IO failures are skipped (best-effort), remaining classes still written.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/AppScaffolderWriteTests.cs`:

```csharp
using System.IO;
using AppScaffolding;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class AppScaffolderWriteTests
{
    [TestMethod]
    public void WriteClasses_WritesOneFilePerClass_WithClassAndProperty()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scaffold_" + Path.GetRandomFileName());
        try
        {
            var classes = new List<ClassDefinition>
            {
                new()
                {
                    Name = "Employee",
                    Properties = { new PropertyDefinition { Name = "Salary", Type = "decimal" } }
                }
            };

            var written = new AppScaffolder().WriteClasses(folder, classes);

            Assert.AreEqual(1, written.Count);
            var file = Path.Combine(folder, "Employee.cs");
            Assert.IsTrue(File.Exists(file), "Employee.cs should exist");

            var source = File.ReadAllText(file);
            StringAssert.Contains(source, "class Employee");
            StringAssert.Contains(source, "Salary");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~AppScaffolderWriteTests"`
Expected: FAILS to compile — `AppScaffolder` does not exist yet.

- [ ] **Step 3: Implement `AppScaffolder.WriteClasses`**

Create `AppScaffolding/AppScaffolder.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace AppScaffolding;

/// <summary>Generates a .NET project and one C# class file per domain entity.</summary>
public sealed class AppScaffolder
{
    /// <summary>
    /// Writes one <c>&lt;Name&gt;.cs</c> per class into <paramref name="outputFolder"/>.
    /// Best-effort: a file that cannot be written is skipped and the rest continue.
    /// Returns the paths successfully written.
    /// </summary>
    public IReadOnlyList<string> WriteClasses(
        string outputFolder, IReadOnlyList<ClassDefinition> classes)
    {
        Directory.CreateDirectory(outputFolder);

        var written = new List<string>();
        foreach (var definition in classes)
        {
            try
            {
                var source = RoslynClassGenerator.Generate(definition);
                var path = Path.Combine(outputFolder, definition.Name + ".cs");
                File.WriteAllText(path, source);
                written.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Skip this file; remaining classes are still written.
            }
        }

        return written;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~AppScaffolderWriteTests"`
Expected: PASS (1/1).

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/AppScaffolder.cs CodeGenerator.Tests/App/AppScaffolderWriteTests.cs
git commit -m "Add AppScaffolder.WriteClasses (class-file generation)"
```

---

### Task 4: `Scaffold` orchestrator + wire into `BuildAppCommand`

**Files:**
- Create: `AppScaffolding/ScaffoldResult.cs`
- Modify: `AppScaffolding/AppScaffolder.cs` (add `Scaffold`)
- Modify: `CodeGenerator/CodeGenerator.csproj` (add reference to `AppScaffolding`)
- Modify: `CodeGenerator/App/BuildAppCommand.cs` (derive name, wire approve)

**Interfaces:**
- Consumes: `DomainModelMapper.ToClassDefinitions` (Task 2), `AppScaffolder.WriteClasses` (Task 3), `DomainAppName.FromDescription` (Task 1), `DotNetProjectFactory.CreateProject`, `ProjectRequest`, `DotNetProjectType.Console`.
- Produces: `AppScaffolding.ScaffoldResult(bool Success, string ProjectPath, IReadOnlyList<string> FilesWritten, string Message)`; `AppScaffolder.Scaffold(CandidateDomainModel model, string appName, string workspaceRoot) : ScaffoldResult`.

This task's deliverable (a full `dotnet new` project on disk) needs the .NET SDK, so it is verified by a **manual smoke run** rather than a unit test.

- [ ] **Step 1: Add `ScaffoldResult`**

Create `AppScaffolding/ScaffoldResult.cs`:

```csharp
namespace AppScaffolding;

/// <summary>Outcome of scaffolding: the project folder and the class files written.</summary>
public sealed record ScaffoldResult(
    bool Success,
    string ProjectPath,
    IReadOnlyList<string> FilesWritten,
    string Message);
```

- [ ] **Step 2: Add `Scaffold` to `AppScaffolder`**

Add these `using`s at the top of `AppScaffolding/AppScaffolder.cs` (alongside the existing ones):

```csharp
using CodeGenerationEngine.Factory;
using DomainDiscovery.Model;
```

Add this method inside the `AppScaffolder` class (after `WriteClasses`):

```csharp
    /// <summary>
    /// Creates a console project named <paramref name="appName"/> under
    /// <paramref name="workspaceRoot"/> (reusing it if it already exists) and
    /// writes one class per entity in <paramref name="model"/>.
    /// </summary>
    public ScaffoldResult Scaffold(
        CandidateDomainModel model, string appName, string workspaceRoot)
    {
        var classes = DomainModelMapper.ToClassDefinitions(model);
        var outputFolder = Path.Combine(workspaceRoot, appName);
        var csprojPath = Path.Combine(outputFolder, appName + ".csproj");

        if (!File.Exists(csprojPath))
        {
            var created = DotNetProjectFactory.CreateProject(new ProjectRequest
            {
                ProjectName = appName,
                OutputFolder = outputFolder,
                ProjectType = DotNetProjectType.Console,
                Framework = "net10.0"
            });

            if (!created.Success)
                return new ScaffoldResult(
                    false, outputFolder, Array.Empty<string>(),
                    $"Project creation failed: {created.Message}");
        }

        var written = WriteClasses(outputFolder, classes);
        bool allWritten = written.Count == classes.Count;

        var message = allWritten
            ? $"Generated {written.Count} class(es)."
            : $"Generated {written.Count} of {classes.Count} class(es); " +
              "some files could not be written.";

        return new ScaffoldResult(allWritten, outputFolder, written, message);
    }
```

- [ ] **Step 3: Reference `AppScaffolding` from the CLI**

In `CodeGenerator/CodeGenerator.csproj`, add to the `<ItemGroup>` containing the other `<ProjectReference>` elements:

```xml
    <ProjectReference Include="..\AppScaffolding\AppScaffolding.csproj" />
```

- [ ] **Step 4: Wire the approve branch in `BuildAppCommand`**

In `CodeGenerator/App/BuildAppCommand.cs`, add to the `using` block at the top:

```csharp
using AppScaffolding;
```

In `Handle`, after the empty-description guard, derive the app name and pass it through. Replace this line:

```csharp
        RunReviewLoop(result.Model);
```

with:

```csharp
        var appName = DomainAppName.FromDescription(description);
        RunReviewLoop(result.Model, appName, workspaceRoot);
```

Change the `RunReviewLoop` signature from:

```csharp
    private static void RunReviewLoop(CandidateDomainModel model)
```

to:

```csharp
    private static void RunReviewLoop(CandidateDomainModel model, string appName, string workspaceRoot)
```

Replace the approve case:

```csharp
                case "a":
                case "approve":
                    Console.WriteLine($"Model approved with {model.Entities.Count} entit"
                                      + (model.Entities.Count == 1 ? "y." : "ies.")
                                      + " (Code generation will be added in the scaffolding phase.)");
                    return;
```

with:

```csharp
                case "a":
                case "approve":
                    if (model.Entities.Count == 0)
                    {
                        Console.WriteLine("Nothing to generate — the model is empty.");
                        return;
                    }

                    var scaffold = new AppScaffolder().Scaffold(model, appName, workspaceRoot);
                    if (scaffold.Success)
                    {
                        Console.WriteLine($"Generated project at {scaffold.ProjectPath}");
                        foreach (var file in scaffold.FilesWritten)
                            Console.WriteLine($"  {Path.GetFileName(file)}");
                    }
                    else
                    {
                        Console.WriteLine($"Generation failed: {scaffold.Message}");
                    }
                    return;
```

- [ ] **Step 5: Build the solution**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx`
Expected: build succeeds, 0 errors.

- [ ] **Step 6: Run the full test suite (no regressions)**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj`
Expected: all PASS (the 82 existing + the new tests from Tasks 1–3).

- [ ] **Step 7: Manual smoke — generate a real project**

Run (bash), using a scratch workspace so files land somewhere predictable:

```bash
WS="$(mktemp -d)"; echo "workspace: $WS"
printf 'a\n' | PROJECTPATH="$WS" dotnet run --project CodeGenerator -- "build app: payroll application"
echo "--- generated tree ---"
find "$WS/PayrollApplication" -maxdepth 1 -type f | sort
```

Expected: output includes `Generated project at .../PayrollApplication`, and the tree lists `PayrollApplication.csproj`, `Program.cs`, `Payroll.cs`, and `Application.cs`. Open `Payroll.cs` and confirm it contains `public class Payroll`. (First run may fetch from Wikipedia; if offline it falls back to seed entities, which still yields `Payroll` + `Application`.)

- [ ] **Step 8: Commit**

```bash
git add AppScaffolding/ScaffoldResult.cs AppScaffolding/AppScaffolder.cs CodeGenerator/CodeGenerator.csproj CodeGenerator/App/BuildAppCommand.cs
git commit -m "Generate project + entity classes on build app: approve"
```

---

## Self-Review

**Spec coverage:**
- New `AppScaffolding` library referencing both DomainDiscovery + CodeGenerationEngine → Task 1 (csproj). ✓
- `DomainAppName.FromDescription` (PascalCase, GeneratedApp fallback, digit-prefix) → Task 1. ✓
- `DomainModelMapper.ToClassDefinitions` (fields→properties, field-less→`int Id`) → Task 2. ✓
- `AppScaffolder.WriteClasses` (per-class file, best-effort continue) → Task 3. ✓
- `AppScaffolder.Scaffold` (console/net10.0, reuse existing csproj, `Success` iff all written) → Task 4. ✓
- `ScaffoldResult` record → Task 4. ✓
- `BuildAppCommand` approve wiring + empty-model guard + app-name derivation → Task 4. ✓
- Reference wiring (CodeGenerator.csproj, CodeGenerator.Tests.csproj, slnx) → Tasks 1, 2, 4. ✓
- Tests: name derivation, mapper, file-writing to temp; manual smoke for `dotnet new` → Tasks 1–4. ✓
- Scope boundaries (no relationships/validators/Program.cs wiring/.sln; console only; derived name) → respected; no task adds them. ✓

**Placeholder scan:** No TBD/TODO/"similar to". Every code step shows full code; commands have expected output. ✓

**Type consistency:** `DomainAppName.FromDescription(string):string`; `DomainModelMapper.ToClassDefinitions(CandidateDomainModel):IReadOnlyList<ClassDefinition>`; `AppScaffolder.WriteClasses(string,IReadOnlyList<ClassDefinition>):IReadOnlyList<string>`; `AppScaffolder.Scaffold(CandidateDomainModel,string,string):ScaffoldResult`; `ScaffoldResult(bool,string,IReadOnlyList<string>,string)`. `CandidateEntity.Name`/`CandidateField.Name`/`.Type`, `ClassDefinition.Name`/`.Properties`, `PropertyDefinition.Name`/`.Type`, `ProjectRequest`/`DotNetProjectType.Console`, `DotNetProjectFactory.CreateProject`, `RoslynClassGenerator.Generate` all match the referenced existing signatures. Consistent across tasks. ✓
