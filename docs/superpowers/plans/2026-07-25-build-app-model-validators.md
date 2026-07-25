# Per-Model Validation Classes in `build app:` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `build app:` emits a `Validation/` folder with one validator class per model, enforced centrally so every generated target validates before writing.

**Architecture:** A new `AppScaffolding/ValidatorTemplate.cs` emits `IValidator<T>`, an `IEntityLookup` FK seam, a `ValidatorFactory` that resolves validators by type (mirroring the existing `StoreFactory`), and per-model validators split into a regenerated `.Generated.cs` partial plus a write-once `.Custom.cs` hook. Rules are read from the `PropertyDefinition.Rules`/`.Attributes` that `DomainModelMapper.AddValidation` already populates, so model attributes and validator code cannot drift. `Repository<T>.Create`/`Update` enforce and return `SaveResult<T>`.

**Tech Stack:** C# / .NET 10, Roslyn (`CodeGenerationEngine.Core.Compiler`) for compile-testing emitted code, MSTest for the generator's own tests, xUnit for the code the generator emits into `<App>.Tests`.

**Spec:** `docs/superpowers/specs/2026-07-25-build-app-model-validation-design.md`

## Global Constraints

- Verification gate is the **full solution in Release with `-warnaserror`**: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror` then `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release`. A green `CodeGenerator` Debug build is **not** evidence — it hides analyzer errors and runs only one of the three test projects.
- Run commands from the repo root `D:\SmartCodeGenerator-Repository` (the git root is the **parent** of the `SmartCodeGenerator` tool folder). There are **four** `.slnx` files; CI's is `SmartCodeGenerator/SmartCodeGenerator.slnx`, not one at the repo root.
- Emitted C# must be verified by **compiling it** with `CodeGenerationEngine.Core.Compiler.Compile`, never by substring or `ParseText` assertions. Roslyn error-recovery makes malformed emitted code pass syntax-tree assertions.
- Generator-side tests are **MSTest** (`[TestClass]`, `[TestMethod]`, `Assert.HasCount`, `Assert.IsEmpty`) in `CodeGenerator.Tests/App/`. Code emitted into the generated app's test project is **xUnit** (`[Fact]`, `Assert.True`).
- Generated-app namespaces: models `<App>.Models`, data `<App>.Data`, validation `<App>.Validation`.
- Regenerated files are overwritten every run. **Only** `<Model>Validator.Custom.cs` is write-once.
- Rule set is exactly: `required`, `maxlength`, `EmailAddress`, `Phone`, foreign-key presence. `Key`/`Id` emits **no** check. Numeric and date rules are out of scope.
- File writes stay best-effort via `AppScaffolder.WriteSource`, and every new file must be counted in the `expected` total so `ScaffoldResult.Success` reports shortfalls.
- Commit after every task. Do not squash tasks together.

---

## File Structure

**Created:**

| File | Responsibility |
|---|---|
| `AppScaffolding/ForeignKeys.cs` | FK detection shared by the seeder and validators |
| `AppScaffolding/SaveResultTemplate.cs` | Emits `Data/SaveResult.cs` |
| `AppScaffolding/ValidatorTemplate.cs` | Emits everything under `Validation/` |
| `CodeGenerator.Tests/App/ForeignKeysTests.cs` | Tests the extracted helper |
| `CodeGenerator.Tests/App/SaveResultTemplateTests.cs` | Emits + compiles `SaveResult` |
| `CodeGenerator.Tests/App/ValidatorTemplateTests.cs` | Rule-by-rule emission tests |
| `CodeGenerator.Tests/App/ValidatorCompilesTests.cs` | Roslyn compile gate for emitted validators |
| `CodeGenerator.Tests/App/ValidatorScaffoldingTests.cs` | `AppScaffolder` writes/preserves the right files |

**Modified:**

| File | Change |
|---|---|
| `AppScaffolding/SeedDataTemplate.cs:21,56,86` | Delegate to `ForeignKeys`; delete the private copy |
| `AppScaffolding/DomainModelMapper.cs:18-42` | Reserve the new generated type/namespace names |
| `AppScaffolding/RepositoryTemplate.cs:16-82` | Validate in `Create`/`Update`; return `SaveResult<T>` |
| `AppScaffolding/ConsoleMenuTemplate.cs:72-83` | Use `result.Errors` instead of `Validator.TryValidateObject` |
| `AppScaffolding/ViewModelTemplates.cs:163-179` | Use `ValidatorFactory` (this path bypasses `Repository`) |
| `AppScaffolding/WebApiProgramTemplate.cs:35-42` | `ValidationProblem` / `NotFound` / `Created` |
| `AppScaffolding/TestProjectTemplates.cs:9-49` | Fix `RoundTrip` for validation; add `ValidatorTests` |
| `AppScaffolding/AppScaffolder.cs:128-176` | Write the validation files; write-once guard for `.Custom.cs` |
| `AppScaffolding/README.md` | Document the `Validation/` folder |

---

## Task 1: Shared foreign-key detection

`SeedDataTemplate` already computes exactly the FK relationships the validators need, in a private method. Lift it so both callers share one definition — a second copy would drift, and the FK definition must stay identical to the one the console picker and seeder use.

**Files:**
- Create: `AppScaffolding/ForeignKeys.cs`
- Modify: `AppScaffolding/SeedDataTemplate.cs:21,56,86`
- Test: `CodeGenerator.Tests/App/ForeignKeysTests.cs`

**Interfaces:**
- Consumes: `CodeGenerationEngine.Models.ClassDefinition`, `PropertyDefinition`
- Produces:
  - `ForeignKeys.TargetsOf(ClassDefinition c, HashSet<string> allClassNames) -> IReadOnlyList<string>` (public)
  - `ForeignKeys.FieldNames(IReadOnlyList<ClassDefinition> classes) -> IReadOnlyList<string>` (public)
  - `ForeignKeys.ClassNameSet(IReadOnlyList<ClassDefinition> classes) -> HashSet<string>` (public)

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/ForeignKeysTests.cs`:

```csharp
using AppScaffolding;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

/// <summary>A foreign key is an int &lt;Target&gt;Id whose owner also declares a nav property
/// typed as that target. These tests pin that definition, which the seeder, the console picker
/// and the generated validators all depend on agreeing about.</summary>
[TestClass]
public class ForeignKeysTests
{
    private static ClassDefinition Class(string name, params PropertyDefinition[] properties)
    {
        var definition = new ClassDefinition { Name = name };
        definition.Properties.AddRange(properties);
        return definition;
    }

    private static PropertyDefinition Prop(string name, string type) =>
        new() { Name = name, Type = type };

    [TestMethod]
    public void Int_target_id_with_a_matching_nav_property_is_a_foreign_key()
    {
        var order = Class("Order", Prop("Id", "int"), Prop("CustomerId", "int"), Prop("Customer", "Customer"));
        var customer = Class("Customer", Prop("Id", "int"));
        var classes = new List<ClassDefinition> { order, customer };

        var targets = ForeignKeys.TargetsOf(order, ForeignKeys.ClassNameSet(classes));

        Assert.HasCount(1, targets);
        Assert.AreEqual("Customer", targets[0]);
    }

    [TestMethod]
    public void Int_target_id_without_a_nav_property_is_not_a_foreign_key()
    {
        var order = Class("Order", Prop("Id", "int"), Prop("CustomerId", "int"));
        var customer = Class("Customer", Prop("Id", "int"));
        var classes = new List<ClassDefinition> { order, customer };

        var targets = ForeignKeys.TargetsOf(order, ForeignKeys.ClassNameSet(classes));

        Assert.IsEmpty(targets);
    }

    [TestMethod]
    public void The_entitys_own_Id_is_not_a_foreign_key()
    {
        var customer = Class("Customer", Prop("Id", "int"));
        var classes = new List<ClassDefinition> { customer };

        Assert.IsEmpty(ForeignKeys.TargetsOf(customer, ForeignKeys.ClassNameSet(classes)));
    }

    [TestMethod]
    public void FieldNames_returns_each_foreign_key_field_once()
    {
        var order = Class("Order", Prop("CustomerId", "int"), Prop("Customer", "Customer"));
        var invoice = Class("Invoice", Prop("CustomerId", "int"), Prop("Customer", "Customer"));
        var customer = Class("Customer", Prop("Id", "int"));

        var fields = ForeignKeys.FieldNames(new List<ClassDefinition> { order, invoice, customer });

        Assert.HasCount(1, fields);
        Assert.AreEqual("CustomerId", fields[0]);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter ForeignKeysTests`
Expected: FAIL to build — `ForeignKeys` does not exist.

- [ ] **Step 3: Create the shared helper**

Create `AppScaffolding/ForeignKeys.cs`. The body of `TargetsOf` is moved verbatim from `SeedDataTemplate.ForeignKeyTargets`:

```csharp
using CodeGenerationEngine.Models;

namespace AppScaffolding;

/// <summary>
/// One definition of what counts as a foreign key in a generated app, shared by the seeder (which
/// needs it for dependency ordering and runtime FK assignment) and the generated validators (which
/// check that a non-zero FK points at a real row).
/// </summary>
/// <remarks>
/// Extracted from SeedDataTemplate so the two callers cannot drift. The generated console picker
/// applies the same rule at runtime by reflection (see ConsoleMenuTemplate.TryPickForeignKey), so
/// changing this definition means changing that too.
/// </remarks>
public static class ForeignKeys
{
    /// <summary>The class names in <paramref name="classes"/>, for use as the lookup set.</summary>
    public static HashSet<string> ClassNameSet(IReadOnlyList<ClassDefinition> classes) =>
        new(classes.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

    /// <summary>The class names this class references via a <c>&lt;Target&gt;Id</c> foreign key
    /// whose sibling nav property is typed as that target entity.</summary>
    public static IReadOnlyList<string> TargetsOf(ClassDefinition c, HashSet<string> allNames)
    {
        var targets = new List<string>();
        foreach (var p in c.Properties)
        {
            if (p.Type != "int" || p.Name.Length <= 2)
                continue;
            if (!p.Name.EndsWith("Id", StringComparison.Ordinal)
                || string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase))
                continue;

            var target = p.Name[..^2];
            if (allNames.Contains(target)
                && !string.Equals(target, c.Name, StringComparison.OrdinalIgnoreCase)
                && c.Properties.Any(n => string.Equals(n.Type, target, StringComparison.Ordinal)))
                targets.Add(target);
        }
        return targets;
    }

    /// <summary>The field names that are genuine foreign keys across <paramref name="classes"/> —
    /// a <c>&lt;Target&gt;Id</c> whose owner also has a nav property typed as that target.</summary>
    public static IReadOnlyList<string> FieldNames(IReadOnlyList<ClassDefinition> classes)
    {
        var names = ClassNameSet(classes);
        var fields = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in classes)
            foreach (var target in TargetsOf(c, names))
            {
                var field = target + "Id";
                if (seen.Add(field))
                    fields.Add(field);
            }
        return fields;
    }
}
```

- [ ] **Step 4: Point `SeedDataTemplate` at the shared helper**

In `AppScaffolding/SeedDataTemplate.cs`:

1. Line 21 — change `c => ForeignKeyTargets(c, names)` to `c => ForeignKeys.TargetsOf(c, names)`.
2. Line 19 — change `var names = new HashSet<string>(...)` to `var names = ForeignKeys.ClassNameSet(classes);`.
3. Delete the whole private `ForeignKeyTargets` method (lines 54-74, doc comment included).
4. Replace the body of the public `ForeignKeyFieldNames` with a delegation, keeping the method so existing callers and `SeedDataTemplateTests` are unaffected:

```csharp
    /// <summary>The field names that are genuine foreign keys across <paramref name="classes"/>.
    /// Delegates to <see cref="ForeignKeys"/>, which the generated validators share.</summary>
    public static IReadOnlyList<string> ForeignKeyFieldNames(IReadOnlyList<ClassDefinition> classes)
        => ForeignKeys.FieldNames(classes);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "ForeignKeysTests|SeedDataTemplateTests|SeedInvocationTests"`
Expected: PASS. `SeedDataTemplateTests` passing unchanged is the proof the extraction was behaviour-preserving.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/ForeignKeys.cs AppScaffolding/SeedDataTemplate.cs CodeGenerator.Tests/App/ForeignKeysTests.cs
git commit -m "refactor(scaffolding): share foreign-key detection between seeder and validators"
```

---

## Task 2: `SaveResult<T>` template

The result type `Repository<T>.Create`/`Update` will return. `NotFound` stays separate from `Errors` because `Update` already overloads `false` to mean "no such record", and the Web API needs to pick between 404 and 400.

**Files:**
- Create: `AppScaffolding/SaveResultTemplate.cs`
- Test: `CodeGenerator.Tests/App/SaveResultTemplateTests.cs`

**Interfaces:**
- Produces: `SaveResultTemplate.Source(string projectName) -> string`, emitting `<App>.Data.SaveResult<T>` with `Success`, `NotFound`, `Item`, `Errors`, and `AsDictionary()`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/SaveResultTemplateTests.cs`:

```csharp
using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>SaveResult is the seam every generated write path returns through, so it must compile
/// on its own before anything is built on top of it.</summary>
[TestClass]
public class SaveResultTemplateTests
{
    private static void AssertCompiles(string source)
    {
        var result = Compiler.Compile(source);
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var message = string.Join("\n", errors.Select(e => e.ToString()));
        Assert.IsTrue(result.Success, $"Compilation failed:\n{message}");
        Assert.IsEmpty(errors, $"Compilation errors:\n{message}");
    }

    [TestMethod]
    public void Emitted_save_result_compiles()
    {
        AssertCompiles(SaveResultTemplate.Source("Acme"));
    }

    [TestMethod]
    public void Emitted_save_result_declares_the_data_namespace()
    {
        Assert.Contains("namespace Acme.Data", SaveResultTemplate.Source("Acme"));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter SaveResultTemplateTests`
Expected: FAIL to build — `SaveResultTemplate` does not exist.

- [ ] **Step 3: Write the template**

Create `AppScaffolding/SaveResultTemplate.cs`:

```csharp
namespace AppScaffolding;

/// <summary>Emits <c>Data/SaveResult.cs</c>: what a validating write returns.</summary>
public static class SaveResultTemplate
{
    /// <summary>Source of <c>Data/SaveResult.cs</c> in <paramref name="projectName"/>.Data.</summary>
    public static string Source(string projectName) => $$"""
using System;
using System.Collections.Generic;

namespace {{projectName}}.Data;

/// <summary>The outcome of a validating write.</summary>
/// <remarks>
/// NotFound is kept distinct from Errors: an Update naming an unknown Id is a different failure
/// from an Update carrying an invalid record, and the Web API front-end maps them to 404 and 400
/// respectively.
/// </remarks>
public sealed class SaveResult<T>
{
    /// <summary>True when the record validated and was persisted.</summary>
    public bool Success { get; init; }

    /// <summary>True when no record with this Id exists. Only ever set by Update.</summary>
    public bool NotFound { get; init; }

    /// <summary>The persisted record, or null when the write did not happen.</summary>
    public T? Item { get; init; }

    /// <summary>Human-readable validation messages. Empty when Success is true.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>Every message under a single empty key, the shape Results.ValidationProblem wants.
    /// Messages are whole sentences rather than per-property, so they surface as top-level errors
    /// rather than being attributed to individual fields.</summary>
    public Dictionary<string, string[]> AsDictionary() =>
        new() { [string.Empty] = Errors is null ? Array.Empty<string>() : new List<string>(Errors).ToArray() };
}
""";
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter SaveResultTemplateTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/SaveResultTemplate.cs CodeGenerator.Tests/App/SaveResultTemplateTests.cs
git commit -m "feat(scaffolding): emit SaveResult<T> for validating writes"
```

---

## Task 3: Validation core — interfaces, lookup, factory

The four fixed files under `Validation/`. `ValidatorFactory` mirrors `StoreFactory.Create<T>()` because the front-end templates are generic over `T` and cannot name a concrete validator.

**Files:**
- Create: `AppScaffolding/ValidatorTemplate.cs`
- Modify: `AppScaffolding/DomainModelMapper.cs:18-42`
- Test: `CodeGenerator.Tests/App/ValidatorTemplateTests.cs`

**Interfaces:**
- Produces (all `public static string`, on `ValidatorTemplate`):
  - `ValidatorInterface(string projectName)` → `Validation/IValidator.cs`
  - `EntityLookupInterface(string projectName)` → `Validation/IEntityLookup.cs`
  - `StoreEntityLookup(string projectName)` → `Validation/StoreEntityLookup.cs`
  - `Factory(string projectName, IReadOnlyList<string> entityNames)` → `Validation/ValidatorFactory.cs`

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/ValidatorTemplateTests.cs`:

```csharp
using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>The fixed part of the generated Validation/ folder: the seams and the factory.</summary>
[TestClass]
public class ValidatorTemplateTests
{
    private static void AssertCompiles(string source)
    {
        var result = Compiler.Compile(source);
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var message = string.Join("\n", errors.Select(e => e.ToString()));
        Assert.IsTrue(result.Success, $"Compilation failed:\n{message}");
        Assert.IsEmpty(errors, $"Compilation errors:\n{message}");
    }

    [TestMethod]
    public void Validator_interface_compiles_and_declares_the_validation_namespace()
    {
        var source = ValidatorTemplate.ValidatorInterface("Acme");
        Assert.Contains("namespace Acme.Validation", source);
        AssertCompiles(source);
    }

    [TestMethod]
    public void Entity_lookup_interface_compiles()
    {
        AssertCompiles(ValidatorTemplate.EntityLookupInterface("Acme"));
    }

    [TestMethod]
    public void Factory_maps_every_entity_to_its_validator()
    {
        var source = ValidatorTemplate.Factory("Acme", new[] { "Customer", "Order" });

        Assert.Contains("typeof(Customer)", source);
        Assert.Contains("new CustomerValidator()", source);
        Assert.Contains("typeof(Order)", source);
        Assert.Contains("new OrderValidator()", source);
    }

    [TestMethod]
    public void Factory_falls_back_to_a_no_op_validator_for_an_unmapped_type()
    {
        // Repository<T> is generic: a T with no generated validator must not throw.
        Assert.Contains("NullValidator", ValidatorTemplate.Factory("Acme", new[] { "Customer" }));
    }

    [TestMethod]
    public void Factory_with_no_entities_still_compiles()
    {
        var source = ValidatorTemplate.Factory("Acme", System.Array.Empty<string>());
        Assert.Contains("NullValidator", source);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter ValidatorTemplateTests`
Expected: FAIL to build — `ValidatorTemplate` does not exist.

> Note: `ValidatorInterface` and `EntityLookupInterface` are compile-tested standalone. `StoreEntityLookup` and `Factory` reference `<App>.Data` and `<App>.Models` types that do not exist in isolation, so they are asserted structurally here and compile-tested for real in Task 5 and end-to-end in Task 8.

- [ ] **Step 3: Write the template**

Create `AppScaffolding/ValidatorTemplate.cs`:

```csharp
using CodeGenerationEngine.Models;

namespace AppScaffolding;

/// <summary>
/// Emits the generated app's <c>Validation/</c> folder: the <c>IValidator&lt;T&gt;</c> seam, the
/// <c>IEntityLookup</c> foreign-key seam, a <c>ValidatorFactory</c> that resolves a validator from
/// a type, and one validator per model.
/// </summary>
/// <remarks>
/// The factory exists because the front-end templates are generic over T (EntityMenu&lt;T&gt;,
/// MapEntity&lt;T&gt;, EntityViewModel&lt;T&gt;) and so cannot name CustomerValidator directly. It
/// deliberately mirrors StoreFactory.Create&lt;T&gt;().
/// </remarks>
public static class ValidatorTemplate
{
    /// <summary>Source of <c>Validation/IValidator.cs</c>.</summary>
    public static string ValidatorInterface(string projectName) => $$"""
using System.Collections.Generic;

namespace {{projectName}}.Validation;

/// <summary>Validates one entity, returning a message per broken rule.</summary>
public interface IValidator<T>
{
    /// <summary>Every rule this entity breaks. Empty means valid.</summary>
    IReadOnlyList<string> Validate(T item);
}
""";

    /// <summary>Source of <c>Validation/IEntityLookup.cs</c>.</summary>
    public static string EntityLookupInterface(string projectName) => $$"""
namespace {{projectName}}.Validation;

/// <summary>Existence checks a validator needs for foreign keys. Injected so a validator can be
/// unit-tested with a fake and no store on disk.</summary>
public interface IEntityLookup
{
    /// <summary>True when a record of this type with this Id exists.</summary>
    bool Exists<TEntity>(int id) where TEntity : class, new();
}
""";

    /// <summary>Source of <c>Validation/StoreEntityLookup.cs</c>.</summary>
    public static string StoreEntityLookup(string projectName) => $$"""
using System;
using System.Reflection;
using {{projectName}}.Data;

namespace {{projectName}}.Validation;

/// <summary>The real <see cref="IEntityLookup"/>: reads through the configured store.</summary>
public sealed class StoreEntityLookup : IEntityLookup
{
    /// <inheritdoc />
    public bool Exists<TEntity>(int id) where TEntity : class, new()
    {
        foreach (var item in StoreFactory.Create<TEntity>().GetAll())
            if (IdOf(item) == id)
                return true;

        return false;
    }

    private static int IdOf<TEntity>(TEntity item)
    {
        var property = typeof(TEntity).GetProperty("Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return (int)(property?.GetValue(item) ?? 0);
    }
}
""";

    /// <summary>Source of <c>Validation/ValidatorFactory.cs</c> for <paramref name="entityNames"/>.</summary>
    public static string Factory(string projectName, IReadOnlyList<string> entityNames)
    {
        var arms = string.Join("\n", entityNames.Select(name => $$"""
        if (typeof(T) == typeof({{name}}))
            return (IValidator<T>)(object)new {{name}}Validator();
"""));

        if (arms.Length == 0)
            arms = "        // This app declares no entities.";

        return $$"""
using System;
using System.Collections.Generic;
using {{projectName}}.Models;

namespace {{projectName}}.Validation;

/// <summary>Resolves the validator for an entity type. Mirrors StoreFactory.Create&lt;T&gt;().</summary>
public static class ValidatorFactory
{
    /// <summary>The validator for entity type <typeparamref name="T"/>, or a no-op validator when
    /// this app generated none for it.</summary>
    public static IValidator<T> Create<T>() where T : class, new()
    {
{{arms}}

        return new NullValidator<T>();
    }
}

/// <summary>Validates nothing. Repository&lt;T&gt; is generic, so an unmapped T must degrade to
/// "no rules" rather than throw.</summary>
internal sealed class NullValidator<T> : IValidator<T>
{
    /// <inheritdoc />
    public IReadOnlyList<string> Validate(T item) => Array.Empty<string>();
}
""";
    }
}
```

- [ ] **Step 4: Reserve the new generated names**

An entity called `Validation` would make `<App>.Validation` ambiguous exactly as `Data` and `Models` already do (CS0118), and an entity called `SaveResult` or `ValidatorFactory` would collide with a generated type. In `AppScaffolding/DomainModelMapper.cs`, extend `ReservedNames` — add to the first group (line 25):

```csharp
        "Data", "Models", "ViewModels", "Views", "Validation",
```

and add a new group after the "Types the generated app declares" block:

```csharp
        // Types the generated Validation/ folder and validating repository declare.
        "IValidator", "IEntityLookup", "StoreEntityLookup", "ValidatorFactory", "NullValidator",
        "SaveResult",
```

- [ ] **Step 5: Add the reserved-name test**

Append to `CodeGenerator.Tests/App/DomainModelMapperTests.cs`:

```csharp
    [TestMethod]
    public void An_entity_named_Validation_is_renamed_to_avoid_the_namespace()
    {
        var model = new CandidateDomainModel
        {
            Entities = { new CandidateEntity { Name = "Validation" } }
        };

        var classes = DomainModelMapper.ToClassDefinitions(model);

        Assert.HasCount(1, classes);
        Assert.AreEqual("ValidationItem", classes[0].Name);
    }
```

> If `CandidateDomainModel`/`CandidateEntity` construction in that file uses a different shape, copy the shape already used by the neighbouring tests rather than this one.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "ValidatorTemplateTests|DomainModelMapperTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add AppScaffolding/ValidatorTemplate.cs AppScaffolding/DomainModelMapper.cs CodeGenerator.Tests/App/ValidatorTemplateTests.cs CodeGenerator.Tests/App/DomainModelMapperTests.cs
git commit -m "feat(scaffolding): emit the validation seams and a type-resolving validator factory"
```

---

## Task 4: Per-model validator emission

The rule emitter. Reads the `Rules`/`Attributes` that `DomainModelMapper.AddValidation` already wrote, so nothing is re-inferred here.

**Files:**
- Modify: `AppScaffolding/ValidatorTemplate.cs` (add `Generated` and `CustomStub`)
- Test: `CodeGenerator.Tests/App/ValidatorTemplateTests.cs` (add cases)
- Test: `CodeGenerator.Tests/App/ValidatorCompilesTests.cs`

**Interfaces:**
- Consumes: `ForeignKeys.TargetsOf` (Task 1), `ValidatorTemplate.ValidatorInterface`/`EntityLookupInterface` (Task 3)
- Produces:
  - `ValidatorTemplate.Generated(string projectName, ClassDefinition definition, IReadOnlyList<string> foreignKeyTargets) -> string`
  - `ValidatorTemplate.CustomStub(string projectName, string className) -> string`

- [ ] **Step 1: Write the failing emission tests**

Append to `CodeGenerator.Tests/App/ValidatorTemplateTests.cs`:

```csharp
    private static CodeGenerationEngine.Models.ClassDefinition ClassWith(
        string name, params CodeGenerationEngine.Models.PropertyDefinition[] properties)
    {
        var definition = new CodeGenerationEngine.Models.ClassDefinition { Name = name };
        definition.Properties.AddRange(properties);
        return definition;
    }

    [TestMethod]
    public void A_required_rule_emits_a_blank_check()
    {
        var name = new CodeGenerationEngine.Models.PropertyDefinition { Name = "Name", Type = "string" };
        name.Rules.Add(new CodeGenerationEngine.Models.PropertyRule { Name = "required" });

        var source = ValidatorTemplate.Generated(
            "Acme", ClassWith("Customer", name), System.Array.Empty<string>());

        Assert.Contains("string.IsNullOrWhiteSpace(item.Name)", source);
        Assert.Contains("\"Name is required.\"", source);
    }

    [TestMethod]
    public void A_maxlength_rule_emits_a_length_check_using_the_rules_value()
    {
        var title = new CodeGenerationEngine.Models.PropertyDefinition { Name = "Title", Type = "string" };
        title.Rules.Add(new CodeGenerationEngine.Models.PropertyRule { Name = "maxlength", MaxLength = 120 });

        var source = ValidatorTemplate.Generated(
            "Acme", ClassWith("Book", title), System.Array.Empty<string>());

        Assert.Contains("Length: > 120", source);
        Assert.Contains("\"Title must be 120 characters or fewer.\"", source);
    }

    [TestMethod]
    public void An_email_attribute_emits_a_shape_check()
    {
        var email = new CodeGenerationEngine.Models.PropertyDefinition { Name = "Email", Type = "string" };
        email.Attributes.Add("EmailAddress");

        var source = ValidatorTemplate.Generated(
            "Acme", ClassWith("Contact", email), System.Array.Empty<string>());

        Assert.Contains("\"Email is not a valid email address.\"", source);
    }

    [TestMethod]
    public void A_phone_attribute_emits_a_shape_check()
    {
        var phone = new CodeGenerationEngine.Models.PropertyDefinition { Name = "Phone", Type = "string" };
        phone.Attributes.Add("Phone");

        var source = ValidatorTemplate.Generated(
            "Acme", ClassWith("Contact", phone), System.Array.Empty<string>());

        Assert.Contains("\"Phone is not a valid phone number.\"", source);
    }

    [TestMethod]
    public void A_key_attribute_emits_no_check()
    {
        // The store assigns Id in Create, so validating it would reject every new record.
        var id = new CodeGenerationEngine.Models.PropertyDefinition { Name = "Id", Type = "int" };
        id.Attributes.Add("Key");

        var source = ValidatorTemplate.Generated(
            "Acme", ClassWith("Customer", id), System.Array.Empty<string>());

        Assert.DoesNotContain("item.Id", source);
    }

    [TestMethod]
    public void A_foreign_key_target_emits_a_lookup_check()
    {
        var source = ValidatorTemplate.Generated(
            "Acme", ClassWith("Order"), new[] { "Customer" });

        Assert.Contains("item.CustomerId != 0", source);
        Assert.Contains("_lookup.Exists<Customer>(item.CustomerId)", source);
        Assert.Contains("\"CustomerId does not refer to a known Customer.\"", source);
    }

    [TestMethod]
    public void Every_validator_calls_the_custom_hook()
    {
        var source = ValidatorTemplate.Generated(
            "Acme", ClassWith("Customer"), System.Array.Empty<string>());

        Assert.Contains("ValidateCustom(item, errors);", source);
        Assert.Contains("partial void ValidateCustom(Customer item, List<string> errors);", source);
    }

    [TestMethod]
    public void The_custom_stub_implements_the_hook_as_a_partial_of_the_same_class()
    {
        var source = ValidatorTemplate.CustomStub("Acme", "Customer");

        Assert.Contains("public sealed partial class CustomerValidator", source);
        Assert.Contains("partial void ValidateCustom(Customer item, List<string> errors)", source);
    }
```

- [ ] **Step 2: Write the failing compile test**

Create `CodeGenerator.Tests/App/ValidatorCompilesTests.cs`:

```csharp
using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>Emitted validators must COMPILE, not merely contain the right substrings. Roslyn
/// error-recovery lets malformed emitted code pass syntax-tree assertions, and the rules here
/// interpolate both entity names and message literals — exactly where injection breaks compilation.
/// The model class and the two seams are composed in so the validator has real types to bind to.</summary>
[TestClass]
public class ValidatorCompilesTests
{
    private static void AssertCompiles(string source)
    {
        var result = Compiler.Compile(source);
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var message = string.Join("\n", errors.Select(e => e.ToString()));
        Assert.IsTrue(result.Success, $"Compilation failed:\n{message}");
        Assert.IsEmpty(errors, $"Compilation errors:\n{message}");
    }

    /// <summary>The emitted validator plus everything it binds against, with namespaces flattened
    /// so one Compile call sees the lot.</summary>
    private static string Compose(
        string modelDeclarations, ClassDefinition definition, IReadOnlyList<string> foreignKeyTargets)
    {
        var validator = ValidatorTemplate.Generated("Acme", definition, foreignKeyTargets)
            .Replace("using Acme.Models;", string.Empty)
            .Replace("namespace Acme.Validation;", string.Empty);

        var custom = ValidatorTemplate.CustomStub("Acme", definition.Name)
            .Replace("using Acme.Models;", string.Empty)
            .Replace("namespace Acme.Validation;", string.Empty);

        return """
using System;
using System.Collections.Generic;

public interface IValidator<T> { IReadOnlyList<string> Validate(T item); }
public interface IEntityLookup { bool Exists<TEntity>(int id) where TEntity : class, new(); }
public sealed class StoreEntityLookup : IEntityLookup
{
    public bool Exists<TEntity>(int id) where TEntity : class, new() => true;
}
"""
            + modelDeclarations + validator + custom;
    }

    private static PropertyDefinition Prop(string name, string type) => new() { Name = name, Type = type };

    [TestMethod]
    public void A_validator_with_every_rule_family_compiles()
    {
        var name = Prop("Name", "string");
        name.Rules.Add(new PropertyRule { Name = "required" });
        name.Rules.Add(new PropertyRule { Name = "maxlength", MaxLength = 200 });

        var email = Prop("Email", "string");
        email.Rules.Add(new PropertyRule { Name = "maxlength", MaxLength = 200 });
        email.Attributes.Add("EmailAddress");

        var phone = Prop("Phone", "string");
        phone.Attributes.Add("Phone");

        var id = Prop("Id", "int");
        id.Attributes.Add("Key");

        var definition = new ClassDefinition { Name = "Customer" };
        definition.Properties.AddRange(new[] { id, name, email, phone, Prop("RegionId", "int"), Prop("Region", "Region") });

        var models = """
public class Region { public int Id { get; set; } }
public class Customer
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public int RegionId { get; set; }
    public Region? Region { get; set; }
}
""";

        AssertCompiles(Compose(models, definition, new[] { "Region" }));
    }

    [TestMethod]
    public void A_validator_for_a_model_with_no_rules_compiles()
    {
        var definition = new ClassDefinition { Name = "Tag" };
        definition.Properties.Add(Prop("Id", "int"));

        AssertCompiles(Compose(
            "public class Tag { public int Id { get; set; } }",
            definition,
            System.Array.Empty<string>()));
    }

    [TestMethod]
    public void Two_email_properties_do_not_collide_on_a_local_variable()
    {
        // Each property's checks get their own try block precisely so locals stay scoped.
        var primary = Prop("Email", "string");
        primary.Attributes.Add("EmailAddress");
        var backup = Prop("BackupEmail", "string");
        backup.Attributes.Add("EmailAddress");

        var definition = new ClassDefinition { Name = "Contact" };
        definition.Properties.AddRange(new[] { primary, backup });

        AssertCompiles(Compose(
            "public class Contact { public string? Email { get; set; } public string? BackupEmail { get; set; } }",
            definition,
            System.Array.Empty<string>()));
    }
}
```

- [ ] **Step 3: Run both tests to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "ValidatorTemplateTests|ValidatorCompilesTests"`
Expected: FAIL to build — `ValidatorTemplate.Generated` and `.CustomStub` do not exist.

- [ ] **Step 4: Implement `Generated` and `CustomStub`**

Add to `AppScaffolding/ValidatorTemplate.cs`:

```csharp
    /// <summary>Rule name recorded by DomainModelMapper for a required field.</summary>
    private const string RequiredRule = "required";

    /// <summary>Rule name recorded by DomainModelMapper for a length bound.</summary>
    private const string MaxLengthRule = "maxlength";

    /// <summary>
    /// Source of <c>Validation/&lt;Model&gt;Validator.Generated.cs</c>: the inferred rules, plus the
    /// call to the hand-written hook. Regenerated on every build, so nothing here may be edited.
    /// </summary>
    /// <remarks>
    /// Rules are read from the Rules/Attributes that DomainModelMapper.AddValidation already
    /// recorded, never re-inferred, so a model's attributes and its validator cannot disagree.
    /// Each property's checks sit in their own try/catch: it scopes any locals the checks declare,
    /// and it keeps one throwing rule from aborting the rest — the same best-effort stance
    /// ValidationEngine.EmitSection takes.
    /// </remarks>
    public static string Generated(
        string projectName, ClassDefinition definition, IReadOnlyList<string> foreignKeyTargets)
    {
        var blocks = new List<string>();

        foreach (var property in definition.Properties)
            if (Checks(property) is { Count: > 0 } checks)
                blocks.Add(Block(checks));

        foreach (var target in foreignKeyTargets)
            blocks.Add(Block(new List<string>
            {
                $"if (item.{target}Id != 0 && !_lookup.Exists<{target}>(item.{target}Id))",
                $"    errors.Add(\"{target}Id does not refer to a known {target}.\");",
            }));

        var body = blocks.Count == 0
            ? "        // This model declares no inferred rules."
            : string.Join("\n\n", blocks);

        return $$"""
using System;
using System.Collections.Generic;
using {{projectName}}.Models;

namespace {{projectName}}.Validation;

/// <summary>Inferred rules for {{definition.Name}}. REGENERATED — edit
/// {{definition.Name}}Validator.Custom.cs instead.</summary>
public sealed partial class {{definition.Name}}Validator : IValidator<{{definition.Name}}>
{
    private readonly IEntityLookup _lookup;

    /// <summary>Validates against the real store.</summary>
    public {{definition.Name}}Validator() : this(new StoreEntityLookup()) { }

    /// <summary>Validates against the supplied lookup. Tests pass a fake.</summary>
    public {{definition.Name}}Validator(IEntityLookup lookup) => _lookup = lookup;

    /// <inheritdoc />
    public IReadOnlyList<string> Validate({{definition.Name}} item)
    {
        var errors = new List<string>();
        if (item is null)
        {
            errors.Add("{{definition.Name}} is null.");
            return errors;
        }

{{body}}

        ValidateCustom(item, errors);
        return errors;
    }

    /// <summary>Hand-written rules, implemented in {{definition.Name}}Validator.Custom.cs.</summary>
    partial void ValidateCustom({{definition.Name}} item, List<string> errors);
}
""";
    }

    /// <summary>Source of the write-once <c>Validation/&lt;Model&gt;Validator.Custom.cs</c>.</summary>
    public static string CustomStub(string projectName, string className) => $$"""
using System.Collections.Generic;
using {{projectName}}.Models;

namespace {{projectName}}.Validation;

/// <summary>Hand-written rules for {{className}}.</summary>
public sealed partial class {{className}}Validator
{
    /// <summary>Add rules the generator cannot infer — cross-field checks, business rules.
    /// Regenerating the app never overwrites this file.</summary>
    partial void ValidateCustom({{className}} item, List<string> errors)
    {
    }
}
""";

    /// <summary>Wraps one property's (or one relationship's) checks in its own try/catch.</summary>
    private static string Block(IReadOnlyList<string> checks)
    {
        var indented = string.Join("\n", checks.Select(line => "            " + line));
        return "        try\n        {\n" + indented + "\n        }\n        catch (Exception) { }";
    }

    /// <summary>The checks for one property, from the rules DomainModelMapper recorded. A
    /// navigation property and a [Key] Id both yield none.</summary>
    private static List<string> Checks(PropertyDefinition property)
    {
        var checks = new List<string>();
        if (property.Type != "string")
            return checks;

        var name = property.Name;

        if (property.Rules.Any(r => string.Equals(r.Name, RequiredRule, StringComparison.OrdinalIgnoreCase)))
        {
            checks.Add($"if (string.IsNullOrWhiteSpace(item.{name}))");
            checks.Add($"    errors.Add(\"{name} is required.\");");
        }

        var max = property.Rules
            .FirstOrDefault(r => string.Equals(r.Name, MaxLengthRule, StringComparison.OrdinalIgnoreCase))
            ?.MaxLength;

        if (max.HasValue)
        {
            checks.Add($"if (item.{name} is {{ Length: > {max.Value} }})");
            checks.Add($"    errors.Add(\"{name} must be {max.Value} characters or fewer.\");");
        }

        // Deliberately loose: these exist to match what [EmailAddress]/[Phone] already give the
        // console and WPF front-ends, not to be authoritative format validators.
        if (property.Attributes.Contains("EmailAddress"))
        {
            checks.Add($"if (!string.IsNullOrWhiteSpace(item.{name}))");
            checks.Add("{");
            checks.Add($"    var at = item.{name}!.IndexOf('@');");
            checks.Add($"    if (at <= 0 || at == item.{name}!.Length - 1)");
            checks.Add($"        errors.Add(\"{name} is not a valid email address.\");");
            checks.Add("}");
        }

        if (property.Attributes.Contains("Phone"))
        {
            checks.Add($"if (!string.IsNullOrWhiteSpace(item.{name}))");
            checks.Add("{");
            checks.Add($"    foreach (var character in item.{name}!)");
            checks.Add("    {");
            checks.Add("        if (!char.IsDigit(character) && character != ' ' && character != '+'");
            checks.Add("            && character != '-' && character != '(' && character != ')')");
            checks.Add("        {");
            checks.Add($"            errors.Add(\"{name} is not a valid phone number.\");");
            checks.Add("            break;");
            checks.Add("        }");
            checks.Add("    }");
            checks.Add("}");
        }

        return checks;
    }
```

- [ ] **Step 5: Run both tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "ValidatorTemplateTests|ValidatorCompilesTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/ValidatorTemplate.cs CodeGenerator.Tests/App/ValidatorTemplateTests.cs CodeGenerator.Tests/App/ValidatorCompilesTests.cs
git commit -m "feat(scaffolding): emit per-model validators from the recorded rules"
```

---

## Task 5: Write the validation files during scaffolding

Wires the templates into `AppScaffolder.Scaffold`, with the write-once guard for `.Custom.cs`.

**Files:**
- Modify: `AppScaffolding/AppScaffolder.cs:128-176`
- Test: `CodeGenerator.Tests/App/ValidatorScaffoldingTests.cs`

**Interfaces:**
- Consumes: `ForeignKeys` (Task 1), `SaveResultTemplate.Source` (Task 2), all four `ValidatorTemplate` members from Task 3, `Generated`/`CustomStub` (Task 4)
- Produces: a generated app containing `Validation/IValidator.cs`, `Validation/IEntityLookup.cs`, `Validation/StoreEntityLookup.cs`, `Validation/ValidatorFactory.cs`, `Data/SaveResult.cs`, and per model `Validation/<Model>Validator.Generated.cs` + `Validation/<Model>Validator.Custom.cs`

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/ValidatorScaffoldingTests.cs`. Follow the model-construction and temp-folder shape already used by `CodeGenerator.Tests/App/AppScaffolderWriteTests.cs` — read that file first and mirror it, including how it builds a `CandidateDomainModel` and which `ITargetScaffolder` it passes:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

/// <summary>The Validation/ folder is written, and a hand-edited .Custom.cs survives a rebuild.</summary>
[TestClass]
public class ValidatorScaffoldingTests
{
    private static string NewWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "scg-validators-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [TestMethod]
    public void Scaffolding_writes_the_validation_folder()
    {
        var workspace = NewWorkspace();
        try
        {
            var model = /* a two-entity CandidateDomainModel, shaped as AppScaffolderWriteTests builds one */;
            var result = new AppScaffolder().Scaffold(
                model, "Shop", workspace, new AppScaffolding.Targets.ClassLibraryTarget());

            var validation = Path.Combine(result.OutputFolder, "Validation");
            Assert.IsTrue(File.Exists(Path.Combine(validation, "IValidator.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(validation, "IEntityLookup.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(validation, "StoreEntityLookup.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(validation, "ValidatorFactory.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(result.OutputFolder, "Data", "SaveResult.cs")));
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); } catch (IOException) { }
        }
    }

    [TestMethod]
    public void A_hand_edited_custom_validator_survives_regeneration()
    {
        var workspace = NewWorkspace();
        try
        {
            var model = /* same model as above */;
            var scaffolder = new AppScaffolder();
            var first = scaffolder.Scaffold(
                model, "Shop", workspace, new AppScaffolding.Targets.ClassLibraryTarget());

            var custom = Directory
                .GetFiles(Path.Combine(first.OutputFolder, "Validation"), "*Validator.Custom.cs")
                .First();
            var generated = custom.Replace(".Custom.cs", ".Generated.cs");

            File.WriteAllText(custom, "// MY OWN RULES");
            File.WriteAllText(generated, "// STALE");

            scaffolder.Scaffold(model, "Shop", workspace, new AppScaffolding.Targets.ClassLibraryTarget());

            Assert.AreEqual("// MY OWN RULES", File.ReadAllText(custom));
            Assert.AreNotEqual("// STALE", File.ReadAllText(generated));
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); } catch (IOException) { }
        }
    }
}
```

Replace both `/* ... */` comments with the real model construction from `AppScaffolderWriteTests`. `ClassLibraryTarget` is chosen because it needs no WPF/ASP.NET tooling.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter ValidatorScaffoldingTests`
Expected: FAIL — `Validation/IValidator.cs` does not exist.

- [ ] **Step 3: Add the files to `Scaffold`**

In `AppScaffolding/AppScaffolder.cs`, after `var dataFolder = ...` (line 129) add:

```csharp
        var validationFolder = Path.Combine(outputFolder, "Validation");
```

In the `files` list initialiser (after the `SeedData.cs` entry, line 148), add:

```csharp
            (dataFolder, "SaveResult.cs", SaveResultTemplate.Source(projectName)),
            (validationFolder, "IValidator.cs", ValidatorTemplate.ValidatorInterface(projectName)),
            (validationFolder, "IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface(projectName)),
            (validationFolder, "StoreEntityLookup.cs", ValidatorTemplate.StoreEntityLookup(projectName)),
            (validationFolder, "ValidatorFactory.cs", ValidatorTemplate.Factory(projectName, entityNames)),
```

After the per-entity repository loop (line 164), add the per-model validators:

```csharp
        var classNameSet = ForeignKeys.ClassNameSet(classes);
        foreach (var definition in classes)
            files.Add((validationFolder, $"{definition.Name}Validator.Generated.cs",
                ValidatorTemplate.Generated(
                    projectName, definition, ForeignKeys.TargetsOf(definition, classNameSet))));
```

Then, immediately **after** the `foreach (var (folder, name, source) in files)` write loop (line 176), add the write-once pass:

```csharp
        // Written once, never overwritten: this is where the user's own rules live, and a rebuild
        // must not discard them. The .Generated.cs half above is refreshed every run.
        foreach (var definition in classes)
        {
            var fileName = $"{definition.Name}Validator.Custom.cs";
            if (File.Exists(Path.Combine(validationFolder, fileName)))
                continue;

            expected++;
            var customPath = WriteSource(
                validationFolder, fileName, ValidatorTemplate.CustomStub(projectName, definition.Name));
            if (customPath is not null)
                written.Add(customPath);
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "ValidatorScaffoldingTests|AppScaffolderWriteTests|AppScaffolderPersistenceTests"`
Expected: PASS. The two existing `AppScaffolder` suites passing is the check that the `expected`/`written` accounting still balances.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/AppScaffolder.cs CodeGenerator.Tests/App/ValidatorScaffoldingTests.cs
git commit -m "feat(scaffolding): write the Validation folder, preserving hand-written rules"
```

---

## Task 6: Enforce in the repository and update every call site

**This task is deliberately atomic.** Changing `Create`/`Update` return types breaks the console, WPF and Web API templates and the generated round-trip tests at once; splitting it would leave the generated app uncompilable between commits, and `GeneratedProjectBuildTests` would fail.

Two traps to respect:

1. **The WPF path bypasses `Repository`.** `EntityViewModel<T>.Save()` calls `_store.SaveAll(...)` directly, so repository-level enforcement does not reach it. It gets its own `ValidatorFactory.Create<T>()` call.
2. **`RoundTrip` creates a blank `new T()`**, which now fails `Required`. It must populate fields, and the values must satisfy the email and phone shape checks — `"Test"` in an `Email` property would fail.

`SeedData` writes through `StoreFactory`/`store.SaveAll`, not `Repository`, so seeding stays unvalidated and needs no change.

**Files:**
- Modify: `AppScaffolding/RepositoryTemplate.cs:41-61`
- Modify: `AppScaffolding/ConsoleMenuTemplate.cs:72-83`
- Modify: `AppScaffolding/ViewModelTemplates.cs:163-179`
- Modify: `AppScaffolding/WebApiProgramTemplate.cs:35-42`
- Modify: `AppScaffolding/TestProjectTemplates.cs:30-39`
- Test: `CodeGenerator.Tests/App/RepositoryTemplateTests.cs` (add cases)

**Interfaces:**
- Consumes: `SaveResult<T>` (Task 2), `ValidatorFactory.Create<T>()` (Task 3)
- Produces: `Repository<T>.Create(T) -> SaveResult<T>`, `Repository<T>.Update(T) -> SaveResult<T>`. `GetAll`, `GetById` and `Delete` are unchanged.

- [ ] **Step 1: Write the failing tests**

Append to `CodeGenerator.Tests/App/RepositoryTemplateTests.cs`:

```csharp
    [TestMethod]
    public void The_repository_resolves_a_validator_and_returns_a_save_result()
    {
        var source = RepositoryTemplate.Base("Acme");

        Assert.Contains("ValidatorFactory.Create<T>()", source);
        Assert.Contains("public SaveResult<T> Create(T item)", source);
        Assert.Contains("public SaveResult<T> Update(T item)", source);
    }

    [TestMethod]
    public void The_repository_reports_a_missing_record_as_NotFound_not_as_a_validation_error()
    {
        Assert.Contains("NotFound = true", RepositoryTemplate.Base("Acme"));
    }

    [TestMethod]
    public void The_repository_validates_before_touching_the_store()
    {
        var source = RepositoryTemplate.Base("Acme");
        var validate = source.IndexOf("_validator.Validate(item)", StringComparison.Ordinal);
        var load = source.IndexOf("_store.GetAll()", StringComparison.Ordinal);

        Assert.IsGreaterThan(0, validate);
        Assert.IsLessThan(load, validate);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter RepositoryTemplateTests`
Expected: FAIL — the emitted repository has no validator.

- [ ] **Step 3: Make the repository validate**

In `AppScaffolding/RepositoryTemplate.cs`, add `using {{appName}}.Validation;` to the emitted using block (after line 20's `using System.Reflection;`), add the validator field beside `_store`, and replace `Create` and `Update`:

```csharp
    private readonly IStore<T> _store = StoreFactory.Create<T>();
    private readonly IValidator<T> _validator = ValidatorFactory.Create<T>();
```

```csharp
    /// <summary>Validates, then assigns the next Id, appends the record and persists it.</summary>
    public SaveResult<T> Create(T item)
    {
        var errors = _validator.Validate(item);
        if (errors.Count > 0)
            return new SaveResult<T> { Success = false, Errors = errors };

        var all = _store.GetAll();
        SetId(item, _store.NextId(all));
        all.Add(item);

        if (!_store.SaveAll(all))
            return new SaveResult<T> { Success = false, Errors = new[] { "Could not persist " + typeof(T).Name + "." } };

        return new SaveResult<T> { Success = true, Item = item };
    }

    /// <summary>Validates, then replaces the record sharing this item's Id.</summary>
    public SaveResult<T> Update(T item)
    {
        var errors = _validator.Validate(item);
        if (errors.Count > 0)
            return new SaveResult<T> { Success = false, Errors = errors };

        var all = _store.GetAll();
        var index = all.FindIndex(existing => GetId(existing) == GetId(item));
        if (index < 0)
            return new SaveResult<T> { Success = false, NotFound = true };

        all[index] = item;

        if (!_store.SaveAll(all))
            return new SaveResult<T> { Success = false, Errors = new[] { "Could not persist " + typeof(T).Name + "." } };

        return new SaveResult<T> { Success = true, Item = item };
    }
```

- [ ] **Step 4: Update the console front-end**

In `AppScaffolding/ConsoleMenuTemplate.cs`, replace the `Validator.TryValidateObject` block and the bare `_repository.Create(item);` (lines 72-83) with:

```csharp
        var result = _repository.Create(item);
        if (!result.Success)
        {
            foreach (var error in result.Errors)
                System.Console.WriteLine("  " + error);
            System.Console.WriteLine("Not added.");
            return;
        }

        System.Console.WriteLine("Added.");
```

Remove `using System.ComponentModel.DataAnnotations;` from that template's emitted using block if nothing else in the file uses it, and add `using {{appName}}.Validation;` only if the template references `ValidatorFactory` (it does not — it goes through the repository).

- [ ] **Step 5: Update the WPF front-end**

In `AppScaffolding/ViewModelTemplates.cs`, replace the `Save()` body's validation loop (lines 165-174) with a direct validator call, because this path writes through `_store`, not `Repository`:

```csharp
        var validator = ValidatorFactory.Create<T>();
        foreach (var record in Records)
        {
            var errors = validator.Validate(record!);
            if (errors.Count > 0)
            {
                Status = $"Cannot save: {errors[0]}";
                return;
            }
        }
```

Add `using {{appName}}.Validation;` to that template's emitted using block, and drop `using System.ComponentModel.DataAnnotations;` if now unused.

- [ ] **Step 6: Update the Web API front-end**

In `AppScaffolding/WebApiProgramTemplate.cs`, replace the `MapPost` and `MapPut` registrations (lines 35-42) with:

```csharp
    app.MapPost($"/{route}", (T item) =>
    {
        var result = repository.Create(item);
        return result.Success
            ? Results.Created($"/{route}", result.Item)
            : Results.ValidationProblem(result.AsDictionary());
    });

    app.MapPut($"/{route}/{{id:int}}", (int id, T item) =>
    {
        var result = repository.Update(item);
        if (result.NotFound)
            return Results.NotFound();

        return result.Success
            ? Results.NoContent()
            : Results.ValidationProblem(result.AsDictionary());
    });
```

- [ ] **Step 7: Fix the generated round-trip test**

In `AppScaffolding/TestProjectTemplates.cs`, replace `RoundTrip` and add `Fill` (lines 30-39). A blank record now fails validation, and the filled values must satisfy the email and phone checks:

```csharp
    private static void RoundTrip<T>(Repository<T> repository) where T : class, new()
    {
        var item = new T();
        Fill(item);

        var created = repository.Create(item);
        Assert.True(created.Success, string.Join("; ", created.Errors));

        var id = IdOf(created.Item!);
        Assert.NotEqual(0, id);
        Assert.NotNull(repository.GetById(id));

        var updated = repository.Update(created.Item!);
        Assert.True(updated.Success, string.Join("; ", updated.Errors));

        Assert.True(repository.Delete(id));
        Assert.Null(repository.GetById(id));
    }

    /// <summary>Populates writable properties with values that satisfy the generated rules.
    /// A property the validator treats as an email or a phone number needs a value of that
    /// shape, so a plain placeholder will not do.</summary>
    private static void Fill<T>(T item)
    {
        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite)
                continue;

            if (property.PropertyType == typeof(string))
                property.SetValue(item, PlaceholderFor(property.Name));
            else if (property.PropertyType == typeof(DateTime))
                property.SetValue(item, DateTime.UtcNow);
        }
    }

    private static string PlaceholderFor(string propertyName)
    {
        if (propertyName.Contains("Email", StringComparison.OrdinalIgnoreCase))
            return "test@example.com";

        if (propertyName.Contains("Phone", StringComparison.OrdinalIgnoreCase))
            return "+1 555 0100";

        return "Test";
    }
```

Add `using System;` to that emitted file's using block (it already emits `using System.Reflection;`).

- [ ] **Step 8: Run the full suite**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror` then `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release`
Expected: PASS, including `GeneratedProjectBuildTests`. If a generated app fails to compile, read the compiler output — a missing `using <App>.Validation;` in one of the four templates is the likely cause.

- [ ] **Step 9: Commit**

```bash
git add AppScaffolding/RepositoryTemplate.cs AppScaffolding/ConsoleMenuTemplate.cs AppScaffolding/ViewModelTemplates.cs AppScaffolding/WebApiProgramTemplate.cs AppScaffolding/TestProjectTemplates.cs CodeGenerator.Tests/App/RepositoryTemplateTests.cs
git commit -m "feat(scaffolding): validate in Repository.Create/Update and update every call site"
```

---

## Task 7: Generated per-model validator tests

The generated app should ship tests for its own validators. The emitter knows which property is required, so the test it writes can assert something real instead of a tautology.

**Files:**
- Modify: `AppScaffolding/TestProjectTemplates.cs`
- Modify: `AppScaffolding/AppScaffolder.cs` (`ScaffoldTestProject`)
- Test: `CodeGenerator.Tests/App/TestProjectTemplatesTests.cs` (add cases)

**Interfaces:**
- Consumes: `ValidatorTemplate` output (Task 4), `IEntityLookup` (Task 3)
- Produces: `TestProjectTemplates.ValidatorTests(string projectName, string entityName, string? requiredPropertyName) -> string`

- [ ] **Step 1: Write the failing test**

Append to `CodeGenerator.Tests/App/TestProjectTemplatesTests.cs`:

```csharp
    [TestMethod]
    public void Validator_tests_assert_a_required_field_when_the_model_has_one()
    {
        var source = TestProjectTemplates.ValidatorTests("Shop", "Customer", "Name");

        Assert.Contains("class CustomerValidatorTests", source);
        Assert.Contains("Name is required.", source);
        Assert.Contains("IEntityLookup", source);
    }

    [TestMethod]
    public void Validator_tests_for_a_model_with_no_required_field_still_assert_a_valid_record_passes()
    {
        var source = TestProjectTemplates.ValidatorTests("Shop", "Tag", null);

        Assert.Contains("class TagValidatorTests", source);
        Assert.DoesNotContain("is required.", source);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter TestProjectTemplatesTests`
Expected: FAIL to build — `ValidatorTests` does not exist.

- [ ] **Step 3: Add the template**

Add to `AppScaffolding/TestProjectTemplates.cs`:

```csharp
    /// <summary>Source of <c><paramref name="entityName"/>ValidatorTests.cs</c>. When the model has
    /// a required property, also asserts a blank record is rejected — otherwise the only honest
    /// assertion is that a filled record passes.</summary>
    public static string ValidatorTests(string projectName, string entityName, string? requiredPropertyName)
    {
        var requiredFact = requiredPropertyName is null ? string.Empty : $$"""


    [Fact]
    public void A_blank_{{entityName}}_reports_its_required_field()
    {
        var errors = new {{entityName}}Validator(new AlwaysFound()).Validate(new {{entityName}}());

        Assert.Contains("{{requiredPropertyName}} is required.", errors);
    }
""";

        return $$"""
using System;
using System.Reflection;
using {{projectName}}.Models;
using {{projectName}}.Validation;
using Xunit;

namespace {{projectName}}.Tests;

/// <summary>Rules generated for {{entityName}}. Uses a fake lookup so no store is needed.</summary>
public class {{entityName}}ValidatorTests
{
    /// <summary>Every foreign key resolves, so only the property rules are under test.</summary>
    private sealed class AlwaysFound : IEntityLookup
    {
        public bool Exists<TEntity>(int id) where TEntity : class, new() => true;
    }

    [Fact]
    public void A_populated_{{entityName}}_is_valid()
    {
        var item = new {{entityName}}();
        foreach (var property in typeof({{entityName}}).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite)
                continue;

            if (property.PropertyType == typeof(string))
                property.SetValue(item, Placeholder(property.Name));
            else if (property.PropertyType == typeof(DateTime))
                property.SetValue(item, DateTime.UtcNow);
        }

        var errors = new {{entityName}}Validator(new AlwaysFound()).Validate(item);

        Assert.Empty(errors);
    }

    [Fact]
    public void A_null_{{entityName}}_is_reported_rather_than_throwing()
    {
        var errors = new {{entityName}}Validator(new AlwaysFound()).Validate(null!);

        Assert.NotEmpty(errors);
    }
{{requiredFact}}

    private static string Placeholder(string propertyName)
    {
        if (propertyName.Contains("Email", StringComparison.OrdinalIgnoreCase))
            return "test@example.com";

        if (propertyName.Contains("Phone", StringComparison.OrdinalIgnoreCase))
            return "+1 555 0100";

        return "Test";
    }
}
""";
    }
```

- [ ] **Step 4: Emit one per model**

`ScaffoldTestProject` currently takes only entity **names**, but choosing the required property needs the `ClassDefinition`. Change its signature to take the classes and derive the names inside:

In `AppScaffolding/AppScaffolder.cs`, change the declaration (line 196) to:

```csharp
    private static string? ScaffoldTestProject(
        string workspaceRoot, string appProjectName, IReadOnlyList<ClassDefinition> classes)
```

and at the top of the method add:

```csharp
        var entityNames = classes.Select(c => c.Name).ToList();
```

Update the call site (line 178) to `ScaffoldTestProject(workspaceRoot, projectName, classes)`.

Then, after the three existing `WriteSource` calls in that method, add:

```csharp
        // One validator test file per model, so the generated suite covers the generated rules.
        foreach (var definition in classes)
        {
            var required = definition.Properties.FirstOrDefault(p =>
                p.Type == "string"
                && p.Rules.Any(r => string.Equals(r.Name, "required", StringComparison.OrdinalIgnoreCase)));

            if (WriteSource(testFolder, $"{definition.Name}ValidatorTests.cs",
                    TestProjectTemplates.ValidatorTests(appProjectName, definition.Name, required?.Name)) is null)
                return null;
        }
```

- [ ] **Step 5: Run the suite**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "TestProjectTemplatesTests|AppScaffolderWriteTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/TestProjectTemplates.cs AppScaffolding/AppScaffolder.cs CodeGenerator.Tests/App/TestProjectTemplatesTests.cs
git commit -m "feat(scaffolding): generate validator tests into the app's test project"
```

---

## Task 8: End-to-end verification and docs

Proves a real generated app compiles, its own tests pass, and validation actually rejects a bad record — none of which the unit tests establish.

**Files:**
- Modify: `CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs`
- Modify: `AppScaffolding/README.md`

**Interfaces:**
- Consumes: everything from Tasks 1-7

- [ ] **Step 1: Read the existing integration test**

Read `CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs` in full and note how it invokes `dotnet build`/`dotnet test`, how it skips when tooling is absent, and its timeout handling. Mirror that shape exactly — do not invent a new harness.

- [ ] **Step 2: Add the failing end-to-end test**

Add one test following that file's established pattern, asserting:

1. A console app generated from a two-entity model (one with a `Name` field, one referenced by a foreign key) **builds**.
2. Its generated test project **passes** `dotnet test`.

```csharp
    [TestMethod]
    public void A_generated_console_app_with_validators_builds_and_its_tests_pass()
    {
        // Body mirrors the existing build-and-test helper in this file, including its
        // tooling-absent skip and its timeout. Assert both the build and the test run succeed.
    }
```

Replace the comment with the real body copied from the neighbouring test, changing only the model so it has a required `Name` and a foreign key.

- [ ] **Step 3: Run it**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter GeneratedProjectBuildTests`
Expected: PASS. A failure here is a real defect in Tasks 1-7, not a test problem — read the captured `dotnet build` output before changing the test.

- [ ] **Step 4: Document the new folder**

In `AppScaffolding/README.md`, add a `Validation/` section to the generated-layout description covering: the files emitted, that `.Generated.cs` is overwritten while `.Custom.cs` is written once, that `ValidatorFactory` mirrors `StoreFactory`, that `Repository.Create`/`Update` return `SaveResult<T>`, that the WPF save path validates through `ValidatorFactory` directly because it bypasses `Repository`, and that `SeedData` is deliberately not validated.

- [ ] **Step 5: Full gate**

Run:
```bash
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release
```
Expected: build clean, all tests pass. Record the actual test count in the commit message.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs AppScaffolding/README.md
git commit -m "test(scaffolding): end-to-end build of a generated app with validators"
```

---

## Task 9: Verify against the real CLI

Piped-stdin probing of the real CLI has repeatedly found defects a green unit suite hid, because the tests enter the pipeline downstream of the defect. This task exists to catch that class of bug.

**Files:** none (verification only)

- [ ] **Step 1: Rebuild the CLI**

`dotnet test` leaves the CLI's `bin` stale, so a green fix can look dead. Run:

```bash
dotnet build SmartCodeGenerator/CodeGenerator/CodeGenerator.csproj -c Release
```

- [ ] **Step 2: Drive `build app:` non-interactively**

Pipe a `build app:` session into the built CLI (redirected stdin works; `CommandLineEditor` falls back to `ReadLine`). Approve the review loop, then inspect the generated app on disk. Confirm:

- `Validation/` contains the four fixed files plus a `.Generated.cs` and a `.Custom.cs` per model.
- `Data/SaveResult.cs` exists.
- The app builds: `dotnet build <generated app>.csproj -c Release`.

- [ ] **Step 3: Prove validation actually rejects**

Run the generated console app, add a record leaving a required field blank, and confirm it prints the rule message and "Not added." rather than saving.

- [ ] **Step 4: Prove the custom hook survives**

Write a rule into one `.Custom.cs`, re-run the same `build app:`, and confirm the file is unchanged while its `.Generated.cs` was refreshed.

- [ ] **Step 5: Record the results**

Append the outcome to `.superpowers/sdd/progress.md`. If anything failed, stop and fix it before opening the PR — do not report the branch ready on unit tests alone.

---

## Self-Review

**Spec coverage:**

| Spec requirement | Task |
|---|---|
| `<Model>Validator` per model, self-contained | 4 |
| `IValidator<T>` seam | 3 |
| `IEntityLookup` + `StoreEntityLookup` | 3 |
| `ValidatorFactory` mirroring `StoreFactory` | 3 |
| `.Generated.cs` regenerated / `.Custom.cs` write-once | 4 (emit), 5 (guard) |
| `.Generated.cs` not `.g.cs` | 4, 5 |
| `Data/SaveResult.cs` with `AsDictionary` under one empty key | 2 |
| Baseline rules: required, maxlength, email, phone | 4 |
| `[Key]`/Id emits no check | 4 |
| Navigation properties emit no check | 4 (`Checks` returns early for non-string) |
| Foreign-key presence rule | 1 (detection), 4 (emission) |
| Repository enforcement, `SaveResult<T>` returns | 6 |
| Console / WPF / Web API call sites | 6 |
| Shared FK detection refactor | 1 |
| Generated `<Model>ValidatorTests.cs` | 7 |
| Per-rule try/catch; unverifiable FK skipped, not failed | 4 |
| `Validate(null)` returns an error, does not throw | 4 (emit), 7 (asserted) |
| Both constructors on every validator | 4 |
| `.Custom.cs` deletable without breaking the build | 4 (partial method) |
| Best-effort writes counted in `expected` | 5 |
| Roslyn compile-testing of emitted code | 2, 3, 4 |
| Generated-app integration (`dotnet test`) | 8 |
| Release `-warnaserror` gate | 6, 8 |
| Docs | 8 |

Gaps found and closed while reviewing: the spec never said what reserves the `Validation` namespace against an entity of that name (added to Task 3, with a test), nor that the WPF path bypasses `Repository` (Task 6, trap 1), nor that the existing generated round-trip test creates a blank entity that validation now rejects (Task 6, trap 7/step 7). The spec's claim that repository enforcement covers "every write path" is therefore true only once the WPF path gets its own validator call — Task 6 does that, and Task 8's README change records it.

**Placeholder scan:** Three steps deliberately defer to an existing file's shape rather than inventing one — the `CandidateDomainModel` construction in Task 5 Step 1, the `DomainModelMapperTests` model shape in Task 3 Step 5, and the `dotnet build`/`dotnet test` harness in Task 8 Step 2. Each names the exact file to copy from and why. Everything else carries literal code.

**Type consistency:** `ForeignKeys.TargetsOf`/`FieldNames`/`ClassNameSet` (Task 1) are used with those names in Tasks 4 and 5. `ValidatorTemplate.ValidatorInterface`/`EntityLookupInterface`/`StoreEntityLookup`/`Factory`/`Generated`/`CustomStub` are consistent across Tasks 3, 4, 5 and the compile tests. `SaveResult<T>`'s members (`Success`, `NotFound`, `Item`, `Errors`, `AsDictionary`) are used identically in Tasks 2, 6 and 7. `ValidateCustom(T item, List<string> errors)` has one signature in Tasks 4 and 7. `ScaffoldTestProject`'s signature change from `IReadOnlyList<string>` to `IReadOnlyList<ClassDefinition>` is made in Task 7 along with its only call site.
