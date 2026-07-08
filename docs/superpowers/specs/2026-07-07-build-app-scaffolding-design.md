# Design: `build app:` scaffolding phase — generate a .NET app from the approved model

**Date:** 2026-07-07
**Status:** Approved (pending spec review)
**Area:** CodeGenerator `build app:` → DomainDiscovery → CodeGenerationEngine

## Problem

`build app: <description>` runs domain discovery and an interactive review loop,
but the **approve** branch in `CodeGenerator/App/BuildAppCommand.cs` is a stub:
it prints *"Code generation will be added in the scaffolding phase"* and returns
without generating anything. Approving a model produces no application on disk.

## Goal

On approve, generate a runnable .NET **console project** named from the
description, containing **one C# class per approved entity** (its fields become
public properties), reusing the existing CodeGenerationEngine generators.

Example: `build app: payroll application` → approve →

```
<workspace>/PayrollApplication/
  PayrollApplication.csproj   (console, net10.0)
  Program.cs                  (default from `dotnet new`)
  Payroll.cs                  public class Payroll { public int Id { get; set; } }
  Application.cs              public class Application { public int Id { get; set; } }
```

## Architecture

The orchestration needs both `CandidateDomainModel` (DomainDiscovery) and
`ClassDefinition` / `DotNetProjectFactory` (CodeGenerationEngine). To keep those
two libraries decoupled, the bridge lives in a **new class library
`AppScaffolding`** that references both. `CodeGenerator` (the composition root)
and `CodeGenerator.Tests` reference `AppScaffolding`.

Rejected alternatives: hosting the logic in the `CodeGenerator` exe (awkward to
unit-test); making CodeGenerationEngine depend on DomainDiscovery (wrong
dependency direction).

## Components (new project `AppScaffolding`, `net10.0`)

References: `DomainDiscovery`, `CodeGenerationEngine`.

### 1. `DomainAppName.FromDescription(string description) : string`

Derives a PascalCase project name from the free-text description.

- Split on non-alphanumeric characters; PascalCase each word; concatenate.
- `"payroll application"` → `"PayrollApplication"`.
- Empty / no alphanumeric characters → `"GeneratedApp"`.
- If the result starts with a digit, prefix `App` (valid C# identifier / project name).

### 2. `DomainModelMapper.ToClassDefinitions(CandidateDomainModel model) : IReadOnlyList<ClassDefinition>`

Pure mapping, no I/O.

- Each `CandidateEntity` → one `ClassDefinition { Name = entity.Name }`.
- Each `CandidateField` → `PropertyDefinition { Name = field.Name, Type = field.Type }`
  (field types already carry C# type names from DomainDiscovery's `TypeInference`).
- An entity with **no fields** gets a single `PropertyDefinition { Name = "Id", Type = "int" }`
  so generated classes are never empty.

### 3. `AppScaffolder.Scaffold(CandidateDomainModel model, string appName, string workspaceRoot) : ScaffoldResult`

- `outputFolder = Path.Combine(workspaceRoot, appName)`.
- If `Path.Combine(outputFolder, appName + ".csproj")` does not exist, call
  `DotNetProjectFactory.CreateProject(new ProjectRequest { ProjectName = appName,
  OutputFolder = outputFolder, ProjectType = DotNetProjectType.Console,
  Framework = "net10.0" })`. If that returns `Success == false`, return a failed
  `ScaffoldResult` with the factory's message and write no classes.
- If the `.csproj` already exists, reuse the folder (re-runs are idempotent).
- For each `ClassDefinition` from the mapper: `RoslynClassGenerator.Generate(def)`
  → write to `Path.Combine(outputFolder, def.Name + ".cs")`. Collect written paths.
- Per-file write failures are caught and appended to `Message`; scaffolding
  continues for the remaining classes.
- Returns `ScaffoldResult { Success, ProjectPath, FilesWritten (IReadOnlyList<string>), Message }`.

The class-file writing is a separately callable public method
`AppScaffolder.WriteClasses(string outputFolder, IReadOnlyList<ClassDefinition> classes) : IReadOnlyList<string>`
so it can be unit-tested without invoking `dotnet new`.

### 4. `ScaffoldResult`

`public sealed record ScaffoldResult(bool Success, string ProjectPath, IReadOnlyList<string> FilesWritten, string Message);`

## Wiring (`CodeGenerator/App/BuildAppCommand.cs`)

- In `Handle`, derive `appName = DomainAppName.FromDescription(description)` and
  pass it into the review loop.
- Replace the **approve** stub: call
  `new AppScaffolder().Scaffold(model, appName, workspaceRoot)` and print either
  the created project path + generated file list, or the failure message. Then return.
- An approved model with **zero entities** prints "Nothing to generate — the
  model is empty." and returns (no project created).
- Add a `ProjectReference` to `AppScaffolding` in `CodeGenerator.csproj`.

## Error handling

- `dotnet new` failure → `Success == false`, message surfaced, no class files written.
- Individual class-file write failure → caught, path + error noted in `Message`,
  remaining classes still written, and `Success` set to `false`.
- `Success == true` iff the project was created (or already existed) **and** every
  class file was written without error.
- Empty model → handled in `BuildAppCommand` before calling the scaffolder.

## Testing

`CodeGenerator.Tests` (MSTest) gains `ProjectReference`s to `AppScaffolding` and
`DomainDiscovery`.

- `DomainAppName.FromDescription`: `"payroll application"` → `"PayrollApplication"`;
  `""` → `"GeneratedApp"`; `"123 app"` → starts with a letter.
- `DomainModelMapper.ToClassDefinitions`: entity with two fields → class with two
  matching properties; field-less entity → class with a single `int Id`; multiple
  entities → multiple class definitions preserving names.
- `AppScaffolder.WriteClasses` into a temp folder: one `.cs` file per class, file
  content contains the class name and each property. Temp folder cleaned up.
- **Not unit-tested (needs the SDK / network):** full `dotnet new` project
  creation. Covered by a manual smoke run: pipe `build app: payroll application`
  then `a`, and confirm `<workspace>/PayrollApplication/` contains the `.csproj`
  and `Payroll.cs` / `Application.cs`.

## Scope boundaries (YAGNI)

- No relationships / foreign keys between generated classes.
- No validators, no `Program.cs` wiring to the entities, no `.sln`.
- No project-type choice (always console) and no prompt for the app name
  (derived from the description; the user can rename the folder afterward).

## Files touched

- Create: `AppScaffolding/AppScaffolding.csproj`, `AppScaffolding/DomainAppName.cs`,
  `AppScaffolding/DomainModelMapper.cs`, `AppScaffolding/AppScaffolder.cs`,
  `AppScaffolding/ScaffoldResult.cs`.
- Modify: `CodeGenerator/CodeGenerator.csproj` (add reference),
  `CodeGenerator/App/BuildAppCommand.cs` (derive name, wire approve),
  `CodeGenerator.Tests/CodeGenerator.Tests.csproj` (add references),
  `SmartCodeGenerator/SmartCodeGenerator.slnx` (add the new project).
- Create tests under `CodeGenerator.Tests/App/`.

## Risks

- `RoslynClassGenerator` emits classes in the fixed `GeneratedCode` namespace, so
  the default `Program.cs` won't reference the entity classes. Acceptable — the
  scaffold is a starting point, not a wired-up app (documented in scope).
- `dotnet new` requires the .NET SDK on PATH and is slow; the scaffolder shells
  out via the existing `DotNetProjectFactory`. Same constraint the CLI already has.
