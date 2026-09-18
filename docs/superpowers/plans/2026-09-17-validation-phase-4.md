# Validation Phase 4 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a business rule — hand-stated via `add business validation`, or accepted from a Cross-Field/Calculation proposal the data supports — reach a generated app's validator, through one shared emission path.

**Architecture:** A new conditional `Business` stage joins `Security`/`Field`/`Entity` in the emitted validator's pipeline, built the same way `Entity` was in phase 3 — a `{{businessStage}}` string spliced in after `{{entityStage}}`, gated on non-empty content. It is fed by `IValidationRule` objects from two sources that converge into one list on `ClassDefinition.BusinessRules`: `ValidationRegistry.Get(className, ValidationCategory.Business)` (rules typed by hand, already parsed and stored by the existing `add business validation` command) and accepted `EntityRuleProposal`s (a new per-entity proposal, mirroring `CandidateField`'s existing `RuleProposal`) produced by a new entity-wide scan alongside the existing per-field one. An accepted proposal already *is* a `ComparisonRule`/`ArithmeticRule`/`PercentageRule` object — there is no second construction step at accept time.

**Tech Stack:** C# / .NET 10, MSTest (`CodeGenerator.Tests`, `DomainDiscovery.Tests`), xUnit (`DataIngestion.Tests` — **do not use MSTest attributes there**), Roslyn (`Compiler.CompileSources`) for compile-and-invoke tests, Blazor Server (bUnit) for the review page.

**Spec:** `docs/superpowers/specs/2026-09-17-validation-phase-4-design.md` (parent: `docs/superpowers/specs/2026-09-06-validation-architecture-design.md`; most recent sibling: `docs/superpowers/specs/2026-09-11-validation-phase-3-design.md`)

**Branch:** `validation-phase-4`, already created, spec committed on it (`a9021b1`).

## Global Constraints

- **CI builds `SmartCodeGenerator/SmartCodeGenerator.slnx` in Release with `--no-incremental -warnaserror`, then `dotnet test --no-build`.** Before declaring a task done, run:
  `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror`
- **Repo root is `D:\SmartCodeGenerator-Repository`; the solution is one level down in `SmartCodeGenerator/`.** All paths in this plan are given from the repo root.
- **`CodeGenerator.Tests` and `DomainDiscovery.Tests` are MSTest** (`[TestClass]`/`[TestMethod]`, `Assert.AreEqual` etc. — but see the next two bullets). **`DataIngestion.Tests` is xUnit** (`[Fact]`/`[Theory]`, `Assert.Equal(expected, actual)` — the argument order is the OPPOSITE of MSTest's `Assert.AreEqual`). Task 4/5's new test file lives in `DataIngestion.Tests` — write it as xUnit, not MSTest.
- **`[DataTestMethod]` is MSTEST0044** (error under `-warnaserror`). Use `[TestMethod]` with `[DataRow]`.
- **`Assert.AreEqual(n, x.Count)` is MSTEST0037.** Use `Assert.HasCount(n, x)`.
- **Emitted C# is tested by COMPILING it**, with `Compiler.CompileSources` over the real files in their real namespaces — never a substring check standing in for a compile. `EmittedSource.Flatten` strips `using`/`namespace` and proves nothing about cross-namespace resolution.
- **Red before green.** Every task writes the failing test first and runs it to see it fail for the stated reason.
- **Never infer a proposal from a sample.** The new entity-wide scan (tasks 4-5) reads `TabularTable` in full, never `ImportedData` (capped at `ImportedData.MaxSeedRows`).
- **A proposal is never pre-accepted.** `EntityRuleProposal.Accepted` starts `false` and only a user action sets it — same rule as `RuleProposal.Accepted`.
- The checkout is CRLF; the Write tool produces LF-only files. Existing files keep their endings — never rewrite a whole file just to fix them, and check byte-mode CR/LF counts after multiple `Edit` passes on the same file (the Edit tool has silently flipped a whole CRLF file to LF-only before, on this branch).
- Commit after every task. Attribution line on every commit:
  `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`

---

## File Structure

**New files**

| File | Responsibility |
|---|---|
| `CodeGenerationEngine/Models/EntityRuleProposal.cs` | The entity-scoped counterpart to `RuleProposal`: a Cross-Field or Calculation rule the data supports, naming 2-3 properties, wrapping an already-built `IValidationRule`. |
| `CodeGenerationEngine/Core/Validation/Rules/PercentageRule.cs` | A 16th rule (Business category, no parser — only ever produced by the Calculation scan): `Total == Net * (1 + Rate)` within tolerance. |
| `DataIngestion/EntityRuleProposals.cs` | The entity-wide scan: `For(entity, table)` returns the Cross-Field and Calculation proposals one entity's rows support. |
| `CodeGenerator.Tests/App/BusinessStageEmissionTests.cs` | Task 1: the Business stage compiles and runs, given a hand-built rule list. |
| `CodeGenerator.Tests/App/BusinessRulesReachAppTests.cs` | Task 3: a rule registered via `ValidationRegistry` reaches `ClassDefinition.BusinessRules` and the emitted validator. |
| `DataIngestion.Tests/EntityRuleProposalsTests.cs` | Tasks 4-5: the two scans' zero-counter-example gates, xUnit. |
| `CodeGenerator.Tests/App/AcceptedEntityProposalReachesValidatorTests.cs` | Task 6: an accepted proposal, run through the real `DataModelBuilder` → `DomainModelMapper` → `ValidatorTemplate` pipeline, refuses live data. |

**Modified files**

| File | Change |
|---|---|
| `AppScaffolding/ValidatorTemplate.cs` | `Generated(...)` gains a `businessRules` parameter and emits the conditional `Business` stage. |
| `CodeGenerationEngine/Models/ClassDefinition.cs` | `BusinessRules` — the merged, ready-to-emit list. |
| `DomainDiscovery/Model/CandidateDomainModel.cs` | `CandidateEntity.Proposals` — the entity-level mirror of `CandidateField.Proposals`. |
| `AppScaffolding/AppScaffolder.cs` | Pass `definition.BusinessRules` into `ValidatorTemplate.Generated`. |
| `AppScaffolding/DomainModelMapper.cs` | New `AddEntityValidation` — merges `ValidationRegistry` lookup with accepted `EntityRuleProposal`s onto `ClassDefinition.BusinessRules`. |
| `DataIngestion/DataModelBuilder.cs` | `Build` gains a call to a new `AddEntityProposals`, alongside the existing `AddProposals`. |
| `CodeGenerator/App/ProposalReport.cs` | `All` also flattens `entity.Proposals` into the same numbered list. |
| `CodeGenerator/Web/Components/Stages/ReviewStage.razor` | An entity-scoped proposal checkbox block, beside the per-field one. |

---

## Task order and why

Tasks 1-3 deliver requirement 8 (Business Rule) end to end: task 1 builds the emission mechanics in isolation (a hand-built rule list, no registry yet); task 2 lays the data-model groundwork task 3's merge needs even though nothing populates it yet; task 3 wires the real `ValidationRegistry` in, which is the whole of job 1. Tasks 4-6 deliver requirements 9 and 14 (Cross-Field, Calculation) by producing `EntityRuleProposal`s that ride the exact same `ClassDefinition.BusinessRules` list task 3 built — job 2 adds no second emission path. Tasks 7-8 extend both review surfaces. Task 9 runs a real generated app, which phase 3's own history says is the only step that finds what the other eight miss.

---

### Task 1: The Business stage in the emitted validator

**Files:**
- Modify: `AppScaffolding/ValidatorTemplate.cs`
- Test: `CodeGenerator.Tests/App/BusinessStageEmissionTests.cs`

**Interfaces:**
- Consumes: `CodeGenerationEngine.Core.Validation.IValidationRule` (`Emit(ValidationEmitContext)`, `Category`), `ValidationEmitContext(StringBuilder, IReadOnlyList<PropertyDefinition>)`, `CodeGenerationEngine.Core.Validation.Rules.ComparisonRule(string left, string right, string violationOp, string original)` — all pre-existing, unmodified.
- Produces: `ValidatorTemplate.Generated(string rootNamespace, ClassDefinition definition, IReadOnlyList<string> foreignKeyTargets, bool referencesRequired, IReadOnlyList<(string Entity, string Property)>? referencedBy = null, IReadOnlyList<IValidationRule>? businessRules = null)` — one new trailing optional parameter, so every existing call site (18+ across `AppScaffolder.cs` and test fixtures) keeps compiling untouched.

**Why the adapter is not a straight "run Emit unchanged" (a correction to the design spec's own prose):** `ComparisonRule`/`ArithmeticRule`/`EitherOrRule`/`RequiredWhenRule`/`SameAsRule.Emit` all hardcode two things that assumed the OLDER `generate validator` command's shape: a literal `this.{Property}` (instance member access — that command emits `Validate()` as an INSTANCE method on the entity's own partial class) and a literal local named `errors` of type `List<string>`. The new `Business` stage is a `static` method (no `this`) whose own `List<ValidationError>` parameter is already conventionally named `errors` (matching `Security`/`Field`/`Entity`). Two small, mechanical fixes, not a change to any rule class:
1. Name the Business stage's own `List<ValidationError>` parameter `pipelineErrors` instead of `errors`, freeing the name `errors` for a local `List<string>` the rules can write into exactly as they already do.
2. After collecting every rule's `Emit` output into one `StringBuilder`, replace `"this."` with `"item."` on the accumulated text before splicing it in — `item` is already the stage's own local (`context.Item`), matching every other stage.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The Business stage, proven with a hand-built rule list — no registry, no scan, just the
/// emission mechanics. Task 3 wires ValidationRegistry in; this task only proves the stage
/// itself compiles and runs given any IValidationRule list.
/// </summary>
[TestClass]
public class BusinessStageEmissionTests
{
    private const string LookupStub = """
namespace Acme.Validation;

public sealed class StoreEntityLookup : IEntityLookup
{
    public bool Exists<TEntity>(int id) where TEntity : class, new() => true;

    public int CountReferencing<TEntity>(string foreignKeyProperty, int id)
        where TEntity : class, new() => 0;

    public bool AnyOtherWith<TEntity>(string propertyName, object? value, int exceptId)
        where TEntity : class, new() => false;
}
""";

    [TestMethod]
    public void A_comparison_rule_refuses_a_record_that_breaks_it()
    {
        var definition = new ClassDefinition { Name = "Booking" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "StartDate", Type = "DateTime" });
        definition.Properties.Add(new PropertyDefinition { Name = "EndDate", Type = "DateTime" });

        var rule = new ComparisonRule(
            "StartDate", "EndDate", "> 0", "StartDate must not be after EndDate");

        var validate = CompiledValidator(definition, [rule]);

        var badItem = System.Activator.CreateInstance(validate.EntityType)!;
        validate.EntityType.GetProperty("StartDate")!.SetValue(badItem, new System.DateTime(2026, 9, 10));
        validate.EntityType.GetProperty("EndDate")!.SetValue(badItem, new System.DateTime(2026, 9, 1));

        var errors = validate.Errors(badItem);

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], "StartDate must not be after EndDate");
    }

    [TestMethod]
    public void The_same_rule_passes_a_record_that_does_not_break_it()
    {
        var definition = new ClassDefinition { Name = "Booking" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "StartDate", Type = "DateTime" });
        definition.Properties.Add(new PropertyDefinition { Name = "EndDate", Type = "DateTime" });

        var rule = new ComparisonRule(
            "StartDate", "EndDate", "> 0", "StartDate must not be after EndDate");

        var validate = CompiledValidator(definition, [rule]);

        var goodItem = System.Activator.CreateInstance(validate.EntityType)!;
        validate.EntityType.GetProperty("StartDate")!.SetValue(goodItem, new System.DateTime(2026, 9, 1));
        validate.EntityType.GetProperty("EndDate")!.SetValue(goodItem, new System.DateTime(2026, 9, 10));

        Assert.IsEmpty(validate.Errors(goodItem));
    }

    [TestMethod]
    public void No_business_rules_means_no_business_stage_at_all()
    {
        var definition = new ClassDefinition { Name = "Booking" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        var source = ValidatorTemplate.Generated("Acme", definition, [], referencesRequired: false);

        Assert.IsFalse(source.Contains("private static void Business("));
        StringAssert.DoesNotContain(source, "        Business,");
    }

    private static (System.Type EntityType, System.Func<object, IReadOnlyList<string>> Errors)
        CompiledValidator(ClassDefinition definition, IReadOnlyList<IValidationRule> businessRules)
    {
        var modelProperties = string.Join("\n", definition.Properties
            .Select(p => $"    public {p.Type} {p.Name} {{ get; set; }}"));

        var models = $$"""
using System;

namespace Acme.Models;

public class {{definition.Name}}
{
{{modelProperties}}
}
""";

        var result = Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/ValidationPipeline.cs", ValidationPipelineTemplate.Source("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", [], login: false)),
            ("Validation/StoreEntityLookup.cs", LookupStub),
            ("Models/" + definition.Name + ".cs", models),
            ("Validation/" + definition.Name + "Validator.Generated.cs",
                ValidatorTemplate.Generated(
                    "Acme", definition, [], referencesRequired: false, businessRules: businessRules)),
            ("Validation/" + definition.Name + "Validator.Custom.cs",
                ValidatorTemplate.CustomStub("Acme", definition.Name)),
        });

        var message = string.Join("\n",
            result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(e => e.ToString()));
        Assert.IsTrue(result.Success, $"Compilation failed:\n{message}");

        var assembly = System.Reflection.Assembly.Load(result.AssemblyBytes!);
        var entityType = assembly.GetType("Acme.Models." + definition.Name)!;
        var validatorType = assembly.GetType("Acme.Validation." + definition.Name + "Validator")!;
        var validator = System.Activator.CreateInstance(validatorType)!;
        var validateMethod = validatorType.GetMethod("Validate")!;

        var lookupType = assembly.GetType("Acme.Validation.StoreEntityLookup")!;
        var contextType = assembly.GetType("Acme.Validation.ValidationContext`1")!.MakeGenericType(entityType);
        var forMethod = contextType.GetMethod("For")!;
        var insert = assembly.GetType("Acme.Validation.ValidationOperation")!.GetField("Insert")!.GetValue(null)!;

        return (entityType, item =>
        {
            var context = forMethod.Invoke(null, [item, insert, System.Activator.CreateInstance(lookupType)]);
            var outcome = validateMethod.Invoke(validator, [context])!;
            return (IReadOnlyList<string>)outcome.GetType().GetProperty("Messages")!.GetValue(outcome)!;
        });
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.App.BusinessStageEmissionTests"`
Expected: compile error (`ValidatorTemplate.Generated` has no `businessRules` parameter yet) — the third test (`No_business_rules_means_no_business_stage_at_all`) would already pass against the CURRENT template, so it is not evidence on its own; the first two are.

- [ ] **Step 3: Add the `businessRules` parameter and the Business stage**

In `AppScaffolding/ValidatorTemplate.cs`, add near the top:

```csharp
using System.Text;
using CodeGenerationEngine.Core.Validation;
```

Change the `Generated` signature (currently ends `IReadOnlyList<(string Entity, string Property)>? referencedBy = null)`):

```csharp
    public static string Generated(
        string rootNamespace, ClassDefinition definition, IReadOnlyList<string> foreignKeyTargets,
        bool referencesRequired,
        IReadOnlyList<(string Entity, string Property)>? referencedBy = null,
        IReadOnlyList<IValidationRule>? businessRules = null)
    {
        referencedBy ??= Array.Empty<(string Entity, string Property)>();
        businessRules ??= Array.Empty<IValidationRule>();
```

Immediately after the existing `entityStage`/`stageList` block (the one ending `: "        Security,\n        Field,\n        Entity,";`), add:

```csharp
        // Business runs last: a rule stated across more than one field, hand-typed via
        // `add business validation` or accepted from a Cross-Field/Calculation proposal, should
        // trust that Entity has already passed. Emit(ValidationEmitContext) is the OLDER
        // generate-validator command's shape — a literal `this.Property` and a literal local
        // named `errors` of type List<string> — so this stage renames its OWN List<ValidationError>
        // parameter to `pipelineErrors`, giving `errors` back to the rules unchanged, and rewrites
        // `this.` to `item.` on the accumulated text: a static stage method has no `this`.
        var businessSource = new StringBuilder();
        var businessCtx = new ValidationEmitContext(businessSource, definition.Properties);

        foreach (var rule in businessRules)
            rule.Emit(businessCtx);

        var businessBody = businessSource.ToString().Replace("this.", "item.");

        var businessStage = businessBody.Length == 0
            ? string.Empty
            : $$"""


    /// <summary>What no single property or foreign key can judge: a rule spanning more than one
    /// field, either typed by hand or accepted from a proposal the data supported. Runs last,
    /// after Entity has already passed.</summary>
    private static void Business(ValidationContext<{{definition.Name}}> context, List<ValidationError> pipelineErrors)
    {
        var item = context.Item;
        if (item is null)
            return;

        var errors = new List<string>();

{{businessBody}}
        foreach (var m in errors)
            pipelineErrors.Add(new ValidationError(null, m));
    }
""";

        if (businessBody.Length > 0)
            stageList += "\n        Business,";
```

And splice `{{businessStage}}` into the returned template, immediately after `{{entityStage}}` (the line `{{entityStage}}` currently sits right before the `ValidateCustom` partial declaration — change it to `{{entityStage}}{{businessStage}}`).

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.App.BusinessStageEmissionTests"`
Expected: 3/3 PASS.

- [ ] **Step 5: Run the full focused filter for `ValidatorTemplate` to confirm no regression**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ValidatorTemplate|FullyQualifiedName~ValidatorEmitsEveryRuleKind|FullyQualifiedName~ValidatorCompiles|FullyQualifiedName~ValidatorStages"`
Expected: all PASS — the new optional parameter must not change a single existing emitted file.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/ValidatorTemplate.cs CodeGenerator.Tests/App/BusinessStageEmissionTests.cs
git commit -m "$(cat <<'EOF'
Add the Business stage to the emitted validator

Fed by any IValidationRule list; nothing calls it with real rules yet.
The adapter over the fifteen existing rule classes needs a renamed
stage parameter and a this.->item. rewrite, since Emit() assumes an
instance method's `this` and a local literally named `errors`.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: `EntityRuleProposal`, and `CandidateEntity.Proposals`

**Files:**
- Create: `CodeGenerationEngine/Models/EntityRuleProposal.cs`
- Modify: `DomainDiscovery/Model/CandidateDomainModel.cs`
- Test: `DomainDiscovery.Tests` (MSTest) — a new file, `DomainDiscovery.Tests/CandidateEntityProposalsTests.cs`

**Interfaces:**
- Produces: `CodeGenerationEngine.Models.EntityRuleProposalKind { CrossField, Calculation }`; `EntityRuleProposal { Kind, IReadOnlyList<string> Properties, IValidationRule Rule, string Evidence, bool Accepted }`; `DomainDiscovery.Model.CandidateEntity.Proposals : List<EntityRuleProposal>`.

**Why now, with nothing populating it yet:** task 3's merge loop (`foreach (var proposal in entity.Proposals)`) needs this list to exist before it can compile, and building it as its own small step — with no scan behind it — keeps that step reviewable on its own, the same way phase 3 separated `RuleProposal` (its task 10) from the scan that fills it (task 9, landed first) and the code that reads it (task 11).

- [ ] **Step 1: Write the failing test**

```csharp
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace DomainDiscovery.Tests;

[TestClass]
public class CandidateEntityProposalsTests
{
    [TestMethod]
    public void An_entity_starts_with_no_proposals()
    {
        var entity = new CandidateEntity { Name = "Booking" };

        Assert.IsEmpty(entity.Proposals);
    }

    [TestMethod]
    public void A_proposal_carries_its_rule_unaccepted_until_taken()
    {
        var entity = new CandidateEntity { Name = "Booking" };

        entity.Proposals.Add(new EntityRuleProposal
        {
            Kind = EntityRuleProposalKind.CrossField,
            Properties = ["StartDate", "EndDate"],
            Rule = new ComparisonRule("StartDate", "EndDate", "> 0", "StartDate must not be after EndDate"),
            Evidence = "StartDate is always on or before EndDate, in all 40 rows"
        });

        Assert.IsFalse(entity.Proposals[0].Accepted);
        Assert.HasCount(2, entity.Proposals[0].Properties);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=DomainDiscovery.Tests.CandidateEntityProposalsTests"`
Expected: compile error — `EntityRuleProposal`/`EntityRuleProposalKind`/`CandidateEntity.Proposals` do not exist yet.

- [ ] **Step 3: Add `EntityRuleProposal`**

Create `CodeGenerationEngine/Models/EntityRuleProposal.cs`:

```csharp
using CodeGenerationEngine.Core.Validation;

namespace CodeGenerationEngine.Models;

/// <summary>What an entity-scoped proposal would become.</summary>
public enum EntityRuleProposalKind
{
    /// <summary>One column never exceeds another, across every row.</summary>
    CrossField,

    /// <summary>One column always equals an arithmetic identity over two others.</summary>
    Calculation
}

/// <summary>
/// A rule spanning more than one property of the same entity, the data supports, offered to
/// the user and enforced only if they take it.
/// </summary>
/// <remarks>
/// The entity-scoped counterpart to <see cref="RuleProposal"/>, which names exactly one
/// property. Separate rather than a widened <see cref="RuleProposal"/> for the same reason
/// phase 3 added <c>EntityStageRules</c> beside <c>EmittableRules</c> instead of editing it.
/// <para>
/// <see cref="Rule"/> is already the real <see cref="IValidationRule"/> the scan built —
/// unlike <see cref="RuleProposal.Rule"/>'s <see cref="PropertyRule"/>, there is no further
/// conversion step once this is accepted: an accepted proposal and a hand-typed rule are the
/// identical object.
/// </para>
/// </remarks>
public sealed class EntityRuleProposal
{
    public required EntityRuleProposalKind Kind { get; init; }

    /// <summary>Every property this proposal names, for the review card's evidence line.</summary>
    public required IReadOnlyList<string> Properties { get; init; }

    /// <summary>The rule this becomes when accepted — already built, ready to add to a class's
    /// business rules.</summary>
    public required IValidationRule Rule { get; init; }

    /// <summary>What makes this arguable rather than mysterious: "holds in all 40 rows".</summary>
    public required string Evidence { get; init; }

    /// <summary>False until a user picks it.</summary>
    public bool Accepted { get; set; }
}
```

- [ ] **Step 4: Add `CandidateEntity.Proposals`**

In `DomainDiscovery/Model/CandidateDomainModel.cs`, inside `CandidateEntity`, immediately after `public EntityRole Role { get; set; }`:

```csharp

    /// <summary>
    /// Cross-Field and Calculation rules the scan found hold in every row, offered to the user
    /// unaccepted — the entity-level mirror of <see cref="CandidateField.Proposals"/>.
    /// </summary>
    public List<CodeGenerationEngine.Models.EntityRuleProposal> Proposals { get; } = new();
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=DomainDiscovery.Tests.CandidateEntityProposalsTests"`
Expected: 2/2 PASS.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerationEngine/Models/EntityRuleProposal.cs DomainDiscovery/Model/CandidateDomainModel.cs DomainDiscovery.Tests/CandidateEntityProposalsTests.cs
git commit -m "$(cat <<'EOF'
Add EntityRuleProposal, the entity-scoped mirror of RuleProposal

Unpopulated until tasks 4-5 build the scan that fills it. Rule is
already the built IValidationRule, not a shape converted later.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Wire `ValidationRegistry` into `build app` — requirement 8 delivered

**Files:**
- Modify: `CodeGenerationEngine/Models/ClassDefinition.cs`
- Modify: `AppScaffolding/DomainModelMapper.cs`
- Modify: `AppScaffolding/AppScaffolder.cs`
- Test: `CodeGenerator.Tests/App/BusinessRulesReachAppTests.cs`

**Interfaces:**
- Consumes: `CodeGenerationEngine.Core.ValidationRegistry.Get(string className, ValidationCategory category)` (pre-existing); `DomainModelMapper.ToClassDefinitions(CandidateDomainModel model)` (pre-existing, returns `IReadOnlyList<ClassDefinition>`); `ValidatorTemplate.Generated(..., businessRules: ...)` from task 1.
- Produces: `ClassDefinition.BusinessRules : List<IValidationRule>`.

**No persistence gap, no separate helper call in `IncrementalScaffolder.cs`.** `IncrementalScaffolder.Add` never calls `ValidatorTemplate.Generated` directly — it delegates the whole build to a fresh `new AppScaffolder().Scaffold(...)` in a scratch folder, which goes through the exact same `DomainModelMapper.ToClassDefinitions` this task changes. `ValidationRegistry` is a process-wide static, visible identically either way. **This is a correction to the design spec's own wording** ("Both `AppScaffolder.cs`... and `IncrementalScaffolder.cs`... call this helper once per class") — the real shared seam is one level deeper, inside `DomainModelMapper`, and `IncrementalScaffolder.cs` needs no code change at all.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>
/// A rule registered via ValidationRegistry.Add (what `add business validation` already does)
/// reaches ClassDefinition.BusinessRules once DomainModelMapper builds the class, with no
/// scan and no accepted proposal involved.
/// </summary>
[TestClass]
[DoNotParallelize] // ValidationRegistry is a process-wide static.
public class BusinessRulesReachAppTests
{
    [TestInitialize]
    public void Setup() => ValidationRegistry.Clear();

    [TestCleanup]
    public void Cleanup() => ValidationRegistry.Clear();

    [TestMethod]
    public void A_hand_registered_rule_lands_on_the_matching_class()
    {
        var rule = new ComparisonRule("StartDate", "EndDate", "> 0", "StartDate must not be after EndDate");
        ValidationRegistry.Add("Booking", rule);

        var model = new CandidateDomainModel();
        var booking = new CandidateEntity { Name = "Booking" };
        booking.Fields.Add(new CandidateField { Name = "StartDate", Type = "DateTime" });
        booking.Fields.Add(new CandidateField { Name = "EndDate", Type = "DateTime" });
        model.Entities.Add(booking);

        var classes = DomainModelMapper.ToClassDefinitions(model);
        var definition = classes.Single(c => c.Name == "Booking");

        Assert.HasCount(1, definition.BusinessRules);
        Assert.AreSame(rule, definition.BusinessRules[0]);
    }

    [TestMethod]
    public void A_class_with_no_registered_rule_gets_an_empty_list()
    {
        var model = new CandidateDomainModel();
        var order = new CandidateEntity { Name = "Order" };
        order.Fields.Add(new CandidateField { Name = "Total", Type = "decimal" });
        model.Entities.Add(order);

        var classes = DomainModelMapper.ToClassDefinitions(model);

        Assert.IsEmpty(classes.Single(c => c.Name == "Order").BusinessRules);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.App.BusinessRulesReachAppTests"`
Expected: compile error — `ClassDefinition.BusinessRules` does not exist yet.

- [ ] **Step 3: Add `ClassDefinition.BusinessRules`**

In `CodeGenerationEngine/Models/ClassDefinition.cs`:

```csharp
using CodeGenerationEngine.Core.Validation;

namespace CodeGenerationEngine.Models;

public class ClassDefinition
{
    public string Name { get; set; } = string.Empty;

    public List<PropertyDefinition> Properties { get; set; } = [];

    public List<MethodDefinition> Methods { get; set; } = [];

    public List<string> Usings { get; set; } = [];

    /// <summary>Rules spanning more than one property — hand-stated via `add business
    /// validation`, or accepted from a Cross-Field/Calculation proposal. Reaches the Business
    /// stage in the emitted validator.</summary>
    public List<IValidationRule> BusinessRules { get; set; } = [];
}
```

- [ ] **Step 4: Add `DomainModelMapper.AddEntityValidation`**

In `AppScaffolding/DomainModelMapper.cs`, add near the top:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
```

Find the call site inside `ToClassDefinitions` that currently reads:

```csharp
        AddNavigationProperties(model, classes, names);
        AddAuditFields(classes);
        AddValidation(classes);
```

Change it to:

```csharp
        AddNavigationProperties(model, classes, names);
        AddAuditFields(classes);
        AddValidation(classes);
        AddEntityValidation(classes, model, names);
```

Add the new method (near `AddValidation`):

```csharp
    /// <summary>
    /// Merges hand-stated business rules with accepted entity-scoped proposals onto the class
    /// that matches each entity, so both reach the Business stage through the one list
    /// ValidatorTemplate.Generated reads.
    /// </summary>
    /// <remarks>
    /// The one shared seam both AppScaffolder and IncrementalScaffolder need: IncrementalScaffolder
    /// never calls ValidatorTemplate.Generated directly, it delegates its whole build to a fresh
    /// AppScaffolder run, which reaches this same method. ValidationRegistry is a process-wide
    /// static, so both paths see identical rules with no extra plumbing.
    /// </remarks>
    private static void AddEntityValidation(
        List<ClassDefinition> classes, CandidateDomainModel model, Dictionary<string, string> names)
    {
        foreach (var entity in model.Entities)
        {
            if (!names.TryGetValue(entity.Name, out var className))
                continue;

            var definition = classes.FirstOrDefault(c => c.Name == className);
            if (definition is null)
                continue;

            var rules = new List<IValidationRule>(
                ValidationRegistry.Get(className, ValidationCategory.Business));

            foreach (var proposal in entity.Proposals)
            {
                if (!proposal.Accepted)
                    continue;

                rules.Add(proposal.Rule);
            }

            definition.BusinessRules = rules;
        }
    }
```

- [ ] **Step 5: Wire `AppScaffolder.cs`'s call site**

In `AppScaffolding/AppScaffolder.cs`, the existing call:

```csharp
        foreach (var definition in classes)
            files.Add((validationFolder, $"{definition.Name}Validator.Generated.cs",
                ValidatorTemplate.Generated(
                    rootNamespace, definition, ForeignKeys.TargetsOf(definition, classNameSet),
                    referencesRequired: roles.RoleOf(definition.Name)
                        is EntityRole.Transaction or EntityRole.DocumentLine,
                    referencedBy: ForeignKeys.ReferencesTo(definition.Name, classes))));
```

becomes:

```csharp
        foreach (var definition in classes)
            files.Add((validationFolder, $"{definition.Name}Validator.Generated.cs",
                ValidatorTemplate.Generated(
                    rootNamespace, definition, ForeignKeys.TargetsOf(definition, classNameSet),
                    referencesRequired: roles.RoleOf(definition.Name)
                        is EntityRole.Transaction or EntityRole.DocumentLine,
                    referencedBy: ForeignKeys.ReferencesTo(definition.Name, classes),
                    businessRules: definition.BusinessRules)));
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.App.BusinessRulesReachAppTests"`
Expected: 2/2 PASS.

- [ ] **Step 7: Run the full focused filter for `DomainModelMapper`/`AppScaffolder`/validator emission**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~DomainModelMapper|FullyQualifiedName~AppScaffolder|FullyQualifiedName~Validator"`
Expected: all PASS.

- [ ] **Step 8: Commit**

```bash
git add CodeGenerationEngine/Models/ClassDefinition.cs AppScaffolding/DomainModelMapper.cs AppScaffolding/AppScaffolder.cs CodeGenerator.Tests/App/BusinessRulesReachAppTests.cs
git commit -m "$(cat <<'EOF'
Wire ValidationRegistry into build app -- requirement 8 delivered

A rule typed via `add business validation` now reaches a generated
app's Business stage. IncrementalScaffolder needs no change: it
delegates its whole build to AppScaffolder, which reaches the same
DomainModelMapper.AddEntityValidation this adds.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: The Cross-Field scan

**Files:**
- Create: `DataIngestion/EntityRuleProposals.cs`
- Modify: `DataIngestion/DataModelBuilder.cs`
- Test: `DataIngestion.Tests/EntityRuleProposalsTests.cs` (**xUnit** — `[Fact]`, `Assert.Equal(expected, actual)`)

**Interfaces:**
- Consumes: `DataIngestion.Schema.TabularTable` (`Value(row, column)`, `Rows.Count`), `DomainDiscovery.Model.CandidateEntity.Fields : List<CandidateField>`, `CodeGenerationEngine.Core.Validation.Rules.ComparisonRule` (task 1).
- Produces: `DataIngestion.EntityRuleProposals.For(CandidateEntity entity, TabularTable table) : IReadOnlyList<EntityRuleProposal>`.

- [ ] **Step 1: Write the failing test**

```csharp
using CodeGenerationEngine.Models;
using DataIngestion;
using DataIngestion.Schema;
using DomainDiscovery.Model;
using Xunit;

namespace DataIngestion.Tests;

public class EntityRuleProposalsTests
{
    private static TabularTable TableOf(string name, string column, params string[] values)
        => TableOf(name, [column], values.Select(v => new[] { v }).ToArray());

    private static TabularTable TableOf(string name, string[] columns, string?[][] rows)
    {
        var table = new TabularTable { Name = name };
        foreach (var column in columns)
            table.Columns.Add(new TabularColumn { Name = column });
        foreach (var row in rows)
            table.Rows.Add(row);
        return table;
    }

    [Fact]
    public void Proposes_a_cross_field_rule_when_left_never_exceeds_right()
    {
        var entity = new CandidateEntity { Name = "Booking" };
        entity.Fields.Add(new CandidateField { Name = "StartDate", Type = "DateTime" });
        entity.Fields.Add(new CandidateField { Name = "EndDate", Type = "DateTime" });

        var table = TableOf("Booking", ["StartDate", "EndDate"],
        [
            ["2026-01-01", "2026-01-10"],
            ["2026-02-01", "2026-02-01"],
            ["2026-03-01", "2026-03-15"],
        ]);

        var proposals = EntityRuleProposals.For(entity, table);

        var crossField = Assert.Single(proposals.Where(p => p.Kind == EntityRuleProposalKind.CrossField
            && p.Properties[0] == "StartDate" && p.Properties[1] == "EndDate"));
        Assert.False(crossField.Accepted);
        Assert.Contains("StartDate", crossField.Evidence);
    }

    [Fact]
    public void One_counter_example_across_the_full_table_blocks_the_proposal()
    {
        var entity = new CandidateEntity { Name = "Booking" };
        entity.Fields.Add(new CandidateField { Name = "StartDate", Type = "DateTime" });
        entity.Fields.Add(new CandidateField { Name = "EndDate", Type = "DateTime" });

        var table = TableOf("Booking", ["StartDate", "EndDate"],
        [
            ["2026-01-01", "2026-01-10"],
            ["2026-05-01", "2026-01-01"], // StartDate after EndDate: the counter-example
        ]);

        var proposals = EntityRuleProposals.For(entity, table);

        Assert.Empty(proposals.Where(p => p.Kind == EntityRuleProposalKind.CrossField
            && p.Properties[0] == "StartDate" && p.Properties[1] == "EndDate"));
    }

    [Fact]
    public void A_column_that_never_strictly_precedes_the_other_is_not_proposed()
    {
        var entity = new CandidateEntity { Name = "Snapshot" };
        entity.Fields.Add(new CandidateField { Name = "A", Type = "int" });
        entity.Fields.Add(new CandidateField { Name = "B", Type = "int" });

        var table = TableOf("Snapshot", ["A", "B"],
        [
            ["5", "5"],
            ["7", "7"],
        ]);

        var proposals = EntityRuleProposals.For(entity, table);

        Assert.Empty(proposals);
    }

    [Fact]
    public void A_blank_cell_in_either_column_is_skipped_not_counted_as_a_break()
    {
        var entity = new CandidateEntity { Name = "Booking" };
        entity.Fields.Add(new CandidateField { Name = "StartDate", Type = "DateTime" });
        entity.Fields.Add(new CandidateField { Name = "EndDate", Type = "DateTime" });

        var table = TableOf("Booking", ["StartDate", "EndDate"],
        [
            ["2026-01-01", "2026-01-10"],
            [null, "2026-02-01"],
            ["2026-03-01", null],
        ]);

        var proposals = EntityRuleProposals.For(entity, table);

        Assert.Single(proposals.Where(p => p.Kind == EntityRuleProposalKind.CrossField));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~DataIngestion.Tests.EntityRuleProposalsTests"`
Expected: compile error — `DataIngestion.EntityRuleProposals` does not exist yet.

- [ ] **Step 3: Implement the Cross-Field half of the scan**

Create `DataIngestion/EntityRuleProposals.cs`:

```csharp
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;
using DataIngestion.Schema;
using DomainDiscovery.Model;

namespace DataIngestion;

/// <summary>
/// Turns one entity's complete rows into the Cross-Field and Calculation rules they support —
/// the entity-wide counterpart to <see cref="RuleProposals"/>, which scans one column at a time.
/// </summary>
/// <remarks>
/// Every threshold here is a counter-example test over the FULL table, never a sample, for the
/// same reason RuleProposals is: a rule inferred from a sample is a guess about rows not yet
/// seen, and the app it generates would reject data its own source contains.
/// </remarks>
public static class EntityRuleProposals
{
    /// <summary>Above this many numeric columns, the Calculation scan's triple search is
    /// skipped for the entity rather than left to grow cubically. Twelve columns is 1,320
    /// candidate triples; fifty would be well over a hundred thousand.</summary>
    private const int MaxNumericColumnsForCalculationScan = 12;

    public static IReadOnlyList<EntityRuleProposal> For(CandidateEntity entity, TabularTable table)
    {
        var proposals = new List<EntityRuleProposal>();

        var dateFields = entity.Fields.Where(f => f.Type == "DateTime").ToList();
        var numericFields = entity.Fields.Where(f => f.Type is "int" or "decimal").ToList();

        AddCrossFieldProposals(proposals, table, dateFields);
        AddCrossFieldProposals(proposals, table, numericFields);

        return proposals;
    }

    private static void AddCrossFieldProposals(
        List<EntityRuleProposal> proposals, TabularTable table, List<CandidateField> fields)
    {
        foreach (var left in fields)
        foreach (var right in fields)
        {
            if (left.Name == right.Name)
                continue;

            if (!HoldsForEveryRow(table, left.Name, right.Name, out var sawStrictlyLess))
                continue;

            if (!sawStrictlyLess)
                continue;

            var rows = table.Rows.Count.ToString("N0");

            proposals.Add(new EntityRuleProposal
            {
                Kind = EntityRuleProposalKind.CrossField,
                Properties = [left.Name, right.Name],
                Rule = new ComparisonRule(
                    left.Name, right.Name, "> 0", $"{left.Name} must not be after {right.Name}"),
                Evidence = $"{left.Name} is always on or before {right.Name}, in all {rows} rows"
            });
        }
    }

    /// <summary>Whether left &lt;= right in every row where both are present and parseable.
    /// <paramref name="sawStrictlyLess"/> guards against two columns that are always equal,
    /// which would otherwise pass in both directions and propose two redundant rules.</summary>
    private static bool HoldsForEveryRow(
        TabularTable table, string leftColumn, string rightColumn, out bool sawStrictlyLess)
    {
        sawStrictlyLess = false;
        var compared = 0;

        for (var row = 0; row < table.Rows.Count; row++)
        {
            if (!TryParseComparable(table.Value(row, leftColumn), out var left)
                || !TryParseComparable(table.Value(row, rightColumn), out var right))
                continue;

            compared++;
            var order = left.CompareTo(right);

            if (order > 0)
                return false;

            if (order < 0)
                sawStrictlyLess = true;
        }

        return compared > 0;
    }

    private static bool TryParseComparable(string? value, out System.IComparable comparable)
    {
        comparable = 0m;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (decimal.TryParse(
                value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            comparable = number;
            return true;
        }

        if (System.DateTime.TryParse(
                value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
        {
            comparable = date;
            return true;
        }

        return false;
    }
}
```

- [ ] **Step 4: Wire the scan into `DataModelBuilder.Build`**

In `DataIngestion/DataModelBuilder.cs`, the existing `Build` method calls `AddProposals(model, sources);` — add a call right after it:

```csharp
        AddProposals(model, sources);
        AddEntityProposals(model, sources);
        RecordNormalization(model, applied);
```

Add the new method beside `AddProposals`:

```csharp
    /// <summary>The Cross-Field and Calculation rules each entity's own rows support, attached
    /// to the entity rather than one field. See <see cref="AddProposals"/> for why this runs
    /// over the complete tables.</summary>
    private static void AddEntityProposals(
        CandidateDomainModel model, IReadOnlyDictionary<string, TabularTable> sources)
    {
        foreach (var entity in model.Entities)
        {
            if (!sources.TryGetValue(entity.Name, out var table))
                continue;

            foreach (var proposal in EntityRuleProposals.For(entity, table))
                entity.Proposals.Add(proposal);
        }
    }
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~DataIngestion.Tests.EntityRuleProposalsTests"`
Expected: 4/4 PASS.

- [ ] **Step 6: Run the focused filter for `DataModelBuilder`**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~DataModelBuilder"`
Expected: all PASS — a model with no matching `sources` entry, or an entity with no date/numeric fields, must be unaffected.

- [ ] **Step 7: Commit**

```bash
git add DataIngestion/EntityRuleProposals.cs DataIngestion/DataModelBuilder.cs DataIngestion.Tests/EntityRuleProposalsTests.cs
git commit -m "$(cat <<'EOF'
Add the Cross-Field scan

Proposes left <= right for every same-typed column pair where it
holds in every row of the complete table, guarding against a
degenerate always-equal pair proposing a vacuous rule.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: The Calculation scan, and `PercentageRule`

**Files:**
- Create: `CodeGenerationEngine/Core/Validation/Rules/PercentageRule.cs`
- Modify: `DataIngestion/EntityRuleProposals.cs`
- Test: `DataIngestion.Tests/EntityRuleProposalsTests.cs` (extend, xUnit)
- Test: `CodeGenerator.Tests/Core/Validation/Rules/PercentageRuleTests.cs` (MSTest — same folder/convention as `ComparisonRuleTests.cs`)

**Interfaces:**
- Produces: `CodeGenerationEngine.Core.Validation.Rules.PercentageRule(string total, string net, string rate)` implementing `IValidationRule`, `Category => Business`, no parser (only ever built by the scan, never typed by a user).
- Extends: `EntityRuleProposals.For` — the Calculation half.

**Why a 16th rule class, when the design spec says "zero changes needed to any of the 15 existing rule classes":** the percentage form is `Total == Net * (1 + Rate)` — an implicit `+1` applied to one operand, which `ArithmeticRule`'s single `target == left OP right` shape cannot express (its `op` is one raw symbol between two raw properties). Adding a NEW class is not a change to any of the fifteen; it is the same kind of addition `EitherOrRule`/`SameAsRule` already are beside `ComparisonRule`. This is a correction the design spec's prose did not anticipate, found by tracing what `ArithmeticRule.Emit` can and cannot represent.

- [ ] **Step 1: Write the failing tests**

`CodeGenerator.Tests/Core/Validation/Rules/PercentageRuleTests.cs`:

```csharp
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class PercentageRuleTests
{
    private sealed class Line { public decimal Total; public decimal Net; public decimal Rate; }

    [TestMethod]
    public void Refuses_when_total_does_not_match_net_times_one_plus_rate()
    {
        var rule = new PercentageRule("Total", "Net", "Rate");
        var ctx = new ValidationEmitContext(new System.Text.StringBuilder(),
            [
                new CodeGenerationEngine.Models.PropertyDefinition { Name = "Total", Type = "decimal" },
                new CodeGenerationEngine.Models.PropertyDefinition { Name = "Net", Type = "decimal" },
                new CodeGenerationEngine.Models.PropertyDefinition { Name = "Rate", Type = "decimal" },
            ]);

        rule.Emit(ctx);

        Assert.AreEqual(ValidationCategory.Business, rule.Category);
        CollectionAssert.AreEqual(new[] { "Total", "Net", "Rate" }, rule.Properties.ToArray());
    }

    [TestMethod]
    public void Skips_with_a_comment_when_a_named_property_is_missing()
    {
        var rule = new PercentageRule("Total", "Net", "Rate");
        var sb = new System.Text.StringBuilder();
        var ctx = new ValidationEmitContext(sb,
            [new CodeGenerationEngine.Models.PropertyDefinition { Name = "Total", Type = "decimal" }]);

        rule.Emit(ctx);

        StringAssert.Contains(sb.ToString(), "business rule skipped");
    }
}
```

Extend `DataIngestion.Tests/EntityRuleProposalsTests.cs` with the Calculation cases:

```csharp
    [Fact]
    public void Proposes_a_sum_calculation_when_it_holds_in_every_row()
    {
        var entity = new CandidateEntity { Name = "OrderLine" };
        entity.Fields.Add(new CandidateField { Name = "LineTotal", Type = "decimal" });
        entity.Fields.Add(new CandidateField { Name = "Goods", Type = "decimal" });
        entity.Fields.Add(new CandidateField { Name = "Tax", Type = "decimal" });

        var table = TableOf("OrderLine", ["LineTotal", "Goods", "Tax"],
        [
            ["120.00", "100.00", "20.00"],
            ["60.00", "50.00", "10.00"],
        ]);

        var proposals = EntityRuleProposals.For(entity, table);

        Assert.Single(proposals.Where(p => p.Kind == EntityRuleProposalKind.Calculation
            && p.Properties.SequenceEqual(new[] { "LineTotal", "Goods", "Tax" })
            && p.Evidence.Contains("plus")));
    }

    [Fact]
    public void Proposes_a_percentage_calculation_when_it_holds_in_every_row()
    {
        var entity = new CandidateEntity { Name = "Invoice" };
        entity.Fields.Add(new CandidateField { Name = "Total", Type = "decimal" });
        entity.Fields.Add(new CandidateField { Name = "Net", Type = "decimal" });
        entity.Fields.Add(new CandidateField { Name = "Rate", Type = "decimal" });

        var table = TableOf("Invoice", ["Total", "Net", "Rate"],
        [
            ["120.00", "100.00", "0.20"],
            ["57.50", "50.00", "0.15"],
        ]);

        var proposals = EntityRuleProposals.For(entity, table);

        Assert.Single(proposals.Where(p => p.Kind == EntityRuleProposalKind.Calculation
            && p.Properties.SequenceEqual(new[] { "Total", "Net", "Rate" })));
    }

    [Fact]
    public void A_calculation_is_not_proposed_when_one_row_breaks_it()
    {
        var entity = new CandidateEntity { Name = "OrderLine" };
        entity.Fields.Add(new CandidateField { Name = "LineTotal", Type = "decimal" });
        entity.Fields.Add(new CandidateField { Name = "Goods", Type = "decimal" });
        entity.Fields.Add(new CandidateField { Name = "Tax", Type = "decimal" });

        var table = TableOf("OrderLine", ["LineTotal", "Goods", "Tax"],
        [
            ["120.00", "100.00", "20.00"],
            ["999.00", "50.00", "10.00"], // breaks every calculation form
        ]);

        var proposals = EntityRuleProposals.For(entity, table);

        Assert.Empty(proposals.Where(p => p.Kind == EntityRuleProposalKind.Calculation));
    }

    [Fact]
    public void More_than_the_cap_of_numeric_columns_skips_the_calculation_scan()
    {
        var entity = new CandidateEntity { Name = "Wide" };
        var columns = new List<string>();
        for (var i = 0; i < 14; i++)
        {
            entity.Fields.Add(new CandidateField { Name = $"N{i}", Type = "decimal" });
            columns.Add($"N{i}");
        }

        var table = TableOf("Wide", columns.ToArray(), [columns.Select(_ => "1.00").ToArray()]);

        var proposals = EntityRuleProposals.For(entity, table);

        Assert.Empty(proposals.Where(p => p.Kind == EntityRuleProposalKind.Calculation));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~PercentageRuleTests|FullyQualifiedName~DataIngestion.Tests.EntityRuleProposalsTests"`
Expected: `PercentageRuleTests` fails to compile (`PercentageRule` does not exist); the new `EntityRuleProposalsTests` cases fail (no Calculation proposals produced yet).

- [ ] **Step 3: Add `PercentageRule`**

Create `CodeGenerationEngine/Core/Validation/Rules/PercentageRule.cs`:

```csharp
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerationEngine.Core.Validation.Rules;

/// <summary>Business rule: one amount always equals another plus a percentage of it —
/// `Total == Net * (1 + Rate)`. Only ever built by the Calculation scan; nobody types this
/// rule by hand, so there is no parser.</summary>
public sealed class PercentageRule : IValidationRule
{
    private readonly string _total;
    private readonly string _net;
    private readonly string _rate;

    public PercentageRule(string total, string net, string rate)
    {
        _total = total;
        _net = net;
        _rate = rate;
    }

    public ValidationCategory Category => ValidationCategory.Business;
    public IReadOnlyList<string> Properties => [_total, _net, _rate];
    public string Describe() => $"{_total} must equal {_net} times (1 plus {_rate})";

    public void Emit(ValidationEmitContext ctx)
    {
        if (!ctx.HasProperty(_total) || !ctx.HasProperty(_net) || !ctx.HasProperty(_rate))
        {
            ctx.Line($"// business rule skipped: '{Describe()}' references an unknown property");
            return;
        }

        var message = SymbolDisplay.FormatLiteral(Describe() + ".", quote: true);
        const string Parse = "System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture";

        ctx.Line($"if (decimal.TryParse(((object)this.{_total})?.ToString(), {Parse}, out var __t)");
        ctx.Line($"    && decimal.TryParse(((object)this.{_net})?.ToString(), {Parse}, out var __n)");
        ctx.Line($"    && decimal.TryParse(((object)this.{_rate})?.ToString(), {Parse}, out var __r))");
        ctx.Line("{");
        ctx.Line($"    if (System.Math.Abs(__t - (__n * (1 + __r))) > 0.0001m) errors.Add({message});");
        ctx.Line("}");
    }
}
```

- [ ] **Step 4: Add the Calculation half of the scan**

In `DataIngestion/EntityRuleProposals.cs`, add `using CodeGenerationEngine.Core.Validation;` at the top, change `For` to also call the new pass:

```csharp
    public static IReadOnlyList<EntityRuleProposal> For(CandidateEntity entity, TabularTable table)
    {
        var proposals = new List<EntityRuleProposal>();

        var dateFields = entity.Fields.Where(f => f.Type == "DateTime").ToList();
        var numericFields = entity.Fields.Where(f => f.Type is "int" or "decimal").ToList();

        AddCrossFieldProposals(proposals, table, dateFields);
        AddCrossFieldProposals(proposals, table, numericFields);

        if (numericFields.Count <= MaxNumericColumnsForCalculationScan)
            AddCalculationProposals(proposals, table, numericFields);

        return proposals;
    }
```

Add the new methods:

```csharp
    private static void AddCalculationProposals(
        List<EntityRuleProposal> proposals, TabularTable table, List<CandidateField> numericFields)
    {
        foreach (var target in numericFields)
        {
            var others = numericFields.Where(f => f.Name != target.Name).ToList();

            for (var i = 0; i < others.Count; i++)
            for (var j = i + 1; j < others.Count; j++)
            {
                TryProposeSumOrProduct(proposals, table, target, others[i], others[j]);
                TryProposePercentage(proposals, table, target, others[i], others[j]);
                TryProposePercentage(proposals, table, target, others[j], others[i]);
            }
        }
    }

    private static void TryProposeSumOrProduct(
        List<EntityRuleProposal> proposals, TabularTable table,
        CandidateField target, CandidateField left, CandidateField right)
    {
        if (ZeroCounterExamples(table, target.Name, left.Name, right.Name, (l, r) => l + r, out var sumRows))
            proposals.Add(new EntityRuleProposal
            {
                Kind = EntityRuleProposalKind.Calculation,
                Properties = [target.Name, left.Name, right.Name],
                Rule = new ArithmeticRule(target.Name, left.Name, "plus", right.Name),
                Evidence = $"{target.Name} always equals {left.Name} plus {right.Name}, in all {sumRows:N0} rows"
            });

        if (ZeroCounterExamples(table, target.Name, left.Name, right.Name, (l, r) => l * r, out var productRows))
            proposals.Add(new EntityRuleProposal
            {
                Kind = EntityRuleProposalKind.Calculation,
                Properties = [target.Name, left.Name, right.Name],
                Rule = new ArithmeticRule(target.Name, left.Name, "times", right.Name),
                Evidence = $"{target.Name} always equals {left.Name} times {right.Name}, in all {productRows:N0} rows"
            });
    }

    private static void TryProposePercentage(
        List<EntityRuleProposal> proposals, TabularTable table,
        CandidateField target, CandidateField net, CandidateField rate)
    {
        if (!ZeroCounterExamples(table, target.Name, net.Name, rate.Name, (n, r) => n * (1 + r), out var rows))
            return;

        proposals.Add(new EntityRuleProposal
        {
            Kind = EntityRuleProposalKind.Calculation,
            Properties = [target.Name, net.Name, rate.Name],
            Rule = new PercentageRule(target.Name, net.Name, rate.Name),
            Evidence = $"{target.Name} always equals {net.Name} times (1 plus {rate.Name}), in all {rows:N0} rows"
        });
    }

    private static bool ZeroCounterExamples(
        TabularTable table, string targetColumn, string leftColumn, string rightColumn,
        System.Func<decimal, decimal, decimal> combine, out int rowsCompared)
    {
        rowsCompared = 0;

        for (var row = 0; row < table.Rows.Count; row++)
        {
            if (!TryParseDecimal(table.Value(row, targetColumn), out var t)
                || !TryParseDecimal(table.Value(row, leftColumn), out var l)
                || !TryParseDecimal(table.Value(row, rightColumn), out var r))
                continue;

            rowsCompared++;

            if (System.Math.Abs(t - combine(l, r)) > 0.0001m)
                return false;
        }

        return rowsCompared > 0;
    }

    private static bool TryParseDecimal(string? value, out decimal result)
    {
        result = 0m;
        return !string.IsNullOrWhiteSpace(value)
            && decimal.TryParse(
                value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out result);
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~PercentageRuleTests|FullyQualifiedName~DataIngestion.Tests.EntityRuleProposalsTests"`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerationEngine/Core/Validation/Rules/PercentageRule.cs DataIngestion/EntityRuleProposals.cs DataIngestion.Tests/EntityRuleProposalsTests.cs CodeGenerator.Tests/Core/Validation/Rules/PercentageRuleTests.cs
git commit -m "$(cat <<'EOF'
Add the Calculation scan and PercentageRule

Sum and product forms reuse ArithmeticRule unchanged; the percentage
form (Total = Net * (1 + Rate)) needs a new 16th rule class, since
ArithmeticRule's single target==left-op-right shape cannot express
the implicit +1. Capped at 12 numeric columns per entity.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: An accepted proposal reaches the Business stage — requirements 9 and 14 delivered

**Files:**
- Test: `CodeGenerator.Tests/App/AcceptedEntityProposalReachesValidatorTests.cs`

**Interfaces:**
- Consumes: `DataIngestion.DataModelBuilder.Build` (task 4), `DomainModelMapper.ToClassDefinitions` (task 3), `ValidatorTemplate.Generated` (task 1) — no new production code; this task proves the three already wired together.

**Why this task adds no production code:** by the end of task 5, `DataModelBuilder.Build` already populates `entity.Proposals`, and `DomainModelMapper.AddEntityValidation` (task 3) already folds `entity.Proposals.Where(p => p.Accepted)` into `ClassDefinition.BusinessRules` — the same list `ValidatorTemplate.Generated` already reads. Nothing between tasks 3 and 5 needs a second wire-up; this task is the proof, matching phase 3's own precedent (its task 11 proved the accepted-proposal path end to end, not just at each layer separately).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using DataIngestion;
using DataIngestion.Schema;
using DomainDiscovery.Model;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The full path an accepted Cross-Field proposal travels: DataModelBuilder finds it,
/// DomainModelMapper carries it once accepted, ValidatorTemplate emits it, and the compiled
/// validator actually refuses a record that breaks it.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AcceptedEntityProposalReachesValidatorTests
{
    [TestInitialize]
    public void Setup() => ValidationRegistry.Clear();

    [TestCleanup]
    public void Cleanup() => ValidationRegistry.Clear();

    [TestMethod]
    public void An_accepted_cross_field_proposal_refuses_a_record_that_breaks_it()
    {
        var table = new TabularTable { Name = "Booking" };
        table.Columns.Add(new TabularColumn { Name = "StartDate" });
        table.Columns.Add(new TabularColumn { Name = "EndDate" });
        table.Rows.Add(["2026-01-01", "2026-01-10"]);
        table.Rows.Add(["2026-02-01", "2026-02-20"]);

        var model = DataModelBuilder.Build([table], []);
        var booking = model.Entities.Single(e => e.Name == "Booking");

        var proposal = booking.Proposals.Single(p => p.Kind == EntityRuleProposalKind.CrossField);
        proposal.Accepted = true;

        var classes = DomainModelMapper.ToClassDefinitions(model);
        var definition = classes.Single(c => c.Name == "Booking");

        Assert.HasCount(1, definition.BusinessRules);

        var source = ValidatorTemplate.Generated(
            "Acme", definition, [], referencesRequired: false, businessRules: definition.BusinessRules);

        StringAssert.Contains(source, "private static void Business(");
        StringAssert.Contains(source, "        Business,");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails for the right reason**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.App.AcceptedEntityProposalReachesValidatorTests"`
Expected: if tasks 1-5 landed correctly, this should already PASS. If it fails, the failure must be diagnosed against tasks 1-5's own code, not "fixed" by adding new logic here — that would mean an earlier task's wiring is incomplete, and the fix belongs in that task's file, not this one.

- [ ] **Step 3: Commit** (assuming step 2 passed as expected — this task adds a test file only)

```bash
git add CodeGenerator.Tests/App/AcceptedEntityProposalReachesValidatorTests.cs
git commit -m "$(cat <<'EOF'
Prove an accepted proposal reaches the Business stage end to end

DataModelBuilder -> DomainModelMapper -> ValidatorTemplate needed no
further wiring: tasks 3 and 4-5 already meet at ClassDefinition
.BusinessRules. Requirements 9 and 14 delivered.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: The console review card lists entity-scoped proposals

**Files:**
- Modify: `CodeGenerator/App/ProposalReport.cs`
- Modify: `CodeGenerator.Tests/App/ProposalReportTests.cs`

**Interfaces:**
- Produces: `ProposalReport.All` now flattens BOTH `field.Proposals` (existing) and `entity.Proposals` (new) into the same numbered sequence; `Lines`/`Accept`/`Summary` are unchanged in signature, since both call `All` internally.

**The label problem this solves:** `All`'s current tuple is `(string Entity, string Field, RuleProposal Proposal)` — a per-FIELD shape. An entity-scoped proposal has no single field, so `All` needs its `Field`/`Proposal` slots to accept either shape. The smallest change: widen the tuple's `Field` to the label already wanted on screen (`"StartDate, EndDate"` for a Cross-Field proposal) and its `Proposal` to whichever object's `.Accepted` is being read/set — both `RuleProposal` and `EntityRuleProposal` expose a settable `bool Accepted`, so a tiny local wrapper interface removes the need to branch in `Lines`/`Accept`.

- [ ] **Step 1: Write the failing test**

Add to `CodeGenerator.Tests/App/ProposalReportTests.cs` (same file, alongside `ModelWithTwoProposals`):

```csharp
    /// <summary>An Order entity with one field proposal and one entity-scoped proposal.</summary>
    private static CandidateDomainModel ModelWithAnEntityScopedProposal()
    {
        var model = new CandidateDomainModel();
        var booking = new CandidateEntity { Name = "Booking" };
        booking.Fields.Add(new CandidateField { Name = "StartDate", Type = "DateTime" });
        booking.Fields.Add(new CandidateField { Name = "EndDate", Type = "DateTime" });

        booking.Proposals.Add(new EntityRuleProposal
        {
            Kind = EntityRuleProposalKind.CrossField,
            Properties = ["StartDate", "EndDate"],
            Rule = new CodeGenerationEngine.Core.Validation.Rules.ComparisonRule(
                "StartDate", "EndDate", "> 0", "StartDate must not be after EndDate"),
            Evidence = "StartDate is always on or before EndDate, in all 40 rows"
        });

        model.Entities.Add(booking);
        return model;
    }

    [TestMethod]
    public void An_entity_scoped_proposal_is_numbered_alongside_field_proposals()
    {
        var model = ModelWithAnEntityScopedProposal();

        var lines = ProposalReport.Lines(model);

        Assert.IsTrue(lines.Any(l => l.Contains("1.") && l.Contains("Booking")
            && l.Contains("StartDate is always on or before EndDate")));
    }

    [TestMethod]
    public void Accepting_an_entity_scoped_proposal_by_number_takes_it()
    {
        var model = ModelWithAnEntityScopedProposal();

        var result = ProposalReport.Accept(model, "1");

        Assert.IsTrue(model.Entities.Single().Proposals[0].Accepted);
        StringAssert.Contains(result, "Accepted");
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.App.ProposalReportTests"`
Expected: the two new tests FAIL (`All` does not yet see `entity.Proposals`); existing tests still PASS.

- [ ] **Step 3: Widen `ProposalReport.All`**

In `CodeGenerator/App/ProposalReport.cs`, add `using CodeGenerationEngine.Models;` if not already present, and replace the private `All` method:

```csharp
    /// <summary>Something every proposal shares, whatever it is scoped to.</summary>
    private interface IAcceptable
    {
        string Evidence { get; }
        bool Accepted { get; set; }
    }

    private sealed class FieldProposal(RuleProposal proposal) : IAcceptable
    {
        public RuleProposal Inner { get; } = proposal;
        public string Evidence => Inner.Evidence;
        public bool Accepted { get => Inner.Accepted; set => Inner.Accepted = value; }
    }

    private sealed class EntityProposal(EntityRuleProposal proposal) : IAcceptable
    {
        public EntityRuleProposal Inner { get; } = proposal;
        public string Evidence => Inner.Evidence;
        public bool Accepted { get => Inner.Accepted; set => Inner.Accepted = value; }
    }

    private static List<(string Entity, string Label, IAcceptable Proposal)> All(
        CandidateDomainModel model)
    {
        var all = new List<(string, string, IAcceptable)>();

        foreach (var entity in model.Entities)
        {
            foreach (var field in entity.Fields)
                foreach (var proposal in field.Proposals)
                    all.Add((entity.Name, field.Name, new FieldProposal(proposal)));

            foreach (var proposal in entity.Proposals)
                all.Add((entity.Name, string.Join(", ", proposal.Properties), new EntityProposal(proposal)));
        }

        return all;
    }
```

Update `Summary`, `Lines` and `Accept` to read `entry.Proposal.Accepted`/`entry.Proposal.Evidence` and `entry.Label` in place of the old `Field`/`Proposal.Accepted`/`Proposal.Evidence`:

```csharp
    public static string? Summary(CandidateDomainModel model)
    {
        var count = All(model).Count;

        if (count == 0)
            return null;

        var accepted = All(model).Count(p => p.Proposal.Accepted);

        return $"  {count} rule(s) proposed from your data, {accepted} accepted — "
             + "type `rules` to see them.";
    }

    public static IReadOnlyList<string> Lines(CandidateDomainModel model)
    {
        var lines = new List<string>();
        var all = All(model);

        if (all.Count == 0)
            return ["  Nothing was proposed from your data."];

        lines.Add("  Rules your data supports. None is applied until you accept it.");

        for (var i = 0; i < all.Count; i++)
        {
            var (entity, label, proposal) = all[i];

            lines.Add(
                $"  {i + 1}. [{(proposal.Accepted ? "x" : " ")}] {entity}.{label} — "
                + $"{proposal.Evidence}");
        }

        lines.Add("  accept <n>   accept all");

        return lines;
    }

    public static string Accept(CandidateDomainModel model, string argument)
    {
        var all = All(model);

        if (string.Equals(argument, "all", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var entry in all)
                entry.Proposal.Accepted = true;

            return $"Accepted {all.Count} rule(s).";
        }

        if (!int.TryParse(argument, out var number) || number < 1 || number > all.Count)
            return $"There is no proposal {argument}. Type `rules` to see them.";

        var picked = all[number - 1];
        picked.Proposal.Accepted = true;

        return $"Accepted: {picked.Entity}.{picked.Label}.";
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.App.ProposalReportTests"`
Expected: all PASS, including every pre-existing test (the field-scoped path must read identically to before).

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/App/ProposalReport.cs CodeGenerator.Tests/App/ProposalReportTests.cs
git commit -m "$(cat <<'EOF'
List entity-scoped proposals in the console review card

All() now flattens both field.Proposals and entity.Proposals into
one numbered sequence via a small IAcceptable wrapper, so accept <n>
and rules need no branching by proposal shape.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: The Blazor review page shows entity-scoped proposals

**Files:**
- Modify: `CodeGenerator/Web/Components/Stages/ReviewStage.razor`
- Modify: `CodeGenerator.Tests/Web/ReviewStageTests.cs`

**Interfaces:**
- Consumes: `BuildSession.ModelEdited()` (pre-existing, called on every decision, per `RuleProposal`'s own `Decide` handler).
- Produces: a new markup block reusing the existing `.proposal-row`/`.proposal` CSS classes (asserted elsewhere by bUnit — reuse the class names, never rename them), and an overload of `Decide` for `EntityRuleProposal`.

- [ ] **Step 1: Write the failing test**

Add to `CodeGenerator.Tests/Web/ReviewStageTests.cs`:

```csharp
    private static CandidateDomainModel AModelWithAnEntityProposal()
    {
        var model = AModel();
        var booking = new CandidateEntity { Name = "Booking" };
        booking.Fields.Add(new CandidateField { Name = "StartDate", Type = "DateTime" });
        booking.Fields.Add(new CandidateField { Name = "EndDate", Type = "DateTime" });
        booking.Proposals.Add(new EntityRuleProposal
        {
            Kind = EntityRuleProposalKind.CrossField,
            Properties = ["StartDate", "EndDate"],
            Rule = new CodeGenerationEngine.Core.Validation.Rules.ComparisonRule(
                "StartDate", "EndDate", "> 0", "StartDate must not be after EndDate"),
            Evidence = "StartDate is always on or before EndDate, in all 40 rows"
        });
        model.Entities.Add(booking);
        return model;
    }

    [TestMethod]
    public void Shows_an_entity_scoped_proposal_as_an_unchecked_box()
    {
        using var context = new BunitContext();
        var session = SessionWith(AModelWithAnEntityProposal());
        var rendered = context.Render<ReviewStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        StringAssert.Contains(rendered.Markup, "StartDate is always on or before EndDate");
        var checkbox = rendered.Find("[data-entity=Booking] .entity-proposals input.proposal");
        Assert.IsFalse(checkbox.HasAttribute("checked"));
    }

    [TestMethod]
    public void Checking_an_entity_scoped_proposal_accepts_it()
    {
        using var context = new BunitContext();
        var model = AModelWithAnEntityProposal();
        var session = SessionWith(model);
        var rendered = context.Render<ReviewStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("[data-entity=Booking] .entity-proposals input.proposal").Change(true);

        Assert.IsTrue(model.Entities.Single(e => e.Name == "Booking").Proposals[0].Accepted);
    }
```

(These reuse the file's existing `SessionWith`/`AModel` helpers already in scope.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.Web.ReviewStageTests"`
Expected: the two new tests FAIL (no `.entity-proposals` block rendered yet); existing tests still PASS.

- [ ] **Step 3: Add the markup block and `Decide` overload**

In `CodeGenerator/Web/Components/Stages/ReviewStage.razor`, immediately after the closing `</ul>` of the `fields` list and before the `add-field-row` div (still inside the entity's `<article>`), add:

```razor
                <ul class="entity-proposals">
                    @foreach (var proposal in entity.Proposals.ToList())
                    {
                        <li>
                            <label class="proposal-row">
                                <input type="checkbox" class="proposal"
                                       checked="@proposal.Accepted"
                                       @onchange="e => Decide(proposal, e.Value)" />
                                <span class="proposal-evidence">@proposal.Evidence</span>
                            </label>
                        </li>
                    }
                </ul>
```

Add the overload in the code-behind block, beside the existing `Decide`:

```csharp
    /// <summary>Takes or drops one proposed entity-scoped rule. Mirrors <see cref="Decide(RuleProposal, object?)"/>
    /// exactly — see its own remarks.</summary>
    private void Decide(EntityRuleProposal proposal, object? value)
    {
        proposal.Accepted = value is true || (value is string text && bool.TryParse(text, out var on) && on);
        Session.ModelEdited();
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.Web.ReviewStageTests"`
Expected: all PASS, including every pre-existing test in the file.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/Web/Components/Stages/ReviewStage.razor CodeGenerator.Tests/Web/ReviewStageTests.cs
git commit -m "$(cat <<'EOF'
Show entity-scoped proposals on the Blazor review page

Reuses the existing .proposal-row/.proposal CSS classes bUnit
already asserts elsewhere; Decide gains an EntityRuleProposal
overload mirroring the field-scoped one exactly.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 9: Run a real generated app

**Files:** none (verification only — no production code expected to change; any defect found here is fixed in the file it lives in, then this task's own notes updated).
- Create: `docs/superpowers/plans/2026-09-17-validation-phase-4-run-notes.md`

This is phase 3's own hard lesson, applied again: four of five real defects on that branch were invisible to a 4,000+ test suite and were found only by running a generated app. This task is not optional and is not satisfied by a green suite.

- [ ] **Step 1: Full solution suite**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror`, then `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --no-build`
Expected: 0 warnings/errors on build; every test passes. Record the exact pass count in the run-notes file.

- [ ] **Step 2: Generate a SQL-backed app with genuine cross-field and arithmetic data**

Using the same small-purpose-built-database approach phase 3's task 15 used (a scratch SQL Server database, or a CSV/JSON source — whichever is fastest to seed with real Booking-like `StartDate`/`EndDate` rows and Invoice-like `Total`/`Net`/`Rate` rows that genuinely satisfy both a Cross-Field and a Calculation relationship in every row): drive the CLI (piped stdin, per the established non-interactive recipe) through `build app:` for a login-enabled Blazor and WPF target, reviewing the proposals, accepting at least one Cross-Field and one Calculation proposal, and also typing `add business validation <rule> in <Class>` for a hand-stated rule on a THIRD field pair before `build app` — proving both halves of job 1/2 in the same run.

- [ ] **Step 3: Live click-through, at least one desktop target and Blazor**

Sign in, attempt to save/insert a record that breaks the accepted Cross-Field rule (e.g., an EndDate before StartDate) and confirm the app refuses with the expected message; attempt one that breaks the accepted Calculation rule; attempt one that breaks the hand-stated business rule; then save a record that satisfies all three and confirm it succeeds. Repeat on the second target. Use the same headless-Chromium (`patchright`, `channel: 'chromium'`) or UIA-harness approach phase 3 used, per which target is being driven.

- [ ] **Step 4: Write `run-notes.md`**

Record: exact suite numbers, the exact source data and rules exercised, each of the six checks (three refusals + three or more successes) and its outcome, any defect found and where it was fixed (in the task file it belongs to, not here), and confirmation that both a hand-stated and an accepted-proposal rule reached the same generated app in the same run.

- [ ] **Step 5: Commit**

```bash
git add docs/superpowers/plans/2026-09-17-validation-phase-4-run-notes.md
git commit -m "$(cat <<'EOF'
Run notes: a real generated app enforces phase 4's business rules

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Finishing

Once task 9 is committed and the full suite is green: push the branch, open a PR against `main`, merge once CI comes back green (this branch's standing instruction across phases 2 and 3).

## Spec corrections this plan makes

1. **The adapter is not "run Emit unchanged."** `ComparisonRule`/`ArithmeticRule`/`EitherOrRule`/`RequiredWhenRule`/`SameAsRule.Emit` all hardcode a literal `this.{Property}` (assuming an instance method) and a literal local named `errors` of type `List<string>` — both artifacts of the OLDER `generate validator` command's shape. The new `Business` stage is `static`, so its own `List<ValidationError>` parameter is renamed `pipelineErrors`, and the accumulated emitted text has `this.` rewritten to `item.` before being spliced in. No rule class changes; the adapter carries both fixes. (Task 1.)
2. **The registry-merge helper is one call site inside `DomainModelMapper.ToClassDefinitions`, not two call sites in `AppScaffolder.cs` and `IncrementalScaffolder.cs`.** `IncrementalScaffolder.Add` never calls `ValidatorTemplate.Generated` directly — it delegates its whole build to a fresh `AppScaffolder().Scaffold(...)`, which already goes through `DomainModelMapper.ToClassDefinitions`. Confirmed by reading `IncrementalScaffolder.cs` directly: the only reason it needs no change is that it never had a second entry point to begin with. (Task 3.)
3. **The Calculation scan's percentage form needs a new, 16th rule class (`PercentageRule`), not a reuse of `ArithmeticRule`.** `ArithmeticRule`'s `target == left OP right` shape has no way to express the implicit `+1` in `Total == Net * (1 + Rate)`. This does not conflict with the design spec's "zero changes to any of the 15 existing rule classes" — it is an addition, the same way `EitherOrRule`/`SameAsRule` were additions beside `ComparisonRule`. (Task 5.)
