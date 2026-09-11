# Validation Phase 2: The Pipeline and Enforcement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `IValidator<T>.Validate(T item)` with an ordered seven-stage pipeline over a wide context, and give the Security stage real teeth at the three points that can hold them.

**Architecture:** A `ValidationContext<T>` carries the record, the operation, the stored version, the lookup and the acting user's role. `ValidationPipeline.Run` holds the ordering and the short-circuit; each generated validator declares only its non-empty stages as private methods. Enforcement lands at three points because no single one covers every front end: `Repository<T>` (full pipeline, all four operations), `IStore<T>.SaveAll` (a coarse "may this user write this type at all", unbypassable), and the grids' own delete buttons (the deferred-save UI cannot express the operation at save time).

**Tech Stack:** C#, .NET 10 (`net10.0`, `net10.0-windows` for UI projects), MSTest for `CodeGenerator.Tests`, xUnit for the other four test projects, Roslyn for compile-testing emitted code.

**Spec:** `docs/superpowers/specs/2026-09-06-validation-architecture-design.md`, as amended in `d4f4d8c`.

## Global Constraints

- **Build must pass `-warnaserror` in Release.** CI runs `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-restore -warnaserror` then `dotnet test ... --no-build`. A local Debug build of one project hides analyzer errors.
- **Analyzers only surface on a non-incremental build.** Any `-warnaserror` check needs `--no-incremental`.
- **`[DataTestMethod]` is MSTEST0044 and an ERROR under `-warnaserror`.** Use `[TestMethod]` with `[DataRow]`. This cost a red build in phase 1.
- **`dotnet test --no-build` after a FAILED build runs the previous binaries and reports green.** Read the build result, not only the test summary.
- **MSTEST0037 rejects `Assert.AreEqual(n, x.Count)`.** Use `Assert.HasCount(n, collection)`. MSTest projects only.
- **The test framework follows the project — this repo is mixed.** `CodeGenerator.Tests` is MSTest (`[TestClass]`, `[TestMethod]`, `Assert.*`). `DataIngestion.Tests`, `DomainDiscovery.Tests`, `CodeIntelligence.Tests` and `NaturalLanguageEngine.Tests` are xUnit (`[Fact]`, `[Theory]`, `Assert.Equal`). Every task in this plan targets `CodeGenerator.Tests`, so every test body here is MSTest.
- **Emitted C# is tested by compiling it**, never by substring assertions alone. Use `Compiler.Compile` / `Compiler.CompileSources`. Where a class is emitted as two partial halves, compile **both files together** with `CompileSources` — a concatenation hides CS0260.
- **Generated apps are written to `D:\SampleProjects\<AppName>`**, not the working directory. Probe folders need a SHORT path: the scratchpad blows MAX_PATH, and the tell is MSB3030 reporting a file not found that plainly exists.
- **Rebuild the CLI before driving it by hand.** `dotnet test` leaves `CodeGenerator/bin` stale and a green fix can look dead.

## Two limits this phase cannot lift, and must not claim to

**1. Four of the seven targets have no user at all.** `ScreenPlan.For` computes `login = customization.Login && target.HasScreens`, and `HasScreens` is `true` only in `BlazorTarget`, `WinFormsTarget` and `WpfTarget` (`ITargetScaffolder.cs:60` defaults it false). So a console, webapi, worker or classlib app emits no auth core: no `User` type, no `Session`, no role. The Security stage in those apps is genuinely empty, and **the spec's behaviour-change bullet claiming "a Web API or console caller that previously deleted a master record freely is now refused" is false.** Task 12 corrects that bullet. Giving a Web API its own authentication is a feature, not a phase-2 correction, and is out of scope.

**2. Under the default matrix the coarse `SaveAll` guard refuses nothing but the `User` entity.** A User may write every business entity and is barred only from *deleting* a master record, which `SaveAll` cannot see. The guard is real and unbypassable and nearly inert; the teeth are in `Repository<T>` and at the grids' delete buttons. This is already stated in the spec and must not be quietly oversold in a commit message.

## A deviation from the spec, with its reason

The spec's context reads `User? User`. **This plan carries `string? UserRole` and `int? UserId` instead.** The `User` type is emitted by `AuthCoreFiles` only when the auth core is emitted, so a `ValidationContext<T>` naming it would not compile in the console, webapi, worker or classlib targets — where the Validation folder is still generated. Two primitives keep the pipeline compiling in all seven targets, carry everything the permission matrix needs, and leave identity available for the later phases. Nothing else in the spec depends on the parameter being the entity type.

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `AppScaffolding/ValidationContextTemplate.cs` | Emits `Validation/ValidationContext.cs`: the operation enum, the error and outcome records, the context | Create |
| `AppScaffolding/ValidationPipelineTemplate.cs` | Emits `Validation/ValidationPipeline.cs`: the ordering and the short-circuit | Create |
| `AppScaffolding/PermissionsTemplate.cs` | Emits `Validation/Permissions.cs`: the default matrix. Emitted even under `novalidation` | Create |
| `AppScaffolding/ValidatorTemplate.cs` | `IValidator<T>` widens; `IEntityLookup` gains `CountReferencing`; `Generated` emits stage methods and a stage list; `NullValidator` follows the seam | Modify |
| `AppScaffolding/RepositoryTemplate.cs:55-215` | Runs the pipeline on Create, Update and Delete; `Delete` returns `SaveResult<T>` | Modify |
| `AppScaffolding/StoreSeamTemplate.cs` | `SaveAll` carries a refusal reason | Modify |
| `AppScaffolding/JsonStoreTemplate.cs:45-56`, `AppScaffolding/EfStoreTemplate.cs:31` | Implement the widened `SaveAll` and call the coarse guard | Modify |
| `AppScaffolding/SaveResultTemplate.cs` | Gains `Failures`; `AsDictionary` keyed by property | Modify |
| `AppScaffolding/ConsoleMenuTemplate.cs:74,86` | Names the property in an error line; reads the new `Delete` result | Modify |
| `AppScaffolding/WebApiProgramTemplate.cs:66,79,83` | Reads the new `Delete` result | Modify |
| `AppScaffolding/ViewModelTemplates.cs:175-191,265-280` | Builds a context; `Delete()` gains its first permission check | Modify |
| `AppScaffolding/Screens/MasterScreenTemplate.cs:347` | `Session.IsAdmin` becomes a matrix call | Modify |
| `AppScaffolding/Screens/BlazorScreenTemplates.cs:726,870,887,905` | Builds a context; delete asks the matrix | Modify |
| `AppScaffolding/Screens/RecordEditorTemplate.cs:49` | Builds a context; highlights the failing field | Modify |
| `AppScaffolding/TestProjectTemplates.cs:47,72,100,108` | The generated test project follows the seam | Modify |
| `AppScaffolding/AppScaffolder.cs:418-480` | Emits the four new Validation files; `Permissions.cs` outside the `validation` flag | Modify |
| `CodeGenerationEngine/Core/CommandDictionary.cs:692-701` | `novalidation` no longer claims to omit the whole folder | Modify |
| `docs/commands.md`, the manual's chapter-18 appendix | Regenerated together | Modify |

---

### Task 1: The context, the outcome, and the widened seam

Everything downstream names these types, so they land first and alone.

**Files:**
- Create: `AppScaffolding/ValidationContextTemplate.cs`
- Modify: `AppScaffolding/ValidatorTemplate.cs` (`ValidatorInterface`, `EntityLookupInterface`)
- Test: `CodeGenerator.Tests/App/ValidationContextTemplateTests.cs`

**Interfaces:**
- Produces: `ValidationContextTemplate.Source(string rootNamespace)` returning the text of `Validation/ValidationContext.cs`. It declares `ValidationOperation { Insert, Update, Delete, Post }`, `ValidationError(string? Property, string Message)`, `ValidationOutcome(IReadOnlyList<ValidationError> Errors)` with `IsValid`, and `ValidationContext<T>(T Item, ValidationOperation Op, T? Stored, IEntityLookup Lookup, string? UserRole, int? UserId)` plus a static `ValidationContext<T>.For(T item, ValidationOperation op)` convenience used by tests only.
- Produces: `ValidatorTemplate.ValidatorInterface` now declaring `ValidationOutcome Validate(ValidationContext<T> context)`.
- Produces: `ValidatorTemplate.EntityLookupInterface` now also declaring `int CountReferencing<TEntity>(string foreignKeyProperty, int id) where TEntity : class, new()`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/ValidationContextTemplateTests.cs`:

```csharp
using AppScaffolding;
using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ValidationContextTemplateTests
{
    [TestMethod]
    public void TheContextAndTheWidenedSeam_CompileTogether()
    {
        // Compiled, not asserted by substring: these four types are named by every later task,
        // and a record whose positional parameters do not line up is invisible to a Contains.
        var result = Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
        });

        var errors = string.Join("\n",
            result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(e => e.ToString()));

        Assert.IsTrue(result.Success, $"Compilation failed:\n{errors}");
    }

    [TestMethod]
    public void TheOperationEnum_CarriesAllFour()
    {
        var source = ValidationContextTemplate.Source("Acme");

        StringAssert.Contains(source, "enum ValidationOperation");
        StringAssert.Contains(source, "Insert");
        StringAssert.Contains(source, "Update");
        StringAssert.Contains(source, "Delete");

        // Post has no caller until phase 6's posting gate. It is here deliberately, so the
        // enum a generated app exposes does not have to widen later.
        StringAssert.Contains(source, "Post");
    }

    [TestMethod]
    public void TheContext_NamesNoAuthType()
    {
        // The console, webapi, worker and classlib targets generate the Validation folder but
        // NO auth core, so there is no User type in them. A context naming it would not
        // compile in four of the seven targets; the role and the id are what the matrix needs.
        var source = ValidationContextTemplate.Source("Acme");

        Assert.DoesNotContain("Acme.Auth", source);
        Assert.DoesNotContain("User? User", source);
        StringAssert.Contains(source, "string? UserRole");
        StringAssert.Contains(source, "int? UserId");
    }

    [TestMethod]
    public void AnOutcomeWithNoErrors_IsValid()
    {
        var source = ValidationContextTemplate.Source("Acme");

        StringAssert.Contains(source, "public bool IsValid => Errors.Count == 0;");
    }

    [TestMethod]
    public void TheSeam_TakesAContextAndReturnsAnOutcome()
    {
        // Replaced, not supplemented. A convenience overload taking a bare item would have to
        // invent an operation and a null role, so a caller reaching for it gets an inert
        // Security stage and a validator reporting success over a check that never ran.
        var source = ValidatorTemplate.ValidatorInterface("Acme");

        StringAssert.Contains(source, "ValidationOutcome Validate(ValidationContext<T> context);");
        Assert.DoesNotContain("Validate(T item)", source);
    }

    [TestMethod]
    public void TheLookup_CanCountReferences()
    {
        var source = ValidatorTemplate.EntityLookupInterface("Acme");

        StringAssert.Contains(source,
            "int CountReferencing<TEntity>(string foreignKeyProperty, int id)");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter ValidationContextTemplateTests`
Expected: FAIL to compile — `ValidationContextTemplate` does not exist.

- [ ] **Step 3: Create `ValidationContextTemplate`**

Create `AppScaffolding/ValidationContextTemplate.cs`:

```csharp
namespace AppScaffolding;

/// <summary>Emits <c>Validation/ValidationContext.cs</c>: what a stage is given to judge.</summary>
/// <remarks>
/// The context carries the acting user's ROLE and ID rather than the app's own <c>User</c> type.
/// <c>AuthCoreFiles</c> emits that type only when the auth core is emitted — a login AND a target
/// with screens — so the console, webapi, worker and classlib targets have no such type while
/// still generating this folder. Two primitives compile in all seven targets and carry everything
/// the permission matrix reads.
/// </remarks>
public static class ValidationContextTemplate
{
    /// <summary>Source of <c>Validation/ValidationContext.cs</c> in
    /// <paramref name="rootNamespace"/>.Validation.</summary>
    public static string Source(string rootNamespace) => $$"""
using System.Collections.Generic;

namespace {{rootNamespace}}.Validation;

/// <summary>Which write is being attempted.</summary>
/// <remarks>
/// Post has no caller yet: the posting gate over a header and its lines arrives with the
/// Transaction stage. It is declared now so the enum a generated app exposes does not widen later.
/// </remarks>
public enum ValidationOperation
{
    Insert,
    Update,
    Delete,
    Post
}

/// <summary>One broken rule. A null <see cref="Property"/> means the record as a whole.</summary>
public sealed record ValidationError(string? Property, string Message);

/// <summary>Everything a run of the pipeline found.</summary>
public sealed record ValidationOutcome(IReadOnlyList<ValidationError> Errors)
{
    /// <summary>An outcome with nothing in it.</summary>
    public static ValidationOutcome Valid { get; } = new(new List<ValidationError>());

    /// <summary>True when no rule was broken.</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>Just the sentences, for a caller that only prints them.</summary>
    public IReadOnlyList<string> Messages
    {
        get
        {
            var messages = new List<string>(Errors.Count);

            foreach (var error in Errors)
                messages.Add(error.Message);

            return messages;
        }
    }
}

/// <summary>
/// What one validation is given: the record as submitted, which operation is being attempted,
/// what is on disk now, a lookup for other records, and who is acting.
/// </summary>
/// <param name="Stored">
/// What is on disk now. Null on Insert, and null wherever a caller cannot supply it — a stage
/// that needs it must handle null rather than assume an Update always has one.
/// </param>
/// <param name="UserRole">
/// The acting user's role, or null when this app has no login at all. Null means "no notion of
/// who is acting", which the permission matrix reads as allowed — an app generated without a
/// login has nobody to refuse.
/// </param>
public sealed record ValidationContext<T>(
    T Item,
    ValidationOperation Op,
    T? Stored,
    IEntityLookup Lookup,
    string? UserRole,
    int? UserId)
{
    /// <summary>A context with no stored version, no user and a lookup that finds nothing.
    /// For tests and for a caller that genuinely has none of those.</summary>
    public static ValidationContext<T> For(T item, ValidationOperation op, IEntityLookup lookup)
        => new(item, op, default, lookup, null, null);
}
""";
}
```

- [ ] **Step 4: Widen the two interfaces**

In `AppScaffolding/ValidatorTemplate.cs`, replace the body of `ValidatorInterface`:

```csharp
    /// <summary>Source of <c>Validation/IValidator.cs</c>.</summary>
    public static string ValidatorInterface(string rootNamespace) => $$"""
namespace {{rootNamespace}}.Validation;

/// <summary>Validates one entity against the whole picture: the record, the operation, the stored
/// version, other records, and the acting user.</summary>
public interface IValidator<T>
{
    /// <summary>Every rule this operation breaks. An outcome with no errors means valid.</summary>
    ValidationOutcome Validate(ValidationContext<T> context);
}
""";
```

and add the counting method to `EntityLookupInterface`:

```csharp
    /// <summary>Source of <c>Validation/IEntityLookup.cs</c>.</summary>
    public static string EntityLookupInterface(string rootNamespace) => $$"""
namespace {{rootNamespace}}.Validation;

/// <summary>Existence and reference counts a validator needs. Injected so a validator can be
/// unit-tested with a fake and no store on disk.</summary>
public interface IEntityLookup
{
    /// <summary>True when a record of this type with this Id exists.</summary>
    bool Exists<TEntity>(int id) where TEntity : class, new();

    /// <summary>How many records of this type carry this foreign key value.</summary>
    int CountReferencing<TEntity>(string foreignKeyProperty, int id)
        where TEntity : class, new();
}
""";
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter ValidationContextTemplateTests`
Expected: PASS, 6 tests.

The rest of the suite is expected to be RED after this step — every existing caller of `Validate(T item)` and every implementer of `IEntityLookup` is now broken. That is the point of replacing the signature, and Tasks 2 to 4 close it. Do not chase those failures here.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/ValidationContextTemplate.cs AppScaffolding/ValidatorTemplate.cs CodeGenerator.Tests/App/ValidationContextTemplateTests.cs
git commit -m "Widen the validation seam to a context and an outcome"
```

---

---

---

### Task 2: The pipeline, and stages as methods

**Files:**
- Create: `AppScaffolding/ValidationPipelineTemplate.cs`
- Test: `CodeGenerator.Tests/App/ValidationPipelineTemplateTests.cs`

**Interfaces:**
- Consumes: `ValidationContextTemplate.Source` from Task 1.
- Produces: `ValidationPipelineTemplate.Source(string rootNamespace)` emitting `Validation/ValidationPipeline.cs`, which declares `delegate void Stage<T>(ValidationContext<T> context, List<ValidationError> errors)` and `static ValidationOutcome Run<T>(Stage<T>[] stages, ValidationContext<T> context)`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/ValidationPipelineTemplateTests.cs`:

```csharp
using AppScaffolding;
using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ValidationPipelineTemplateTests
{
    /// <summary>Drives the emitted pipeline for real: three stages, the middle one failing.</summary>
    private const string Driver = """
using System.Collections.Generic;
using Acme.Validation;

namespace Acme.Probe;

public sealed class NoLookup : IEntityLookup
{
    public bool Exists<TEntity>(int id) where TEntity : class, new() => true;

    public int CountReferencing<TEntity>(string foreignKeyProperty, int id)
        where TEntity : class, new() => 0;
}

public static class Probe
{
    public static List<string> Ran { get; } = new();

    public static ValidationOutcome ThreeStages()
    {
        ValidationPipeline.Stage<string>[] stages =
        [
            (context, errors) => Ran.Add("first"),
            (context, errors) =>
            {
                Ran.Add("second");
                errors.Add(new ValidationError("A", "a is wrong"));
                errors.Add(new ValidationError("B", "b is wrong"));
            },
            (context, errors) => Ran.Add("third"),
        ];

        return ValidationPipeline.Run(
            stages,
            ValidationContext<string>.For("x", ValidationOperation.Insert, new NoLookup()));
    }
}
""";

    [TestMethod]
    public void AStageCollectsEveryError_AndThePipelineStopsAtTheFirstFailingStage()
    {
        // Both halves of the short-circuit rule, run rather than read. Collecting within a stage
        // is what lets a form show all its bad fields at once; stopping between stages is what
        // keeps a range check away from a value that is not a number.
        var probe = Compile();

        var outcome = probe.GetType("Acme.Probe.Probe")!.GetMethod("ThreeStages")!.Invoke(null, null)!;
        var errors = (System.Collections.ICollection)outcome.GetType()
            .GetProperty("Errors")!.GetValue(outcome)!;

        Assert.HasCount(2, errors);

        var ran = (System.Collections.IEnumerable)probe.GetType("Acme.Probe.Probe")!
            .GetProperty("Ran")!.GetValue(null)!;

        var names = new List<string>();
        foreach (var name in ran)
            names.Add((string)name);

        CollectionAssert.AreEqual(new[] { "first", "second" }, names,
            "the third stage ran after an earlier one had already failed");
    }

    [TestMethod]
    public void NoStageFails_AndTheOutcomeIsValid()
    {
        var source = ValidationPipelineTemplate.Source("Acme");

        // The empty case returns the shared Valid instance rather than allocating a list.
        StringAssert.Contains(source, "ValidationOutcome.Valid");
    }

    private static System.Reflection.Assembly Compile()
    {
        var result = Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/ValidationPipeline.cs", ValidationPipelineTemplate.Source("Acme")),
            ("Probe/Probe.cs", Driver),
        });

        var errors = string.Join("\n",
            result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(e => e.ToString()));

        Assert.IsTrue(result.Success, $"Compilation failed:\n{errors}");

        return System.Reflection.Assembly.Load(result.AssemblyBytes!);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter ValidationPipelineTemplateTests`
Expected: FAIL to compile — `ValidationPipelineTemplate` does not exist.

- [ ] **Step 3: Create `ValidationPipelineTemplate`**

Create `AppScaffolding/ValidationPipelineTemplate.cs`:

```csharp
namespace AppScaffolding;

/// <summary>Emits <c>Validation/ValidationPipeline.cs</c>: the stage ordering and the
/// short-circuit, in one place for the whole app.</summary>
/// <remarks>
/// A stage is a DELEGATE over a method on the generated validator, not a class. Seven classes per
/// entity would be seven files per entity, and an app whose entities have only field rules would
/// carry six empty ones each; a method list means an empty stage emits nothing at all, which is
/// what keeps a three-entity CRUD app's output no heavier than before this phase.
/// </remarks>
public static class ValidationPipelineTemplate
{
    /// <summary>Source of <c>Validation/ValidationPipeline.cs</c> in
    /// <paramref name="rootNamespace"/>.Validation.</summary>
    public static string Source(string rootNamespace) => $$"""
using System.Collections.Generic;

namespace {{rootNamespace}}.Validation;

/// <summary>Runs an entity's stages in order and stops at the first one that found anything.</summary>
public static class ValidationPipeline
{
    /// <summary>One stage: adds an error per rule it finds, and adds nothing when it finds none.</summary>
    public delegate void Stage<T>(ValidationContext<T> context, List<ValidationError> errors);

    /// <summary>
    /// Every error the first failing stage found, or a valid outcome when none failed.
    /// </summary>
    /// <remarks>
    /// A stage collects EVERY error it finds, so a form can show all its bad fields at once. The
    /// pipeline stops BETWEEN stages, which is what keeps a range check away from a value that is
    /// not a number, and what makes a posting gate a property of the ordering rather than an
    /// extra check: a Post commits only when every stage returned nothing.
    /// </remarks>
    public static ValidationOutcome Run<T>(Stage<T>[] stages, ValidationContext<T> context)
    {
        var errors = new List<ValidationError>();

        foreach (var stage in stages)
        {
            stage(context, errors);

            if (errors.Count > 0)
                return new ValidationOutcome(errors);
        }

        return ValidationOutcome.Valid;
    }
}
""";
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter ValidationPipelineTemplateTests`
Expected: PASS, 2 tests.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/ValidationPipelineTemplate.cs CodeGenerator.Tests/App/ValidationPipelineTemplateTests.cs
git commit -m "Add the pipeline: stages in order, stopping at the first that fails"
```

---

---

---

### Task 3: The permission matrix, emitted even when validation is not

**Files:**
- Create: `AppScaffolding/PermissionsTemplate.cs`
- Test: `CodeGenerator.Tests/App/PermissionsTemplateTests.cs`

**Interfaces:**
- Produces: `PermissionsTemplate.Source(string rootNamespace, IReadOnlyList<string> masterEntityNames)` emitting `Validation/Permissions.cs` with `static bool Allows(ValidationOperation op, string entity, string? role)` and `static string Refusal(ValidationOperation op, string entity)`.
- The master names come from `AppScaffolder`'s `plan.Roles` (`EntityRoleMap`) at generation time — the same classification the review summary reports, so a screen and its permission cannot disagree.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/PermissionsTemplateTests.cs`:

```csharp
using AppScaffolding;
using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

[TestClass]
public class PermissionsTemplateTests
{
    private const string Driver = """
using Acme.Validation;

namespace Acme.Probe;

public static class Probe
{
    public static bool Allows(string op, string entity, string? role)
        => Permissions.Allows(
            op switch
            {
                "Insert" => ValidationOperation.Insert,
                "Update" => ValidationOperation.Update,
                "Delete" => ValidationOperation.Delete,
                _ => ValidationOperation.Post,
            },
            entity,
            role);
}
""";

    [TestMethod]
    [DataRow("Admin", "Customer", "Delete", true)]
    [DataRow("Admin", "User", "Delete", true)]
    [DataRow("User", "Customer", "Update", true)]
    [DataRow("User", "Order", "Delete", true)]
    [DataRow("User", "Customer", "Delete", false)]
    [DataRow("User", "User", "Update", false)]
    [DataRow(null, "Customer", "Delete", true)]
    public void TheDefaultMatrix_PreservesTodaysBehaviour(
        string? role, string entity, string op, bool expected)
    {
        // Today's whole rule, in one table: an Admin may do anything; a User may do anything
        // except delete a MASTER record or touch Users; and an app with no login at all refuses
        // nothing, because it has no notion of who is acting. Customer is a master here and
        // Order is a transaction.
        //
        // The null-role row is the one that matters most: four of the seven targets emit no auth
        // core, so every operation in a console or Web API app arrives with a null role. If this
        // returned false, those apps would refuse every write.
        var probe = Compile();

        var allows = (bool)probe.GetType("Acme.Probe.Probe")!
            .GetMethod("Allows")!.Invoke(null, [role, entity, op])!;

        Assert.AreEqual(expected, allows);
    }

    [TestMethod]
    public void TheRefusalNamesTheOperationAndTheEntity_ButNoField()
    {
        // Security runs first precisely so an unauthorised user is not told which fields were
        // wrong. The sentence must not leak one.
        var source = PermissionsTemplate.Source("Acme", ["Customer"]);

        StringAssert.Contains(source, "public static string Refusal(");
        StringAssert.Contains(source, "not permitted");
    }

    [TestMethod]
    public void AnAppWithNoMasters_StillCompiles()
    {
        var result = Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", [])),
        });

        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(e => e.ToString())));
    }

    private static System.Reflection.Assembly Compile()
    {
        var result = Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", ["Customer"])),
            ("Probe/Probe.cs", Driver),
        });

        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(e => e.ToString())));

        return System.Reflection.Assembly.Load(result.AssemblyBytes!);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter PermissionsTemplateTests`
Expected: FAIL to compile — `PermissionsTemplate` does not exist.

- [ ] **Step 3: Create `PermissionsTemplate`**

Create `AppScaffolding/PermissionsTemplate.cs`:

```csharp
namespace AppScaffolding;

/// <summary>Emits <c>Validation/Permissions.cs</c>: who may do what.</summary>
/// <remarks>
/// Emitted whether or not <c>novalidation</c> was given. Turning validation off must not turn
/// permissions off — that is the failure the requirement's own seventeenth item warns about.
/// <para>
/// Nothing here is inferred: permissions are in nobody's data. The default matrix reproduces what
/// the generated apps already displayed — an Admin may do anything, a User may do anything except
/// delete a master record or reach the Users screen — and the change is that it is now ENFORCED
/// rather than only shown.
/// </para>
/// </remarks>
public static class PermissionsTemplate
{
    /// <summary>Source of <c>Validation/Permissions.cs</c>, with
    /// <paramref name="masterEntityNames"/> from the build's own role classification.</summary>
    public static string Source(string rootNamespace, IReadOnlyList<string> masterEntityNames)
    {
        var masters = masterEntityNames.Count == 0
            ? string.Empty
            : string.Join(", ", masterEntityNames.Select(name => $"\"{name}\""));

        return $$"""
using System;
using System.Collections.Generic;

namespace {{rootNamespace}}.Validation;

/// <summary>Who may perform which operation on which entity.</summary>
public static class Permissions
{
    /// <summary>The role name that may do anything.</summary>
    private const string AdministratorRole = "Admin";

    /// <summary>The entity holding the app's own user accounts.</summary>
    private const string UserEntity = "User";

    /// <summary>The entities this build classified as masters, from the same classification the
    /// screens use — so a screen and its permission cannot disagree.</summary>
    private static readonly HashSet<string> Masters =
        new(new[] { {{masters}} }, StringComparer.Ordinal);

    /// <summary>Whether <paramref name="role"/> may perform <paramref name="op"/> on
    /// <paramref name="entity"/>.</summary>
    /// <remarks>
    /// A null role means this app has no login at all — the auth core is emitted only for a
    /// target with screens — so there is no notion of who is acting and nothing to refuse.
    /// Returning false for a null role would refuse every write in every console, Web API and
    /// worker app.
    /// </remarks>
    public static bool Allows(ValidationOperation op, string entity, string? role)
    {
        if (role is null)
            return true;

        if (string.Equals(role, AdministratorRole, StringComparison.Ordinal))
            return true;

        if (string.Equals(entity, UserEntity, StringComparison.Ordinal))
            return false;

        return op != ValidationOperation.Delete || !Masters.Contains(entity);
    }

    /// <summary>The sentence shown when <see cref="Allows"/> said no.</summary>
    /// <remarks>
    /// Names the operation and the entity and NOTHING about the record's fields. The Security
    /// stage runs first so that an unauthorised user is refused without being told which fields
    /// were wrong, and a message leaking one would undo that.
    /// </remarks>
    public static string Refusal(ValidationOperation op, string entity)
        => $"You are not permitted to {op.ToString().ToLowerInvariant()} a {entity} record.";
}
""";
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter PermissionsTemplateTests`
Expected: PASS, 9 tests.

- [ ] **Step 5: Emit the four new files from the scaffolder**

In `AppScaffolding/AppScaffolder.cs`, in the block around lines 418-480 that emits the Validation folder:
- add `ValidationContextTemplate.Source(rootNamespace)`, `ValidationPipelineTemplate.Source(rootNamespace)` and `ValidatorTemplate.StoreEntityLookup(rootNamespace)` inside the existing `if (validation)` block;
- add `PermissionsTemplate.Source(rootNamespace, masterNames)` **outside** it, so it is written whether or not `validation` is true. `masterNames` comes from `plan.Roles`.

Where the pipeline and context types are needed by `Permissions` but `validation` is false, `ValidationContext.cs` must also be emitted outside the flag — `Permissions` names `ValidationOperation`. Emit `ValidationContext.cs` and `Permissions.cs` unconditionally; `ValidationPipeline.cs`, `IValidator.cs`, `IEntityLookup.cs`, `StoreEntityLookup.cs`, `ValidatorFactory.cs` and the per-model validators stay behind the flag.

- [ ] **Step 6: Write the test that pins that split**

Add to `CodeGenerator.Tests/App/NoValidationTests.cs`:

```csharp
    [TestMethod]
    public void NoValidation_StillEmitsThePermissionMatrix()
    {
        // The whole reason Security sits outside the flag: turning validation off must not turn
        // permissions off. Asserted against the emitted file list, because this is the one
        // guarantee a user cannot see until something is refused in a running app.
        var files = ScaffoldedFiles(validation: false);

        Assert.IsTrue(files.Any(f => f.EndsWith("Permissions.cs", System.StringComparison.Ordinal)),
            "novalidation dropped the permission matrix");
        Assert.IsTrue(files.Any(f => f.EndsWith("ValidationContext.cs", System.StringComparison.Ordinal)),
            "Permissions names ValidationOperation, so the context must survive the flag too");

        Assert.IsFalse(files.Any(f => f.EndsWith("ValidationPipeline.cs", System.StringComparison.Ordinal)),
            "novalidation must still drop the pipeline");
    }
```

> `ScaffoldedFiles` must be taken from the existing fixture pattern in `NoValidationTests.cs` rather than written fresh — reuse whatever that file already uses to scaffold and enumerate, renaming only if its helper differs.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~PermissionsTemplateTests|FullyQualifiedName~NoValidationTests"`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add AppScaffolding/PermissionsTemplate.cs AppScaffolding/AppScaffolder.cs CodeGenerator.Tests/App/PermissionsTemplateTests.cs CodeGenerator.Tests/App/NoValidationTests.cs
git commit -m "Emit the permission matrix, and keep it outside the novalidation flag"
```

---

---

---

### Task 4: The generated validator declares only its non-empty stages

Today `ValidatorTemplate.Generated` writes one flat `Validate` body. It becomes a stage list plus one method per non-empty stage, with every rule phase 1 emits moving into the Field stage unchanged.

**Files:**
- Modify: `AppScaffolding/ValidatorTemplate.cs` (`Generated`, `NullValidator`, `Checks`)
- Test: `CodeGenerator.Tests/App/ValidatorStagesTests.cs`
- Test: `CodeGenerator.Tests/App/ValidatorEmitsEveryRuleKindTests.cs` (existing — its `GeneratedFor` helper and its compile helper follow the new seam)

**Interfaces:**
- Consumes: `ValidationContextTemplate` from Task 1, `ValidationPipelineTemplate` from Task 2, `PermissionsTemplate` from Task 3.
- Produces: `ValidatorTemplate.Generated(string rootNamespace, ClassDefinition definition, IReadOnlyList<string> foreignKeyTargets)` — signature unchanged — emitting `Validate(ValidationContext<T>)` over a `Stages` array. Errors are raised as `new ValidationError("<PropertyName>", "<sentence>")`, so attribution exists from the point of the check.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/ValidatorStagesTests.cs`:

```csharp
using AppScaffolding;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ValidatorStagesTests
{
    [TestMethod]
    public void AModelWithOnlyFieldRules_DeclaresOneStage()
    {
        // The mitigation for "seven stages is more machinery than a three-entity CRUD app needs",
        // asserted rather than assumed. Six empty stage methods per entity would be six times the
        // noise in the commonest app there is.
        var source = GeneratedFor(WithSurname());

        StringAssert.Contains(source, "private static void Field(");
        Assert.DoesNotContain("private static void Entity(", source);
        Assert.DoesNotContain("private static void Business(", source);
        Assert.DoesNotContain("private static void Transaction(", source);
        Assert.DoesNotContain("private static void Workflow(", source);
        Assert.DoesNotContain("private static void Database(", source);
    }

    [TestMethod]
    public void TheStageList_NamesTheStagesInSpecOrder()
    {
        // Security first, then Field. The ordering is the design, so it is asserted, not implied.
        var definition = WithSurname();
        var source = GeneratedFor(definition);

        StringAssert.Contains(source, "Stages =");
        var security = source.IndexOf("Security", System.StringComparison.Ordinal);
        var field = source.IndexOf("Field", System.StringComparison.Ordinal);

        Assert.IsTrue(security >= 0, "the Security stage is always present");
        Assert.IsTrue(security < field, "Security must run before Field");
    }

    [TestMethod]
    public void AModelWithNoRulesAtAll_StillCompilesAndStillDeclaresSecurity()
    {
        // Security is not conditional on there being any inferred rule: it is the one stage that
        // exists because of the operation, not because of the data.
        var definition = new ClassDefinition { Name = "Note" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        var source = GeneratedFor(definition);

        StringAssert.Contains(source, "private static void Security(");
    }

    [TestMethod]
    public void EveryErrorCarriesItsProperty()
    {
        // Attribution at the point the check is written, which is what SaveResult later reads.
        // A sentence with a null property would surface as a whole-record error and no front end
        // could highlight the field.
        var source = GeneratedFor(WithSurname());

        StringAssert.Contains(source, "new ValidationError(\"Surname\"");
        Assert.DoesNotContain("new ValidationError(null, \"Surname", source);
    }

    private static ClassDefinition WithSurname()
    {
        var definition = new ClassDefinition { Name = "Customer" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        var surname = new PropertyDefinition { Name = "Surname", Type = "string" };
        surname.Rules.Add(new PropertyRule { Name = "required" });
        surname.Rules.Add(new PropertyRule { Name = "maxlength", MaxLength = 50 });
        definition.Properties.Add(surname);

        return definition;
    }

    private static string GeneratedFor(ClassDefinition definition)
        => ValidatorTemplate.Generated("Acme", definition, []);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter ValidatorStagesTests`
Expected: FAIL — the emitted validator has one flat `Validate` body and no stage methods.

- [ ] **Step 3: Restructure `Generated`**

In `AppScaffolding/ValidatorTemplate.cs`, `Generated` keeps its signature and its two-partial-halves shape. The body becomes:

```csharp
public sealed partial class {{className}}Validator : IValidator<{{className}}>
{
    private readonly IEntityLookup _lookup;

    private static readonly ValidationPipeline.Stage<{{className}}>[] Stages =
    [
{{stageList}}
    ];

    /// <summary>Validates against the real store.</summary>
    public {{className}}Validator() : this(new StoreEntityLookup()) { }

    /// <summary>Validates against the supplied lookup. Tests pass a fake.</summary>
    public {{className}}Validator(IEntityLookup lookup) => _lookup = lookup;

    /// <inheritdoc />
    public ValidationOutcome Validate(ValidationContext<{{className}}> context)
        => ValidationPipeline.Run(Stages, context);

{{stageMethods}}
    /// <summary>Hand-written rules, implemented in {{className}}Validator.Custom.cs.</summary>
    partial void ValidateCustom({{className}} item, List<ValidationError> errors);
}
```

`stageList` holds one line per non-empty stage, in spec order, always including `Security`:

```csharp
        Security,
        Field,
```

`stageMethods` holds one method per named stage. `Security` is:

```csharp
    /// <summary>Refuses an operation this user may not perform, before any field is examined —
    /// so an unauthorised caller is never told which fields were wrong.</summary>
    private static void Security(ValidationContext<{{className}}> context, List<ValidationError> errors)
    {
        if (!Permissions.Allows(context.Op, "{{className}}", context.UserRole))
            errors.Add(new ValidationError(null, Permissions.Refusal(context.Op, "{{className}}")));
    }
```

`Field` carries exactly what the current flat body carried — the null guard, the per-property `try` blocks and the `ValidateCustom` call — with two changes only:
- every `errors.Add("<sentence>")` becomes `errors.Add(new ValidationError("<PropertyName>", "<sentence>"))`;
- the null-item guard raises `new ValidationError(null, "{{className}} is null.")`.

Keep the per-property `try { } catch (Exception) { }` blocks exactly as they are. They exist so one malformed property cannot stop the rest being reported, and nothing in this task changes that.

- [ ] **Step 4: Follow the seam in `NullValidator`**

`NullValidator<T>` is emitted in the same file. Its body becomes:

```csharp
internal sealed class NullValidator<T> : IValidator<T>
{
    /// <inheritdoc />
    public ValidationOutcome Validate(ValidationContext<T> context) => ValidationOutcome.Valid;
}
```

- [ ] **Step 5: Update the existing rule-kind tests to the new seam**

`CodeGenerator.Tests/App/ValidatorEmitsEveryRuleKindTests.cs` compiles and runs emitted validators. Two helpers change and nothing else:
- `GeneratedFor` is unchanged (it calls `ValidatorTemplate.Generated`), but the assertions that read `errors.Add("...")` now read `new ValidationError("...", "...")`. Update the expected substrings, not the structure of the tests.
- `CompiledValidator` must add `("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme"))`, `("Validation/ValidationPipeline.cs", ValidationPipelineTemplate.Source("Acme"))` and `("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", []))` to its `CompileSources` list, and invoke `Validate` with a context:

```csharp
        var lookupType = assembly.GetType("Acme.Validation.StoreEntityLookup")!;
        var contextType = assembly.GetType("Acme.Validation.ValidationContext`1")!
            .MakeGenericType(entityType);
        var forMethod = contextType.GetMethod("For")!;
        var insert = assembly.GetType("Acme.Validation.ValidationOperation")!
            .GetField("Insert")!.GetValue(null)!;

        return (entityType, item =>
        {
            var context = forMethod.Invoke(null, [item, insert, System.Activator.CreateInstance(lookupType)]);
            var outcome = validateMethod.Invoke(validator, [context])!;
            return (IReadOnlyList<string>)outcome.GetType().GetProperty("Messages")!.GetValue(outcome)!;
        });
```

`PermissionsTemplate` comes from Task 3, which is already done by the time this task runs -- that is why the matrix is ordered ahead of the validator that calls it.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ValidatorStagesTests|FullyQualifiedName~ValidatorEmitsEveryRuleKindTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add AppScaffolding/ValidatorTemplate.cs CodeGenerator.Tests/App/ValidatorStagesTests.cs CodeGenerator.Tests/App/ValidatorEmitsEveryRuleKindTests.cs
git commit -m "Emit only the stages an entity actually has"
```

---

---

---

### Task 5: `CountReferencing`, through the store

**Files:**
- Modify: `AppScaffolding/ValidatorTemplate.cs` (`StoreEntityLookup`)
- Test: `CodeGenerator.Tests/App/EntityLookupCountingTests.cs`

**Interfaces:**
- Consumes: the widened `IEntityLookup` from Task 1. Ordered after the matrix so nothing in this plan depends on a later task.
- Produces: `StoreEntityLookup.CountReferencing<TEntity>(string foreignKeyProperty, int id)`, reading through `StoreFactory.Create<TEntity>()` exactly as `Exists` does.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/EntityLookupCountingTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class EntityLookupCountingTests
{
    [TestMethod]
    public void CountingReadsThroughTheStore_LikeExistsDoes()
    {
        var source = ValidatorTemplate.StoreEntityLookup("Acme");

        StringAssert.Contains(source, "public int CountReferencing<TEntity>(string foreignKeyProperty, int id)");
        StringAssert.Contains(source, "StoreFactory.Create<TEntity>()");
    }

    [TestMethod]
    public void AnUnknownPropertyCountsZero_RatherThanThrowing()
    {
        // This runs on a delete, in a generated app, against a property name that came from
        // ForeignKeys at generation time. If a rename ever leaves the two disagreeing, refusing
        // every delete with a reflection exception is far worse than counting nothing: the
        // check goes quiet and the delete proceeds, which is what it did before this phase.
        var source = ValidatorTemplate.StoreEntityLookup("Acme");

        StringAssert.Contains(source, "if (property is null)");
        StringAssert.Contains(source, "return 0;");
    }

    [TestMethod]
    public void ItCompilesWithTheRestOfTheFolder()
    {
        var result = CodeGenerationEngine.Core.Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/StoreEntityLookup.cs", ValidatorTemplate.StoreEntityLookup("Acme")),
            ("Data/StoreFactory.cs", StoreFactoryTemplate.Source("Acme", PersistenceKind.Json)),
            ("Data/IStore.cs", StoreSeamTemplate.Source("Acme")),
            ("Data/JsonStore.cs", JsonStoreTemplate.Source("Acme.Data")),
        });

        var errors = string.Join("\n", result.Diagnostics
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(e => e.ToString()));

        Assert.IsTrue(result.Success, $"Compilation failed:\n{errors}");
    }
}
```

> Before writing Step 3, read `ValidatorTemplate.StoreEntityLookup`'s existing `Exists` body and mirror its reflection and its `StoreFactory` call exactly. Do not invent a second way of reaching the store in the same file.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter EntityLookupCountingTests`
Expected: FAIL — `CountReferencing` is not emitted.

- [ ] **Step 3: Add `CountReferencing` to the emitted lookup**

Add to the emitted `StoreEntityLookup` class, beside `Exists`:

```csharp
    /// <inheritdoc />
    public int CountReferencing<TEntity>(string foreignKeyProperty, int id)
        where TEntity : class, new()
    {
        var property = typeof(TEntity).GetProperty(
            foreignKeyProperty,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        // A name that is not there counts nothing rather than throwing. The name came from
        // ForeignKeys at generation time; if a rename ever leaves the two disagreeing, a quiet
        // zero leaves the delete behaving as it did before this phase, where a reflection
        // exception would refuse every delete in the app.
        if (property is null)
            return 0;

        var count = 0;

        foreach (var record in StoreFactory.Create<TEntity>().GetAll())
        {
            if (property.GetValue(record) is int value && value == id)
                count++;
        }

        return count;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter EntityLookupCountingTests`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/ValidatorTemplate.cs CodeGenerator.Tests/App/EntityLookupCountingTests.cs
git commit -m "Let the lookup count references, for the delete checks that need it"
```

---

---

---

### Task 6: `SaveResult` attribution, and the front ends that read it

**Files:**
- Modify: `AppScaffolding/SaveResultTemplate.cs`
- Modify: `AppScaffolding/ConsoleMenuTemplate.cs:74`
- Test: `CodeGenerator.Tests/App/SaveResultAttributionTests.cs`

**Interfaces:**
- Consumes: `ValidationError` from Task 1.
- Produces: `SaveResult<T>.Failures` as `IReadOnlyList<ValidationError>`, and `AsDictionary()` keyed by property. Ordered ahead of the store and the repository because both write `Failures` into a result.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/SaveResultAttributionTests.cs`:

```csharp
using AppScaffolding;
using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

[TestClass]
public class SaveResultAttributionTests
{
    private const string Driver = """
using System.Collections.Generic;
using Acme.Data;
using Acme.Validation;

namespace Acme.Probe;

public static class Probe
{
    public static Dictionary<string, string[]> TwoFieldsAndOneWholeRecord()
        => new SaveResult<string>
        {
            Success = false,
            Failures =
            [
                new ValidationError("Surname", "Surname is required."),
                new ValidationError("Surname", "Surname must be 50 characters or fewer."),
                new ValidationError(null, "This record is not permitted."),
            ],
        }.AsDictionary();
}
""";

    [TestMethod]
    public void TheDictionaryIsKeyedByProperty_WithWholeRecordErrorsUnderTheEmptyKey()
    {
        // The standard ASP.NET 400 payload. Keeping every message under one empty key, as an
        // earlier draft of the spec did, would leave the Web API unable to say which field was
        // rejected - which is most of what attribution is for.
        var probe = Compile();

        var dictionary = (System.Collections.IDictionary)probe.GetType("Acme.Probe.Probe")!
            .GetMethod("TwoFieldsAndOneWholeRecord")!.Invoke(null, null)!;

        Assert.HasCount(2, dictionary);
        Assert.HasCount(2, (System.Array)dictionary["Surname"]!);
        Assert.HasCount(1, (System.Array)dictionary[string.Empty]!);
    }

    [TestMethod]
    public void ErrorsStaysAListOfSentences()
    {
        // Compatibility for everything that only prints them - the console menu, the desktop
        // status line, the generated tests.
        var source = SaveResultTemplate.Source("Acme");

        StringAssert.Contains(source, "IReadOnlyList<string> Errors");
    }

    private static System.Reflection.Assembly Compile()
    {
        var result = Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Data/SaveResult.cs", SaveResultTemplate.Source("Acme")),
            ("Probe/Probe.cs", Driver),
        });

        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(e => e.ToString())));

        return System.Reflection.Assembly.Load(result.AssemblyBytes!);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter SaveResultAttributionTests`
Expected: FAIL — there is no `Failures` and `AsDictionary` uses one empty key.

- [ ] **Step 3: Add `Failures` and rekey `AsDictionary`**

In `AppScaffolding/SaveResultTemplate.cs`, add `using {{rootNamespace}}.Validation;`, then:

```csharp
    /// <summary>Every broken rule with the property it belongs to. Empty when Success is true.</summary>
    /// <remarks>
    /// Alongside <see cref="Errors"/> rather than replacing it: a front end that only prints
    /// sentences should not have to unpack a record to do so.
    /// </remarks>
    public IReadOnlyList<ValidationError> Failures { get; init; } = Array.Empty<ValidationError>();

    /// <summary>The failures keyed by property, with whole-record errors under the empty key —
    /// the shape Results.ValidationProblem wants.</summary>
    public Dictionary<string, string[]> AsDictionary()
    {
        var byProperty = new Dictionary<string, List<string>>();

        foreach (var failure in Failures)
        {
            var key = failure.Property ?? string.Empty;

            if (!byProperty.TryGetValue(key, out var messages))
                byProperty[key] = messages = new List<string>();

            messages.Add(failure.Message);
        }

        // A result carrying only sentences - a store failure, say - still has to surface. Those
        // belong to no field, so they go under the empty key.
        if (byProperty.Count == 0 && Errors is { Count: > 0 })
            byProperty[string.Empty] = new List<string>(Errors);

        var result = new Dictionary<string, string[]>();

        foreach (var pair in byProperty)
            result[pair.Key] = pair.Value.ToArray();

        return result;
    }
```

- [ ] **Step 4: Name the property in the console menu**

`AppScaffolding/ConsoleMenuTemplate.cs:74` prints `result.Errors`. It becomes:

```csharp
            foreach (var failure in result.Failures)
            {
                System.Console.WriteLine(failure.Property is null
                    ? "  " + failure.Message
                    : $"  {failure.Property}: {failure.Message}");
            }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter SaveResultAttributionTests`
Expected: PASS, 2 tests.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/SaveResultTemplate.cs AppScaffolding/ConsoleMenuTemplate.cs CodeGenerator.Tests/App/SaveResultAttributionTests.cs
git commit -m "Attribute a failure to its field, and key the API payload by it"
```

---

---

---

### Task 7: `SaveAll` carries a refusal, and the stores ask the matrix

**Files:**
- Modify: `AppScaffolding/StoreSeamTemplate.cs`
- Modify: `AppScaffolding/JsonStoreTemplate.cs:45-56`
- Modify: `AppScaffolding/EfStoreTemplate.cs:31`
- Modify: every `SaveAll` call site: `AppScaffolding/ViewModelTemplates.cs:277`, `AppScaffolding/Screens/BlazorScreenTemplates.cs:748,873,890`, `AppScaffolding/Screens/LineSetTemplate.cs:188`, `AppScaffolding/Screens/MasterScreenTemplate.cs:353`, `AppScaffolding/RepositoryTemplate.cs`
- Test: `CodeGenerator.Tests/App/StoreWriteGuardTests.cs`

**Interfaces:**
- Produces: `IStore<T>.SaveAll(List<T> all)` returning `StoreWrite` — `public sealed record StoreWrite(bool Success, string? Refusal)` with `static StoreWrite Ok` — emitted in `Data/IStore.cs` beside the interface.
- Consumes: `Permissions.Allows` from Task 3.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/StoreWriteGuardTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class StoreWriteGuardTests
{
    [TestMethod]
    public void SaveAllCarriesARefusal_RatherThanABareBool()
    {
        // A bool made a permission refusal surface as "Could not write Customer.json", which is
        // a lie: nothing was wrong with the file.
        var source = StoreSeamTemplate.Source("Acme");

        StringAssert.Contains(source, "StoreWrite SaveAll(List<T> all)");
        StringAssert.Contains(source, "record StoreWrite(bool Success, string? Refusal)");
    }

    [TestMethod]
    public void TheStoreAsksTheMatrixBeforeItWrites()
    {
        // The unbypassable point. Coarse - it can only ask "may this user write this type at
        // all", because SaveAll is handed a whole list and cannot tell an insert from a delete -
        // but no front end can go round it.
        // JsonStoreTemplate.Source takes the FULL namespace, not the root: every caller
        // passes $"{rootNamespace}.Data".
        var source = JsonStoreTemplate.Source("Acme.Data");

        StringAssert.Contains(source, "Permissions.Allows");
    }

    [TestMethod]
    public void TheGuardIsHonestAboutWhatItCannotSee()
    {
        // Under the default matrix this refuses nothing except the User entity, because a User
        // may write every business entity and is barred only from DELETING a master. Recorded in
        // the emitted comment so the next reader does not assume it is the whole enforcement.
        var source = JsonStoreTemplate.Source("Acme.Data");

        StringAssert.Contains(source, "cannot tell an insert from a delete");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter StoreWriteGuardTests`
Expected: FAIL — `SaveAll` returns `bool` and no store mentions `Permissions`.

- [ ] **Step 3: Widen the seam**

In `AppScaffolding/StoreSeamTemplate.cs`, add the record and change the method:

```csharp
/// <summary>The outcome of a whole-store write.</summary>
/// <remarks>
/// A bool could not distinguish "the file would not write" from "this user may not write this
/// type", and the second surfaced as the first — a message naming a file that was perfectly fine.
/// </remarks>
public sealed record StoreWrite(bool Success, string? Refusal)
{
    /// <summary>A write that went through.</summary>
    public static StoreWrite Ok { get; } = new(true, null);
}

public interface IStore<T> where T : new()
{
    /// <summary>Every stored record.</summary>
    List<T> GetAll();

    /// <summary>Overwrites the store with <paramref name="all"/>.</summary>
    StoreWrite SaveAll(List<T> all);

    /// <summary>One past the highest Id in <paramref name="existing"/>, or 1 when there are none.</summary>
    int NextId(IEnumerable<T> existing);

    /// <summary>A new record with its Id already assigned.</summary>
    T NewItem(IEnumerable<T> existing);
}
```

- [ ] **Step 4: Guard both stores**

In `AppScaffolding/JsonStoreTemplate.cs`, `SaveAll` becomes:

```csharp
    /// <inheritdoc />
    public StoreWrite SaveAll(List<T> all)
    {
        // The coarse enforcement point, and the only one no front end can go round. It can ask
        // only whether this user may write this type at all: SaveAll is handed a whole list and
        // cannot tell an insert from a delete, so the operation-aware checks live in Repository
        // and at the grids' delete buttons. Under the default matrix this refuses nothing except
        // the app's own User records.
        if (!Permissions.Allows(ValidationOperation.Update, typeof(T).Name, CurrentRole()))
            return new StoreWrite(false, Permissions.Refusal(ValidationOperation.Update, typeof(T).Name));

        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(all, Options));
            return StoreWrite.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StoreWrite(false, null);
        }
    }
```

Define `CurrentRole()` HERE — this is the first task that needs it. Add the pair below to both store templates, give each template a `bool auth` parameter defaulted through its existing overload chain so no current caller breaks, and add `using {{rootNamespace}}.Validation;` to both:

```csharp
        var currentUser = auth
            ? $$"""
                private static string? CurrentRole() => {{rootNamespace}}.Auth.Session.Current?.Role.ToString();

            """
            : """
                // No login in this app, so there is nobody to attribute a write to. Not a
                // stub to fill in later: a target without screens emits no auth core.
                private static string? CurrentRole() => null;

            """;
```

Task 8 adds the same pair, plus `CurrentUserId()`, to `Repository<T>`. They are duplicated rather than shared because the store and the repository are emitted into different folders and neither may depend on the other.

Apply the same change to `AppScaffolding/EfStoreTemplate.cs:31`, keeping its existing `try`/`catch` exactly as it is.

- [ ] **Step 5: Update every `SaveAll` call site**

Each of the seven call sites currently treats the result as a bool. Each becomes, adapted to its local status variable:

```csharp
        var write = _store.SaveAll(all);

        Status = write.Success
            ? $"Saved {Records.Count} {Title} record(s)."
            : write.Refusal ?? $"Could not write {typeof(T).Name}.json.";
```

The `?? ` fallback matters: a genuine IO failure has no refusal, and must still say what it always said.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter StoreWriteGuardTests`
Expected: PASS, 3 tests.

Then the whole project; every emitted-source golden test touching a store or a screen will need its expectation updated.

- [ ] **Step 7: Commit**

```bash
git add AppScaffolding/StoreSeamTemplate.cs AppScaffolding/JsonStoreTemplate.cs AppScaffolding/EfStoreTemplate.cs AppScaffolding/ViewModelTemplates.cs AppScaffolding/Screens/ AppScaffolding/RepositoryTemplate.cs CodeGenerator.Tests/
git commit -m "Guard the store boundary, and let it say why it refused"
```

---

---

---

### Task 8: `Repository<T>` runs the pipeline, and a delete can be refused

**Files:**
- Modify: `AppScaffolding/RepositoryTemplate.cs:55-215`
- Modify: `AppScaffolding/ConsoleMenuTemplate.cs:86`
- Modify: `AppScaffolding/WebApiProgramTemplate.cs:83`
- Modify: `AppScaffolding/TestProjectTemplates.cs:47`
- Test: `CodeGenerator.Tests/App/RepositoryPipelineTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-7: the seam and the outcome from Task 1, the pipeline from Task 2, the matrix from Task 3, `SaveResult.Failures` from Task 6 and `StoreWrite` from Task 7.
- Produces: `Repository<T>.Create(T item)` and `Update(T item)` unchanged in signature, each building an `Insert`/`Update` context with `Stored` filled from the store on update. **`Delete(int id)` changes return type from `bool` to `SaveResult<T>`** — a refusal has to carry its reason, and a bool cannot.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/RepositoryPipelineTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class RepositoryPipelineTests
{
    [TestMethod]
    public void UpdatePassesTheStoredVersion_NotJustTheSubmittedOne()
    {
        // Stored is what makes an immutable-field rule and a status transition expressible at
        // all. An Update that passed null would leave both stages unable to tell what changed.
        var source = RepositoryTemplate.Base("Acme", validation: true);

        StringAssert.Contains(source, "ValidationOperation.Update");
        StringAssert.Contains(source, "Stored:");
    }

    [TestMethod]
    public void DeleteRunsThePipeline_AndReturnsItsReason()
    {
        // Delete is a new call site: nothing validated a delete before this phase. It returns a
        // SaveResult rather than a bool because a refusal that cannot say why is indistinguishable
        // from "no such record", and the console and Web API surface those differently.
        var source = RepositoryTemplate.Base("Acme", validation: true);

        StringAssert.Contains(source, "public SaveResult<T> Delete(int id)");
        StringAssert.Contains(source, "ValidationOperation.Delete");
    }

    [TestMethod]
    public void DeleteWithNoSuchRecord_IsNotFound_NotARefusal()
    {
        var source = RepositoryTemplate.Base("Acme", validation: true);

        StringAssert.Contains(source, "NotFound = true");
    }

    [TestMethod]
    public void WithValidationOff_DeleteStillReturnsASaveResult()
    {
        // The flag changes which checks run, never the shape of the API. A Delete that returned
        // bool under novalidation and SaveResult otherwise would make every caller conditional.
        var source = RepositoryTemplate.Base("Acme", validation: false);

        StringAssert.Contains(source, "public SaveResult<T> Delete(int id)");
        Assert.DoesNotContain("ValidationPipeline", source);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter RepositoryPipelineTests`
Expected: FAIL — `Delete` returns `bool` and no context is built anywhere.

- [ ] **Step 3: Build a context at each of the three call sites**

In `AppScaffolding/RepositoryTemplate.cs`, the `validateCall` fragments become operation-specific. Replace the single `validateCall` with `validateCreate`, `validateUpdate` and `validateDelete`, each following the existing fragment's raw-literal indentation and trailing blank line exactly — a raw literal carries the line endings of the source file it is written in, and these fragments are lines lifted out of the template below them.

`validateCreate`:

```csharp
                    var outcome = _validator.Validate(new ValidationContext<T>(
                        item, ValidationOperation.Insert, null, new StoreEntityLookup(),
                        CurrentRole(), CurrentUserId()));

                    if (!outcome.IsValid)
                        return new SaveResult<T> { Success = false, Failures = outcome.Errors, Errors = outcome.Messages };
```

`validateUpdate` reads the stored version first, because that is the whole point of the field:

```csharp
                    var stored = GetById(GetId(item));

                    var outcome = _validator.Validate(new ValidationContext<T>(
                        item, ValidationOperation.Update, stored, new StoreEntityLookup(),
                        CurrentRole(), CurrentUserId()));

                    if (!outcome.IsValid)
                        return new SaveResult<T> { Success = false, Failures = outcome.Errors, Errors = outcome.Messages };
```

`validateDelete` validates the record that is about to go, not a submitted one:

```csharp
                    var doomed = GetById(id);

                    if (doomed is null)
                        return new SaveResult<T> { Success = false, NotFound = true };

                    var outcome = _validator.Validate(new ValidationContext<T>(
                        doomed, ValidationOperation.Delete, doomed, new StoreEntityLookup(),
                        CurrentRole(), CurrentUserId()));

                    if (!outcome.IsValid)
                        return new SaveResult<T> { Success = false, Failures = outcome.Errors, Errors = outcome.Messages };
```

- [ ] **Step 4: Supply the role without depending on the auth core**

`Repository<T>` is emitted for every target, including the four with no `Session`. The same auth-conditional pair Task 7 added to the two stores, plus `CurrentUserId()`. `RepositoryTemplate.Base` gains a `bool auth` parameter for this, defaulted through the existing overload chain so no current caller breaks:

```csharp
        var currentUser = auth
            ? $$"""
                private static string? CurrentRole() => {{appName}}.Auth.Session.Current?.Role.ToString();

                private static int? CurrentUserId() => {{appName}}.Auth.Session.Current?.Id;

            """
            : """
                // This app has no login, so there is nobody to attribute a write to and the
                // Security stage has nothing to refuse. Not a stub to fill in later: a target
                // without screens emits no auth core at all.
                private static string? CurrentRole() => null;

                private static int? CurrentUserId() => null;

            """;
```

- [ ] **Step 5: Rewrite `Delete` to return a `SaveResult<T>`**

```csharp
    /// <summary>{{deleteSummary}}</summary>
    public SaveResult<T> Delete(int id)
    {
{{validateDelete}}        var all = _store.GetAll();

        if (all.RemoveAll(item => GetId(item) == id) == 0)
            return new SaveResult<T> { Success = false, NotFound = true };

{{deleteTail}}
    }
```

`deleteTail` becomes, with logging:

```csharp
                var saved = _store.SaveAll(all);

                if (saved.Success)
                    Log.LogInformation("Deleted {Entity} {Id}.", typeof(T).Name, id);

                return saved.Success
                    ? new SaveResult<T> { Success = true }
                    : new SaveResult<T> { Success = false, Errors = new[] { saved.Refusal ?? "Could not persist " + typeof(T).Name + "." } };
```

and without:

```csharp
                var saved = _store.SaveAll(all);

                return saved.Success
                    ? new SaveResult<T> { Success = true }
                    : new SaveResult<T> { Success = false, Errors = new[] { saved.Refusal ?? "Could not persist " + typeof(T).Name + "." } };
```

`saved.Success` and `saved.Refusal` are the `StoreWrite` record from Task 7, which is already done by the time this task runs.

- [ ] **Step 6: Update the three `Delete` callers**

`AppScaffolding/ConsoleMenuTemplate.cs:86` — today `if (int.TryParse(..., out var id) && _repository.Delete(id))`. It becomes:

```csharp
        if (!int.TryParse(System.Console.ReadLine(), out var id))
        {
            System.Console.WriteLine("That is not a number.");
            return;
        }

        var result = _repository.Delete(id);

        if (result.Success)
        {
            System.Console.WriteLine("Deleted.");
            return;
        }

        if (result.NotFound)
        {
            System.Console.WriteLine("No record with that Id.");
            return;
        }

        foreach (var error in result.Errors)
            System.Console.WriteLine("  " + error);
```

`AppScaffolding/WebApiProgramTemplate.cs:83` — today `repository.Delete(id) ? Results.NoContent() : Results.NotFound()`. It becomes:

```csharp
        {
            var result = repository.Delete(id);

            return result.Success ? Results.NoContent()
                : result.NotFound ? Results.NotFound()
                : Results.ValidationProblem(result.AsDictionary());
        });
```

`AppScaffolding/TestProjectTemplates.cs:47` — `Assert.True(repository.Delete(id))` becomes `Assert.True(repository.Delete(id).Success)`.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~RepositoryPipelineTests"`
Expected: PASS, 4 tests.

Then run the whole project and read every failure: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj`. Existing tests asserting `Delete` returns `bool`, or asserting the old `Validate` shape, are expected and must be updated to the new API rather than deleted.

- [ ] **Step 8: Commit**

```bash
git add AppScaffolding/RepositoryTemplate.cs AppScaffolding/ConsoleMenuTemplate.cs AppScaffolding/WebApiProgramTemplate.cs AppScaffolding/TestProjectTemplates.cs CodeGenerator.Tests/
git commit -m "Run the pipeline on every write, and let a delete be refused"
```

---

---

---

### Task 9: The grids' delete buttons, where the deferred save cannot help

**Files:**
- Modify: `AppScaffolding/Screens/MasterScreenTemplate.cs:347`
- Modify: `AppScaffolding/ViewModelTemplates.cs:265-273`
- Modify: `AppScaffolding/Screens/BlazorScreenTemplates.cs:870,887`
- Test: `CodeGenerator.Tests/App/GridDeletePermissionTests.cs`

**Interfaces:**
- Consumes: `Permissions.Allows` from Task 3.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/GridDeletePermissionTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class GridDeletePermissionTests
{
    [TestMethod]
    public void TheMasterScreenAsksTheMatrix_NotSessionIsAdmin()
    {
        // Same refusal as before for the default matrix, but routed through the one place that
        // decides it. The hardcoded check could not be changed by a matrix and could not agree
        // with the store's.
        var source = MasterScreenTemplate.Source("Acme", login: true);

        StringAssert.Contains(source, "Permissions.Allows");
        Assert.DoesNotContain("Session.IsAdmin", source);
    }

    [TestMethod]
    public void ThePlainGridDelete_GainsItsFirstCheck()
    {
        // ViewModelTemplates.Delete() had NO check of any kind: it removed the row from an
        // ObservableCollection and said "Not saved yet". The later SaveAll cannot tell that a
        // delete happened, so this is the only point where it can be refused.
        var source = ViewModelTemplates.EntityViewModel("Acme", validation: true, login: true);

        StringAssert.Contains(source, "Permissions.Allows(ValidationOperation.Delete");
    }

    [TestMethod]
    public void WithNoLogin_TheGridDoesNotNameSession()
    {
        // A target with screens but nologin emits no auth core, so naming Session would not
        // compile.
        var source = ViewModelTemplates.EntityViewModel("Acme", validation: true, login: false);

        Assert.DoesNotContain("Session.", source);
    }
}
```

> **Verified signatures.** Today these are `MasterScreenTemplate.Source(string rootNamespace)` and `ViewModelTemplates.EntityViewModel(string rootNamespace, bool validation)` -- neither takes a `login` flag, and both need one, because naming `Session` in an app generated with `nologin` would not compile. Thread it exactly as `validation` is already threaded: `login` is ALREADY in scope at both call sites, `AppScaffolding/Targets/DesktopViewModelFiles.cs:39` and `:65`, as the `bool login = false` parameter of `Build`. Add the parameter with the same `= false` default so no other caller breaks.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter GridDeletePermissionTests`
Expected: FAIL — `Session.IsAdmin` is still hardcoded and the plain grid has no check.

- [ ] **Step 3: Replace the hardcoded check**

In `AppScaffolding/Screens/MasterScreenTemplate.cs`, the block at 347 becomes:

```csharp
        if (!Permissions.Allows(ValidationOperation.Delete, typeof(T).Name, CurrentRole()))
        {
            Status = Permissions.Refusal(ValidationOperation.Delete, typeof(T).Name);
            return;
        }
```

- [ ] **Step 4: Give the plain grid its first check**

In `AppScaffolding/ViewModelTemplates.cs`, `Delete()` becomes:

```csharp
    private void Delete()
    {
        if (_selected is not T item)
            return;

        // Checked HERE, not at Save: this screen defers every change to one SaveAll, and a
        // whole-list write cannot tell that a record was removed. There is nowhere later to ask.
        if (!Permissions.Allows(ValidationOperation.Delete, typeof(T).Name, CurrentRole()))
        {
            Status = Permissions.Refusal(ValidationOperation.Delete, typeof(T).Name);
            return;
        }

        Records.Remove(item);
        Selected = null;
        Status = $"Removed a {Title}. Not saved yet.";
    }
```

- [ ] **Step 5: The same at the two Blazor delete sites**

`AppScaffolding/Screens/BlazorScreenTemplates.cs:870` and `:887` each read the list, remove and save. Add the same guard above each read, setting `_status` from `Permissions.Refusal`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter GridDeletePermissionTests`
Expected: PASS, 3 tests.

- [ ] **Step 7: Commit**

```bash
git add AppScaffolding/Screens/ AppScaffolding/ViewModelTemplates.cs CodeGenerator.Tests/App/GridDeletePermissionTests.cs
git commit -m "Refuse a delete at the grid, the only point a deferred save can be checked"
```

---

---

---

### Task 10: The editors highlight the field that failed

**Files:**
- Modify: `AppScaffolding/Screens/RecordEditorTemplate.cs:49`
- Modify: `AppScaffolding/Screens/BlazorScreenTemplates.cs:726,905`
- Test: `CodeGenerator.Tests/App/FieldHighlightTests.cs`

**Interfaces:**
- Consumes: `ValidationOutcome.Errors` from Task 1.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/FieldHighlightTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class FieldHighlightTests
{
    [TestMethod]
    public void TheBlazorEditorMarksTheFailingInput()
    {
        // Attribution is only worth carrying if something shows it. The class name is asserted
        // because 27 CSS class names in this app are already pinned by bUnit tests: a restyle
        // changes the stylesheet, never the name.
        var source = BlazorScreenTemplates.MasterScreen("Acme", login: true);

        StringAssert.Contains(source, "field-invalid");
    }

    [TestMethod]
    public void TheDesktopEditorNamesTheFailingProperty()
    {
        var source = RecordEditorTemplate.Source("Acme", validation: true);

        StringAssert.Contains(source, "Property");
    }
}
```

> **Verified signatures.** `BlazorScreenTemplates.MasterScreen(string ns, bool login)` is called that way at `AppScaffolding/Targets/BlazorTarget.cs:90`, and `RecordEditorTemplate.Source(string rootNamespace, bool validation)` at `AppScaffolding/Targets/DesktopViewModelFiles.cs:63`. A CSS class added here needs its rule adding to whichever stylesheet that page already links — do not introduce a second stylesheet, and do not rename an existing class: 27 class names in this app are asserted by bUnit tests.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FieldHighlightTests`
Expected: FAIL — neither editor knows which property failed.

- [ ] **Step 3: Keep the failing properties and mark them**

In the Blazor screen, replace the `_status`-only handling with a set of failing property names plus the status line:

```csharp
    private HashSet<string> _invalid = new();

    private string CssFor(string property) => _invalid.Contains(property) ? "field-invalid" : string.Empty;
```

populated from the outcome when a save is refused, and cleared at the start of every save attempt. Apply `CssFor` to each input's `class` attribute.

In `RecordEditorTemplate`, keep the outcome's errors and show the first message against each named property rather than only the first sentence at the top.

- [ ] **Step 4: Add the stylesheet rule**

Add to the page's existing stylesheet:

```css
.field-invalid {
    border-color: #b3261e;
    outline: 2px solid rgba(179, 38, 30, 0.25);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FieldHighlightTests`
Expected: PASS, 2 tests.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/Screens/ CodeGenerator.Tests/App/FieldHighlightTests.cs
git commit -m "Mark the field that failed, not just the sentence"
```

---

---

---

### Task 11: The class path: `generate validator` has no store, no app and no `Session`

`generate validator` and `create class` emit a validator for a class on disk with no generated app around it. The seam change in Task 1 breaks that path, and it carries its own compile trap: the two partial halves must be compiled TOGETHER, because a concatenation hides CS0260 and has done before on this exact path.

**Files:**
- Modify: whichever writer the `generate validator` command uses — find it from `CodeGenerator/` rather than guessing; `ClassValidationWriter.EnsurePartial` is the entry point the spec names
- Test: `CodeGenerator.Tests/App/StandaloneValidatorSeamTests.cs`

**Interfaces:**
- Consumes: the seam from Task 1, the pipeline from Task 2, the matrix from Task 3.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/StandaloneValidatorSeamTests.cs`:

```csharp
using AppScaffolding;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

[TestClass]
public class StandaloneValidatorSeamTests
{
    [TestMethod]
    public void BothPartialHalvesCompileTogether_UnderTheNewSeam()
    {
        // CompileSources, never a concatenation: a concatenation hides CS0260, and this exact
        // path shipped an uncompilable pair for months behind tests that concatenated.
        var definition = new ClassDefinition { Name = "Customer" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        var surname = new PropertyDefinition { Name = "Surname", Type = "string" };
        surname.Rules.Add(new PropertyRule { Name = "required" });
        definition.Properties.Add(surname);

        var models = """
namespace Acme.Models;

public partial class Customer
{
    public int Id { get; set; }
    public string Surname { get; set; } = string.Empty;
}
""";

        var result = Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/ValidationPipeline.cs", ValidationPipelineTemplate.Source("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", ["Customer"])),
            ("Models/Customer.cs", models),
            ("Validation/CustomerValidator.Generated.cs",
                ValidatorTemplate.Generated("Acme", definition, [])),
            ("Validation/CustomerValidator.Custom.cs",
                ValidatorTemplate.CustomStub("Acme", definition.Name)),
        });

        var errors = string.Join("\n", result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(e => e.ToString()));

        Assert.IsTrue(result.Success, $"Compilation failed:\n{errors}");
    }

    [TestMethod]
    public void AStandaloneValidatorCanBeGivenALookup()
    {
        // There is no app here: no StoreFactory, no Session, no seeded data. The parameterless
        // constructor reaches for StoreEntityLookup, which reaches for StoreFactory — so this
        // path has to be able to supply a lookup rather than depend on one existing.
        var definition = new ClassDefinition { Name = "Customer" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        var source = ValidatorTemplate.Generated("Acme", definition, []);

        StringAssert.Contains(source, "public CustomerValidator(IEntityLookup lookup)");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter StandaloneValidatorSeamTests`
Expected: FAIL — the standalone path emits none of the four new Validation files, so the pair does not compile.

- [ ] **Step 3: Emit what the standalone path now needs**

The `generate validator` writer must emit `ValidationContext.cs`, `ValidationPipeline.cs` and `Permissions.cs` alongside the validator pair, exactly as the app path does — the emitted validator names all three. Read the existing writer first and follow its own file-writing pattern; do not introduce a second way of writing a generated file into a user's project.

Uniqueness and reference counting genuinely cannot run here, because there is no store to count through. The `Validate(IEnumerable<T> existing)` overload the spec describes belongs with the phase-3 rules that need it, so it is NOT part of this task: this task only keeps the path compiling under the new seam.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter StandaloneValidatorSeamTests`
Expected: PASS, 2 tests.

- [ ] **Step 5: Run every test that touches the standalone path**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~GenerateValidator|FullyQualifiedName~ClassValidation"`
Expected: PASS. These are the tests that caught the uncompilable CS0260 pair; they must stay green.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/ CodeGenerator.Tests/App/StandaloneValidatorSeamTests.cs
git commit -m "Keep generate validator compiling under the widened seam"
```

---

---

### Task 12: `novalidation` stops meaning "no validation code", in both documents

**Files:**
- Modify: `CodeGenerationEngine/Core/CommandDictionary.cs:692-701`
- Modify: `docs/commands.md` (regenerated)
- Modify: the manual's chapter-18 appendix (regenerated)
- Modify: `docs/superpowers/specs/2026-09-06-validation-architecture-design.md` (the false behaviour-change bullet)
- Test: existing `ManualAppendixDocTests` and the commands-doc test must pass unchanged

**Interfaces:**
- Consumes: nothing. This is the documentation half of Task 3's split.

- [ ] **Step 1: Reword the dictionary entry**

In `CodeGenerationEngine/Core/CommandDictionary.cs`, the `novalidation` description currently reads *"'novalidation' omits the Validation folder and its per-model validators"*. It becomes:

```
"'novalidation' omits the per-model validators and the field, entity and business checks; the permission matrix is always generated, because turning validation off must not turn permissions off"
```

Keep the whole entry inside whatever line-length the surrounding entries observe, and keep the `Example` unchanged.

- [ ] **Step 2: Regenerate both artefacts**

`docs/commands.md` and chapter 18's appendix are both generated from `CommandDictionary`. Find the generator (the test that guards them names it) and run it, so both files change together. Changing one by hand turns `ManualAppendixDocTests` red.

- [ ] **Step 3: Correct the spec's false bullet**

In `docs/superpowers/specs/2026-09-06-validation-architecture-design.md`, the behaviour-change bullet reading *"Permission checks are enforced at the store, so a Web API or console caller that previously deleted a master record freely is now refused"* is not true and cannot be made true in this phase. Replace it with:

```
- Permission checks are enforced rather than displayed, at all three points. This reaches
  the WPF, WinForms and Blazor targets, which are the only ones that HAVE a user:
  `ScreenPlan.For` emits the auth core only for a target with screens, so a console,
  webapi or worker app has no role to check and its Security stage is genuinely empty.
  Giving those targets their own authentication is a feature, not part of this phase.
```

- [ ] **Step 4: Run the documentation tests**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ManualAppendix|FullyQualifiedName~CommandsDoc"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/CommandDictionary.cs docs/
git commit -m "Say what novalidation now leaves behind, in both generated documents"
```

---

---

---

### Task 13: Generate the apps and use them

Four thousand passing tests did not find the defects on the last three branches; running the generated app did, every time. This task is not optional and its findings are not "follow-ups".

**Files:**
- Test: `CodeGenerator.Tests/App/PipelineEndToEndTests.cs`
- No production files unless the run finds a defect.

- [ ] **Step 1: Write the end-to-end test**

Create `CodeGenerator.Tests/App/PipelineEndToEndTests.cs`, scaffolding a console app from a two-entity model and asserting on the emitted files:

```csharp
using System.IO;
using AppScaffolding;
using AppScaffolding.Targets;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class PipelineEndToEndTests
{
    [TestMethod]
    public void AThreeEntityAppEmitsNoEmptyStages()
    {
        // The mitigation for "seven stages is more machinery than a three-entity CRUD app
        // needs", measured on a real scaffold rather than on one template's output.
        var folder = Scaffold();

        try
        {
            var validator = File.ReadAllText(
                Path.Combine(folder, "Validation", "CustomerValidator.Generated.cs"));

            Assert.DoesNotContain("private static void Workflow(", validator);
            Assert.DoesNotContain("private static void Transaction(", validator);
            StringAssert.Contains(validator, "private static void Security(");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void AConsoleAppHasNoRole_AndStillGenerates()
    {
        // The limit this phase cannot lift, pinned so nobody later "fixes" CurrentRole into
        // something that names a Session the console target never emits.
        var folder = Scaffold();

        try
        {
            var repository = File.ReadAllText(Path.Combine(folder, "Data", "Repository.cs"));

            StringAssert.Contains(repository, "private static string? CurrentRole() => null;");
            Assert.DoesNotContain("Session.Current", repository);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string Scaffold()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Customer",
            Fields = { new CandidateField { Name = "Surname", Type = "string" } }
        });
        model.Entities.Add(new CandidateEntity
        {
            Name = "Order",
            Fields = { new CandidateField { Name = "CustomerId", Type = "int" } }
        });

        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);

        var result = new AppScaffolder().Scaffold(
            ScaffoldRequest.For(model, "Shop", workspace, target));

        Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

        return result.ProjectPath!;
    }
}
```

- [ ] **Step 2: Run it**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter PipelineEndToEndTests`
Expected: PASS, 2 tests.

- [ ] **Step 3: Rebuild the CLI before touching it by hand**

Run: `dotnet build CodeGenerator/CodeGenerator.csproj -c Release`

- [ ] **Step 4: Generate a WPF app with a login and use it as a non-admin**

`build app: a shop` with the WPF target and the login left on. Then, in the running app:
- sign in as the seeded **User**, not the Admin;
- select a master record and press Delete. It must refuse with the matrix's sentence.
- add a record with a blank required field and Save. It must refuse, and the failing field must be marked, not merely named in the status line.
- sign out, sign in as **Admin**, delete the same record. It must succeed.

- [ ] **Step 5: Generate a console app and prove it still works with no user at all**

`build app: a shop from sql "..."` with the console target. Add, list and delete a record. Every one must succeed: a console app has no role, and a matrix that refused a null role would have bricked it. This is the single most likely way to break an existing generated app in this phase.

- [ ] **Step 6: Generate a Web API and read the 400 payload**

Launch the webapi target, POST an invalid record, and read the response body. The payload must key the messages by property name, not put them all under one empty key.

- [ ] **Step 7: Generate with `novalidation` and read the folder**

`build app: a shop novalidation`. `Validation/Permissions.cs` and `Validation/ValidationContext.cs` must be present; `ValidationPipeline.cs`, `IValidator.cs` and the per-model validators must not. Then launch it and confirm it still runs.

- [ ] **Step 8: Record anything the runs found**

Any defect found here gets a test that reproduces it, then a fix, then a commit — in that order. A defect found by running and fixed without a test will come back.

- [ ] **Step 9: Full suite in Release, exactly as CI runs it**

```bash
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
```

Read the BUILD result first. A failed build followed by `--no-build` tests reports green over the previous binaries — that happened in phase 1. Then read the test counts; a per-test `  Failed <name>` line does not match a bare `Failed!`.

`Run_ChildFloodsBothPipes_DoesNotDeadlockAndCapturesBoth` is a known CPU-starvation flake: it reddens under full-suite load and passes in isolation in under a second. Re-run it alone before treating it as a regression.

- [ ] **Step 10: Commit**

```bash
git add CodeGenerator.Tests/App/PipelineEndToEndTests.cs
git commit -m "Prove the pipeline reaches a running app, in a target with a user and one without"
```

---

---

---

## Deliberately not in Phase 2

- **`CountReferencing` has no caller yet.** It is emitted and tested here, and the referential-integrity check that uses it arrives with the Entity stage in phase 3. Kept in this phase on the explicit instruction that a generated app's `IEntityLookup` should not have to widen twice.
- **`ValidationOperation.Post` has no caller yet**, for the same reason: the posting gate is phase 6.
- **Every stage but Security and Field is empty.** Entity, Business, Transaction, Workflow and Database emit nothing at all until phases 3 to 6 give them rules. The stage list and the short-circuit exist now so that adding a stage later is a rule change rather than an architecture change.
- **Rules moving onto the model's own `Validate()` partial**, and the `partial` keyword on generated models. The spec puts record-answerable rules there so the 18 existing rule kinds work untouched; that convergence belongs with phase 3, where the inference and the catalog actually meet. Phase 2 leaves every rule in the validator, where phase 1 put it.
- **Company and Branch scoping** deliver nothing until an app has both such an entity and a way to say who belongs where, as the spec already records.
