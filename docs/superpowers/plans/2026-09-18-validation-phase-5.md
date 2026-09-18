# Validation Phase 5 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a confirmed status column's discovered values be ordered and marked final on the Review card, then enforce two rules in a generated app's validator — a move must go forward in that order, and a record at a final status may not move again.

**Architecture:** A new conditional `Workflow` stage joins `Security`/`Field`/`Entity`/`Business` in the emitted validator's pipeline, built the same way `Business` was in phase 4 — a `{{workflowStage}}` string spliced in after `{{businessStage}}`, gated on non-empty content. Unlike every other stage, its rule needs TWO records (`context.Item` and `context.Stored`, both already available on every stage method's `context` parameter) rather than one. It is fed by a new 17th rule, `WorkflowRule`, built only by `DomainModelMapper` from an already-accepted `Status`-kind `RuleProposal` once the user has stated an order for it via two new review-loop commands (`order status`/`final status`) or the Blazor review page's new transition editor.

**Tech Stack:** C# / .NET 10, MSTest (`CodeGenerator.Tests`), Roslyn (`Compiler.CompileSources`) for compile-and-invoke tests, Blazor Server (bUnit) for the review page.

**Spec:** `docs/superpowers/specs/2026-09-18-validation-phase-5-design.md` (parent: `docs/superpowers/specs/2026-09-06-validation-architecture-design.md`; most recent sibling: `docs/superpowers/specs/2026-09-17-validation-phase-4-design.md`)

**Branch:** `validation-phase-5`, already created, spec committed on it (`9c30d32`).

## Global Constraints

- **CI builds `SmartCodeGenerator/SmartCodeGenerator.slnx` in Release with `--no-incremental -warnaserror`, then `dotnet test --no-build`.** Before declaring a task done, run:
  `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror`
- **Repo root is `D:\SmartCodeGenerator-Repository`; the solution is one level down in `SmartCodeGenerator/`.** All paths in this plan are given from the repo root.
- **`CodeGenerator.Tests` and `DomainDiscovery.Tests` are MSTest** (`[TestClass]`/`[TestMethod]`, `Assert.AreEqual` etc). **`DataIngestion.Tests` is xUnit** (`[Fact]`/`[Theory]`, `Assert.Equal(expected, actual)` — argument order is the OPPOSITE of MSTest's `Assert.AreEqual`). This plan touches no `DataIngestion` file (the Status scan already exists from phase 3), so every test in this plan is MSTest.
- **`[DataTestMethod]` is MSTEST0044** (error under `-warnaserror`). Use `[TestMethod]` with `[DataRow]`.
- **`Assert.AreEqual(n, x.Count)` is MSTEST0037.** Use `Assert.HasCount(n, x)`.
- **xUnit's own analyzer (xunit.analyzers) rejects `Assert.Single(x.Where(...))`/`Assert.Empty(x.Where(...))` under `-warnaserror`** (xUnit2031/xUnit2029) — not relevant to this plan's MSTest-only tests, noted only because a prior phase hit it and the pattern (prefer a predicate-overload) generalizes if any test here ends up needing a filtered collection assertion.
- **Emitted C# is tested by COMPILING it**, with `Compiler.CompileSources` over the real files in their real namespaces — never a substring check standing in for a compile.
- **Red before green.** Every task writes the failing test first and runs it to see it fail for the stated reason.
- **The checkout is CRLF for EVERY text file, including `docs/superpowers/*.md` — there is no separate LF convention for docs.** (A prior session wrongly believed docs were LF-only; verified false this session by reading three actual docs files byte-for-byte, all CRLF.) The Write tool produces LF-only files — check byte-mode CR/LF counts after creating or editing any file and convert if needed (`\n` → `\r\n`, only after confirming zero pre-existing `\r` bytes). Expect `git status` to still call a byte-converted file "modified" with an EMPTY `git diff` afterward — `.gitattributes`' `text=auto` normalizes both versions to the same LF blob, so an empty diff means the fix worked, not that nothing happened; there is nothing further to commit in that case.
- Commit after every task. Attribution line on every commit:
  `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`

---

## File Structure

**New files**

| File | Responsibility |
|---|---|
| `CodeGenerationEngine/Core/Validation/Rules/WorkflowRule.cs` | The 17th rule (Workflow category, no parser — only ever built by `DomainModelMapper` from an ordered Status proposal): forward-only order, no move from a final value. |
| `CodeGenerator.Tests/Core/Validation/Rules/WorkflowRuleTests.cs` | Task 1: `WorkflowRule.Emit` writes the real transition checks. |
| `CodeGenerator.Tests/App/WorkflowStageEmissionTests.cs` | Task 4: the Workflow stage compiles and runs, given a hand-built rule list. |
| `CodeGenerator.Tests/App/WorkflowRulesReachAppTests.cs` | Task 3: an ordered, accepted Status proposal reaches `ClassDefinition.WorkflowRules` and the emitted validator. |
| `CodeGenerator.Tests/App/AcceptedWorkflowReachesValidatorTests.cs` | Task 5: the accepted-proposal integration proof, end to end. |
| `CodeGenerator.Tests/App/WorkflowCommandsTests.cs` | Task 6: `order status`/`final status` in the review loop. |

**Modified files**

| File | Change |
|---|---|
| `CodeGenerationEngine/Core/Validation/ValidationCategory.cs` | Gains `Workflow` beside `Data`/`Business`. |
| `CodeGenerationEngine/Models/RuleProposal.cs` | Gains `Order`/`FinalValues`, both optional, meaningful only for an accepted `Status`-kind proposal. |
| `CodeGenerationEngine/Models/ClassDefinition.cs` | Gains `WorkflowRules : List<IValidationRule>`. |
| `AppScaffolding/DomainModelMapper.cs` | New `AddWorkflowValidation` — builds a `WorkflowRule` per ordered, accepted Status proposal. |
| `AppScaffolding/ValidatorTemplate.cs` | `Generated(...)` gains a `workflowRules` parameter and emits the conditional `Workflow` stage. |
| `AppScaffolding/AppScaffolder.cs` | Pass `definition.WorkflowRules` into `ValidatorTemplate.Generated`. |
| `CodeGenerator/App/BuildAppCommand.cs` | Two new review-loop switch cases: `order`/`final`. |
| `CodeGenerator/Web/Components/Stages/ReviewStage.razor` | A `.workflow-order` block per accepted Status proposal, beside the existing `.entity-proposals` block. |
| `CodeGenerator.Tests/Web/ReviewStageTests.cs` | Two new tests for the transition editor. |

---

## Task order and why

Tasks 1-3 build the rule and its data model in isolation (task 1: the rule class and its `Emit`, no wiring; task 2: `RuleProposal`'s two new properties, nothing populates them from a command yet; task 3: `DomainModelMapper` wiring, which needs task 2's properties to read and task 1's rule to build). Task 4 builds the `Workflow` stage's emission mechanics with a hand-built rule list, independent of tasks 2-3 — mirrors how phase 4 sequenced its own Business-stage task before its registry-wiring task. Task 5 proves tasks 1-4 meet end to end, the same proof-only shape as phase 4's task 6. Tasks 6-7 add the two ways a user actually sets `Order`/`FinalValues` — console and Blazor. Task 8 runs a real generated app, which every prior phase's own history says is the only step that finds what the other seven miss, and which the spec explicitly requires be budgeted, not skipped in advance.

---

### Task 1: `WorkflowRule`, the 17th rule

**Files:**
- Create: `CodeGenerationEngine/Core/Validation/Rules/WorkflowRule.cs`
- Modify: `CodeGenerationEngine/Core/Validation/ValidationCategory.cs`
- Test: `CodeGenerator.Tests/Core/Validation/Rules/WorkflowRuleTests.cs`

**Interfaces:**
- Consumes: `CodeGenerationEngine.Core.Validation.IValidationRule` (`Category`, `Properties`, `Describe()`, `Emit(ValidationEmitContext)`), `ValidationEmitContext(StringBuilder, IReadOnlyList<PropertyDefinition>)` — both pre-existing, unmodified.
- Produces: `CodeGenerationEngine.Core.Validation.ValidationCategory` gains `Workflow` as a third value. `CodeGenerationEngine.Core.Validation.Rules.WorkflowRule(string property, IReadOnlyList<string> order, IReadOnlySet<string> finalValues)` implementing `IValidationRule`, `Category => Workflow`.

**Why `Emit` cannot follow the other 16 rules' shape:** every existing rule's `Emit` writes a check against a SINGLE record (`this.Property`, rewritten to `item.Property` for the Business stage). `WorkflowRule` must compare the CURRENT value (`item.{property}`) against the STORED one (`stored.{property}`) — a second record no other rule has ever needed. `Emit` therefore writes a fully self-contained block referencing both names directly, rather than using the generic single-record shape. Both names are already in scope in whatever stage method calls it (task 4 makes sure of that for `Workflow`).

- [ ] **Step 1: Write the failing tests**

```csharp
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class WorkflowRuleTests
{
    private static ValidationEmitContext ContextFor(string property) =>
        new(new System.Text.StringBuilder(),
            [new PropertyDefinition { Name = property, Type = "string" }]);

    [TestMethod]
    public void Category_is_Workflow()
    {
        var rule = new WorkflowRule("Status", ["Draft", "Approved", "Shipped"], new HashSet<string>());

        Assert.AreEqual(ValidationCategory.Workflow, rule.Category);
    }

    [TestMethod]
    public void Properties_names_the_one_status_property()
    {
        var rule = new WorkflowRule("Status", ["Draft", "Approved", "Shipped"], new HashSet<string>());

        CollectionAssert.AreEqual(new[] { "Status" }, rule.Properties.ToArray());
    }

    [TestMethod]
    public void Emit_writes_a_backward_move_check_naming_both_values()
    {
        var rule = new WorkflowRule("Status", ["Draft", "Approved", "Shipped"], new HashSet<string>());
        var ctx = ContextFor("Status");

        rule.Emit(ctx);
        var code = ctx.Source.ToString();

        StringAssert.Contains(code, "stored.Status");
        StringAssert.Contains(code, "item.Status");
        StringAssert.Contains(code, "\"Draft\"");
        StringAssert.Contains(code, "\"Approved\"");
        StringAssert.Contains(code, "\"Shipped\"");
    }

    [TestMethod]
    public void Emit_writes_a_final_status_check_naming_the_final_value()
    {
        var rule = new WorkflowRule("Status", ["Draft", "Approved", "Cancelled"], new HashSet<string> { "Cancelled" });
        var ctx = ContextFor("Status");

        rule.Emit(ctx);
        var code = ctx.Source.ToString();

        StringAssert.Contains(code, "\"Cancelled\"");
    }

    [TestMethod]
    public void Emit_skips_with_a_comment_when_the_property_is_missing()
    {
        var rule = new WorkflowRule("Status", ["Draft", "Approved"], new HashSet<string>());
        var ctx = ContextFor("SomethingElse");

        rule.Emit(ctx);

        StringAssert.Contains(ctx.Source.ToString(), "workflow rule skipped");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.Core.Validation.Rules.WorkflowRuleTests"`
Expected: compile error — `WorkflowRule` and `ValidationCategory.Workflow` do not exist yet.

- [ ] **Step 3: Add `ValidationCategory.Workflow`**

In `CodeGenerationEngine/Core/Validation/ValidationCategory.cs`, change:

```csharp
namespace CodeGenerationEngine.Core.Validation;

public enum ValidationCategory { Data, Business, Workflow }
```

- [ ] **Step 4: Add `WorkflowRule`**

Create `CodeGenerationEngine/Core/Validation/Rules/WorkflowRule.cs`:

```csharp
using System.Linq;

namespace CodeGenerationEngine.Core.Validation.Rules;

/// <summary>
/// Business rule: a status column may only move forward through a stated order, and never away
/// from a value marked final. Only ever built by <c>DomainModelMapper</c> from an accepted,
/// ordered Status proposal — nobody types this rule by hand, so there is no parser.
/// </summary>
public sealed class WorkflowRule : IValidationRule
{
    private readonly string _property;
    private readonly IReadOnlyList<string> _order;
    private readonly IReadOnlySet<string> _finalValues;

    public WorkflowRule(string property, IReadOnlyList<string> order, IReadOnlySet<string> finalValues)
    {
        _property = property;
        _order = order;
        _finalValues = finalValues;
    }

    public ValidationCategory Category => ValidationCategory.Workflow;
    public IReadOnlyList<string> Properties => [_property];

    public string Describe() =>
        $"{_property} must move forward through {string.Join(" -> ", _order)}"
        + (_finalValues.Count > 0
            ? $", and may not change once it is {string.Join(" or ", _finalValues)}"
            : string.Empty);

    public void Emit(ValidationEmitContext ctx)
    {
        if (!ctx.HasProperty(_property))
        {
            ctx.Line($"// workflow rule skipped: '{Describe()}' references an unknown property");
            return;
        }

        var orderLiteral = string.Join(", ", _order.Select(v => $"\"{v}\""));
        var finalLiteral = string.Join(", ", _finalValues.Select(v => $"\"{v}\""));

        ctx.Line("if (stored is not null)");
        ctx.Line("{");
        ctx.Line($"    var __order = new[] {{ {orderLiteral} }};");
        ctx.Line($"    var __from = System.Array.IndexOf(__order, stored.{_property});");
        ctx.Line($"    var __to = System.Array.IndexOf(__order, item.{_property});");
        ctx.Line();
        ctx.Line("    if (__from >= 0 && __to >= 0 && __to < __from)");
        ctx.Line($"        errors.Add($\"{_property} cannot move backward from '{{stored.{_property}}}' to '{{item.{_property}}}'.\");");

        if (_finalValues.Count > 0)
        {
            ctx.Line($"    else if (__from >= 0 && new[] {{ {finalLiteral} }}.Contains(stored.{_property})"
                    + $" && !Equals(stored.{_property}, item.{_property}))");
            ctx.Line($"        errors.Add($\"{_property} cannot change once it is '{{stored.{_property}}}'.\");");
        }

        ctx.Line("}");
    }
}
```

(`_property`'s declared type is always `string` here — the Status scan (`DataIngestion/RuleProposals.cs`) only ever proposes a Status kind over a string-valued column, so no cast is needed. If a future scan ever proposed Status over a non-string type, `System.Array.IndexOf(string[], T)` would fail to compile, which is the right failure — this rule genuinely only makes sense for strings today.)

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.Core.Validation.Rules.WorkflowRuleTests"`
Expected: 5/5 PASS.

- [ ] **Step 6: Run the focused filter for every existing rule class, to confirm the new enum value changes nothing else**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~Core.Validation.Rules"`
Expected: all PASS — a third enum value must not change any existing rule's behavior (none of them switch on `ValidationCategory`).

- [ ] **Step 7: Commit**

```bash
git add CodeGenerationEngine/Core/Validation/ValidationCategory.cs CodeGenerationEngine/Core/Validation/Rules/WorkflowRule.cs CodeGenerator.Tests/Core/Validation/Rules/WorkflowRuleTests.cs
git commit -m "$(cat <<'EOF'
Add WorkflowRule, the 17th rule

Compares two records (item and stored) instead of one, which no
existing rule needed -- Emit writes a self-contained block naming
both directly rather than using the generic this./item. shape.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: `RuleProposal.Order`/`FinalValues`

**Files:**
- Modify: `CodeGenerationEngine/Models/RuleProposal.cs`
- Test: `CodeGenerator.Tests/App/ProposalReportTests.cs` (a new test alongside the existing ones — no test file exists yet purely for `RuleProposal` itself; the existing coverage of `RuleProposal` lives beside its consumers)

**Interfaces:**
- Produces: `RuleProposal.Order : IReadOnlyList<string>?` (null until set), `RuleProposal.FinalValues : IReadOnlySet<string>` (empty until set).

**Why on `RuleProposal` itself, not a new type:** unlike phase 4's `EntityRuleProposal` (a genuinely different shape — entity-scoped, more than one field), this is the *same* proposal a `Status`-kind acceptance already produced. Widening it costs two nullable/defaulted properties that are simply unused by `Unique`/`Pattern`-kind proposals — nothing to parallel with a second type.

- [ ] **Step 1: Write the failing test**

Add to `CodeGenerator.Tests/App/ProposalReportTests.cs`:

```csharp
    [TestMethod]
    public void A_status_proposal_starts_with_no_order_and_no_finals()
    {
        var proposal = new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { PropertyName = "Status", Name = "allowedvalues", AllowedValues = ["Draft", "Approved"] },
            Evidence = "Status holds only Draft, Approved"
        };

        Assert.IsNull(proposal.Order);
        Assert.IsEmpty(proposal.FinalValues);
    }

    [TestMethod]
    public void A_status_proposal_keeps_a_stated_order_and_finals()
    {
        var proposal = new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { PropertyName = "Status", Name = "allowedvalues", AllowedValues = ["Draft", "Approved"] },
            Evidence = "Status holds only Draft, Approved",
            Order = ["Draft", "Approved"],
            FinalValues = new HashSet<string> { "Approved" }
        };

        CollectionAssert.AreEqual(new[] { "Draft", "Approved" }, proposal.Order?.ToArray());
        Assert.IsTrue(proposal.FinalValues.Contains("Approved"));
    }
```

(Check `PropertyRule`'s actual constructor/property shape in `CodeGenerationEngine/Models/PropertyRule.cs` before writing this — the fields above name what `RuleProposals.cs`'s existing Status proposal already builds; match its real shape exactly rather than inventing one.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.App.ProposalReportTests"`
Expected: compile error — `RuleProposal.Order`/`FinalValues` do not exist yet; every pre-existing test in the file still compiles and passes once the two new tests are excluded (they cannot be, since a compile error blocks the whole file — expect the WHOLE file to fail to build, which is the correct RED here, not a runtime failure).

- [ ] **Step 3: Add the two properties**

In `CodeGenerationEngine/Models/RuleProposal.cs`, change the class to:

```csharp
public sealed class RuleProposal
{
    public required RuleProposalKind Kind { get; init; }
    public required PropertyRule Rule { get; init; }
    public required string Evidence { get; init; }
    public bool Accepted { get; set; }

    /// <summary>The stated transition order for a Status-kind proposal -- most recent
    /// `order status` call wins. Null until set; meaningless for Unique/Pattern kinds.</summary>
    public IReadOnlyList<string>? Order { get; set; }

    /// <summary>Values a record at this status may not leave, for a Status-kind proposal.
    /// Empty until `final status` names one; meaningless for Unique/Pattern kinds.</summary>
    public IReadOnlySet<string> FinalValues { get; set; } = new HashSet<string>();
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.App.ProposalReportTests"`
Expected: all PASS, including every pre-existing test in the file (adding two properties with safe defaults must not change any existing behavior).

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Models/RuleProposal.cs CodeGenerator.Tests/App/ProposalReportTests.cs
git commit -m "$(cat <<'EOF'
Add RuleProposal.Order and FinalValues

Widens the existing Status-kind proposal rather than adding a
parallel type -- there is nothing to parallel, only two more facts
on the same object once order/final are stated.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Wire ordered Status proposals into `build app`

**Files:**
- Modify: `CodeGenerationEngine/Models/ClassDefinition.cs`
- Modify: `AppScaffolding/DomainModelMapper.cs`
- Test: `CodeGenerator.Tests/App/WorkflowRulesReachAppTests.cs`

**Interfaces:**
- Consumes: `RuleProposal.Order`/`FinalValues` (task 2), `WorkflowRule` (task 1), `DomainModelMapper.ToClassDefinitions(CandidateDomainModel model)` (pre-existing).
- Produces: `ClassDefinition.WorkflowRules : List<IValidationRule>`.

**Where the accepted, ordered proposal actually lives:** `CandidateField.Proposals : List<RuleProposal>` (pre-existing since phase 3 — the same list `RuleProposals.cs`'s Status scan already populates). This task reads it the same way task 3 of phase 4 read `ValidationRegistry`: from inside `DomainModelMapper.ToClassDefinitions`, the one shared seam both `AppScaffolder` and `IncrementalScaffolder` reach (phase 4 already established `IncrementalScaffolder` needs no separate change, since it delegates its whole build to a fresh `AppScaffolder`).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>
/// An accepted Status proposal, once ordered, reaches ClassDefinition.WorkflowRules with no
/// scan and no generated-app run involved.
/// </summary>
[TestClass]
public class WorkflowRulesReachAppTests
{
    [TestMethod]
    public void An_ordered_accepted_status_proposal_becomes_a_workflow_rule()
    {
        var model = new CandidateDomainModel();
        var order = new CandidateEntity { Name = "Order" };
        var status = new CandidateField { Name = "Status", Type = "string" };
        status.Proposals.Add(new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { PropertyName = "Status", Name = "allowedvalues", AllowedValues = ["Draft", "Approved"] },
            Evidence = "Status holds only Draft, Approved",
            Accepted = true,
            Order = ["Draft", "Approved"],
            FinalValues = new HashSet<string> { "Approved" }
        });
        order.Fields.Add(status);
        model.Entities.Add(order);

        var classes = DomainModelMapper.ToClassDefinitions(model);
        var definition = classes.Single(c => c.Name == "Order");

        Assert.HasCount(1, definition.WorkflowRules);
        Assert.AreEqual(ValidationCategory.Workflow, definition.WorkflowRules[0].Category);
        CollectionAssert.AreEqual(new[] { "Status" }, definition.WorkflowRules[0].Properties.ToArray());
    }

    [TestMethod]
    public void An_accepted_status_proposal_with_no_order_yet_becomes_no_workflow_rule()
    {
        var model = new CandidateDomainModel();
        var order = new CandidateEntity { Name = "Order" };
        var status = new CandidateField { Name = "Status", Type = "string" };
        status.Proposals.Add(new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { PropertyName = "Status", Name = "allowedvalues", AllowedValues = ["Draft", "Approved"] },
            Evidence = "Status holds only Draft, Approved",
            Accepted = true
            // Order left null: accepted but never ordered.
        });
        order.Fields.Add(status);
        model.Entities.Add(order);

        var classes = DomainModelMapper.ToClassDefinitions(model);

        Assert.IsEmpty(classes.Single(c => c.Name == "Order").WorkflowRules);
    }

    [TestMethod]
    public void An_unaccepted_status_proposal_becomes_no_workflow_rule_even_if_ordered()
    {
        var model = new CandidateDomainModel();
        var order = new CandidateEntity { Name = "Order" };
        var status = new CandidateField { Name = "Status", Type = "string" };
        status.Proposals.Add(new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { PropertyName = "Status", Name = "allowedvalues", AllowedValues = ["Draft", "Approved"] },
            Evidence = "Status holds only Draft, Approved",
            Accepted = false,
            Order = ["Draft", "Approved"]
        });
        order.Fields.Add(status);
        model.Entities.Add(order);

        var classes = DomainModelMapper.ToClassDefinitions(model);

        Assert.IsEmpty(classes.Single(c => c.Name == "Order").WorkflowRules);
    }
}
```

(Verified: `CandidateField.Proposals : List<CodeGenerationEngine.Models.RuleProposal>` already exists exactly this way, `DomainDiscovery/Model/CandidateDomainModel.cs:59`, from phase 3 — nothing to add.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.App.WorkflowRulesReachAppTests"`
Expected: compile error — `ClassDefinition.WorkflowRules` does not exist yet.

- [ ] **Step 3: Add `ClassDefinition.WorkflowRules`**

In `CodeGenerationEngine/Models/ClassDefinition.cs`, add beside `BusinessRules`:

```csharp
    /// <summary>Rules on a confirmed status column -- forward-only order, no move from a
    /// final value -- built once the user has ordered an accepted Status proposal. Reaches
    /// the Workflow stage in the emitted validator.</summary>
    public List<IValidationRule> WorkflowRules { get; set; } = [];
```

- [ ] **Step 4: Add `DomainModelMapper.AddWorkflowValidation`**

In `AppScaffolding/DomainModelMapper.cs`, find the call site inside `ToClassDefinitions` that now reads (after phase 4):

```csharp
        AddNavigationProperties(model, classes, names);
        AddAuditFields(classes);
        AddValidation(classes);
        AddEntityValidation(classes, model, names);
```

Change it to:

```csharp
        AddNavigationProperties(model, classes, names);
        AddAuditFields(classes);
        AddValidation(classes);
        AddEntityValidation(classes, model, names);
        AddWorkflowValidation(classes, model, names);
```

Add the new method beside `AddEntityValidation`:

```csharp
    /// <summary>
    /// Builds one WorkflowRule per accepted, ordered Status proposal, so a status column's
    /// transitions reach the Workflow stage through the one list ValidatorTemplate.Generated
    /// reads. Mirrors AddEntityValidation's shape: reads the model's own proposals, writes onto
    /// the matching class, no scan of its own.
    /// </summary>
    private static void AddWorkflowValidation(
        List<ClassDefinition> classes, CandidateDomainModel model, Dictionary<string, string> names)
    {
        foreach (var entity in model.Entities)
        {
            if (!names.TryGetValue(entity.Name, out var className))
                continue;

            var definition = classes.FirstOrDefault(c => c.Name == className);
            if (definition is null)
                continue;

            foreach (var field in entity.Fields)
            {
                foreach (var proposal in field.Proposals)
                {
                    if (proposal.Kind != RuleProposalKind.Status || !proposal.Accepted
                        || proposal.Order is null)
                        continue;

                    definition.WorkflowRules.Add(
                        new WorkflowRule(field.Name, proposal.Order, proposal.FinalValues));
                }
            }
        }
    }
```

Add `using CodeGenerationEngine.Core.Validation.Rules;` near the top if not already present (needed for `WorkflowRule`; `CodeGenerationEngine.Core.Validation` and `CodeGenerationEngine.Models` are already imported from phase 4's `AddEntityValidation`).

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.App.WorkflowRulesReachAppTests"`
Expected: 3/3 PASS.

- [ ] **Step 6: Run the full focused filter for `DomainModelMapper`/`ClassDefinition`**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~DomainModelMapper"`
Expected: all PASS — the new method must not change any existing entity's class definition when it has no Status proposals at all.

- [ ] **Step 7: Commit**

```bash
git add CodeGenerationEngine/Models/ClassDefinition.cs AppScaffolding/DomainModelMapper.cs CodeGenerator.Tests/App/WorkflowRulesReachAppTests.cs
git commit -m "$(cat <<'EOF'
Wire ordered Status proposals into ClassDefinition.WorkflowRules

An accepted proposal with no stated order yet produces no rule --
order and acceptance are both required, matching the review card's
own two-step flow (accept the values, then state their sequence).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: The `Workflow` stage in the emitted validator

**Files:**
- Modify: `AppScaffolding/ValidatorTemplate.cs`
- Modify: `AppScaffolding/AppScaffolder.cs`
- Test: `CodeGenerator.Tests/App/WorkflowStageEmissionTests.cs`

**Interfaces:**
- Consumes: `WorkflowRule` (task 1), `ValidatorTemplate.Generated(string rootNamespace, ClassDefinition definition, IReadOnlyList<string> foreignKeyTargets, bool referencesRequired, IReadOnlyList<(string Entity, string Property)>? referencedBy = null, IReadOnlyList<IValidationRule>? businessRules = null, IReadOnlyList<IValidationRule>? workflowRules = null)` — one new trailing optional parameter, every existing call site keeps compiling.
- Produces: the `Workflow` static method and `Stages` entry, conditional on `workflowRules` being non-empty.

**Why `Workflow`'s stage method needs `stored` in scope, unlike `Business`'s:** `Business`'s method signature already receives the whole `context`, but its body only ever reads `context.Item` (every rule it emits is single-record). `Workflow`'s body must also read `context.Stored`, since `WorkflowRule.Emit` (task 1) writes `stored.{property}` directly into the emitted text.

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
/// The Workflow stage, proven with a hand-built rule list -- no proposal, no order command,
/// just the emission mechanics. Task 3 wires the real accepted-proposal path in.
/// </summary>
[TestClass]
public class WorkflowStageEmissionTests
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
    public void A_backward_move_is_refused()
    {
        var definition = new ClassDefinition { Name = "Order" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "Status", Type = "string" });

        var rule = new WorkflowRule("Status", ["Draft", "Approved", "Shipped"], new HashSet<string>());
        var validate = CompiledValidator(definition, [rule]);

        var errors = validate.UpdateErrors(storedStatus: "Approved", itemStatus: "Draft");

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], "cannot move backward");
    }

    [TestMethod]
    public void A_forward_skip_is_allowed()
    {
        var definition = new ClassDefinition { Name = "Order" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "Status", Type = "string" });

        var rule = new WorkflowRule("Status", ["Draft", "Approved", "Shipped"], new HashSet<string>());
        var validate = CompiledValidator(definition, [rule]);

        var errors = validate.UpdateErrors(storedStatus: "Draft", itemStatus: "Shipped");

        Assert.IsEmpty(errors);
    }

    [TestMethod]
    public void A_move_from_a_final_status_is_refused()
    {
        var definition = new ClassDefinition { Name = "Order" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        definition.Properties.Add(new PropertyDefinition { Name = "Status", Type = "string" });

        var rule = new WorkflowRule("Status", ["Draft", "Cancelled"], new HashSet<string> { "Cancelled" });
        var validate = CompiledValidator(definition, [rule]);

        var errors = validate.UpdateErrors(storedStatus: "Cancelled", itemStatus: "Draft");

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], "cannot change once it is");
    }

    [TestMethod]
    public void No_workflow_rules_means_no_workflow_stage_at_all()
    {
        var definition = new ClassDefinition { Name = "Order" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        var source = ValidatorTemplate.Generated("Acme", definition, [], referencesRequired: false);

        Assert.IsFalse(source.Contains("private static void Workflow("));
        StringAssert.DoesNotContain(source, "        Workflow,");
    }

    private static (System.Type EntityType, System.Func<string, string, IReadOnlyList<string>> UpdateErrors)
        CompiledValidator(ClassDefinition definition, IReadOnlyList<IValidationRule> workflowRules)
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
                    "Acme", definition, [], referencesRequired: false, workflowRules: workflowRules)),
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
        var updateOp = assembly.GetType("Acme.Validation.ValidationOperation")!.GetField("Update")!.GetValue(null)!;

        return (entityType, (storedStatus, itemStatus) =>
        {
            var stored = System.Activator.CreateInstance(entityType)!;
            entityType.GetProperty("Status")!.SetValue(stored, storedStatus);

            var item = System.Activator.CreateInstance(entityType)!;
            entityType.GetProperty("Status")!.SetValue(item, itemStatus);

            var ctor = contextType.GetConstructors()[0];
            var context = ctor.Invoke([item, updateOp, stored, System.Activator.CreateInstance(lookupType), null, null]);
            var outcome = validateMethod.Invoke(validator, [context])!;
            return (IReadOnlyList<string>)outcome.GetType().GetProperty("Messages")!.GetValue(outcome)!;
        });
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.App.WorkflowStageEmissionTests"`
Expected: compile error (`ValidatorTemplate.Generated` has no `workflowRules` parameter yet) — the fourth test (`No_workflow_rules_means_no_workflow_stage_at_all`) would already pass against the CURRENT template on its own, so it is not evidence by itself; the first three are.

- [ ] **Step 3: Add the `workflowRules` parameter and the `Workflow` stage**

In `AppScaffolding/ValidatorTemplate.cs`, change the `Generated` signature (currently ends `IReadOnlyList<IValidationRule>? businessRules = null)`):

```csharp
    public static string Generated(
        string rootNamespace, ClassDefinition definition, IReadOnlyList<string> foreignKeyTargets,
        bool referencesRequired,
        IReadOnlyList<(string Entity, string Property)>? referencedBy = null,
        IReadOnlyList<IValidationRule>? businessRules = null,
        IReadOnlyList<IValidationRule>? workflowRules = null)
    {
        referencedBy ??= Array.Empty<(string Entity, string Property)>();
        businessRules ??= Array.Empty<IValidationRule>();
        workflowRules ??= Array.Empty<IValidationRule>();
```

Immediately after the existing `businessStage`/`stageList` block (the one ending `if (businessBody.Length > 0) stageList += "\n        Business,";`), add:

```csharp
        // Workflow runs last: a status transition should trust every other stage, including
        // hand-stated and inferred business rules, has already passed. Unlike Business, its rule
        // needs the FULL context -- both Item and Stored -- since a transition check compares two
        // records, not one. Each rule still gets its own StringBuilder/ValidationEmitContext and
        // its own try/catch, same CS0128 lesson as Business.
        var workflowBlocks = new List<string>();

        foreach (var rule in workflowRules)
        {
            var ruleSource = new StringBuilder();
            var ruleCtx = new ValidationEmitContext(ruleSource, definition.Properties);
            rule.Emit(ruleCtx);

            var ruleText = ruleSource.ToString();
            if (ruleText.Length == 0)
                continue;

            workflowBlocks.Add(WrapBusinessRule(ruleText));
        }

        var workflowBody = string.Join("\n\n", workflowBlocks);

        var workflowStage = workflowBody.Length == 0
            ? string.Empty
            : $$"""


    /// <summary>A status column's transitions: forward-only through a stated order, and no
    /// move away from a value marked final. Runs last, after Business has already passed.</summary>
    private static void Workflow(ValidationContext<{{definition.Name}}> context, List<ValidationError> pipelineErrors)
    {
        var item = context.Item;
        var stored = context.Stored;
        if (item is null)
            return;

        var errors = new List<string>();

{{workflowBody}}
        foreach (var m in errors)
            pipelineErrors.Add(new ValidationError(null, m));
    }
""";

        if (workflowBody.Length > 0)
            stageList += "\n        Workflow,";
```

**Do NOT apply the `this.` → `item.` rewrite to `workflowBody`** — unlike `businessBody`, `WorkflowRule.Emit` (task 1) already writes `item.`/`stored.` directly, never `this.`. Applying the rewrite would be a harmless no-op today, but leaving it out is the honest signal that this rule kind was never written in the older instance-method shape the rewrite exists to fix.

And splice `{{workflowStage}}` into the returned template, immediately after `{{businessStage}}` (the line currently reads `{{entityStage}}{{businessStage}}` — change it to `{{entityStage}}{{businessStage}}{{workflowStage}}`).

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.App.WorkflowStageEmissionTests"`
Expected: 4/4 PASS.

- [ ] **Step 5: Wire `AppScaffolder.cs`'s call site**

In `AppScaffolding/AppScaffolder.cs`, the existing call (after phase 4):

```csharp
                    businessRules: definition.BusinessRules)));
```

becomes:

```csharp
                    businessRules: definition.BusinessRules,
                    workflowRules: definition.WorkflowRules)));
```

- [ ] **Step 6: Run the full focused filter for `ValidatorTemplate`/`AppScaffolder`/validator emission**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ValidatorTemplate|FullyQualifiedName~ValidatorEmitsEveryRuleKind|FullyQualifiedName~ValidatorCompiles|FullyQualifiedName~ValidatorStages|FullyQualifiedName~AppScaffolder"`
Expected: all PASS — the new optional parameter must not change a single existing emitted file.

- [ ] **Step 7: Commit**

```bash
git add AppScaffolding/ValidatorTemplate.cs AppScaffolding/AppScaffolder.cs CodeGenerator.Tests/App/WorkflowStageEmissionTests.cs
git commit -m "$(cat <<'EOF'
Add the Workflow stage to the emitted validator

Fed by any IValidationRule list; nothing calls it with real rules
yet. Unlike Business, its stage method keeps Stored in scope --
Workflow's own rule is the first to need two records instead of one.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: An accepted, ordered proposal reaches the validator end to end

**Files:**
- Test: `CodeGenerator.Tests/App/AcceptedWorkflowReachesValidatorTests.cs`

**Interfaces:**
- Consumes: `DomainModelMapper.ToClassDefinitions` (task 3), `ValidatorTemplate.Generated` (task 4) — no new production code; this task proves the two already meet.

**Why this task adds no production code:** by the end of task 4, `DomainModelMapper.AddWorkflowValidation` already folds an accepted, ordered Status proposal into `ClassDefinition.WorkflowRules`, the same list `ValidatorTemplate.Generated` already reads. Nothing between tasks 3 and 4 needs a second wire-up — matches phase 4's own task 6 precedent exactly.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The full path an accepted, ordered Status proposal travels: DomainModelMapper carries it
/// once accepted-and-ordered, ValidatorTemplate emits it, and the compiled validator actually
/// refuses a record that breaks it.
/// </summary>
[TestClass]
public class AcceptedWorkflowReachesValidatorTests
{
    [TestMethod]
    public void An_accepted_ordered_status_proposal_refuses_a_backward_move()
    {
        var model = new CandidateDomainModel();
        var order = new CandidateEntity { Name = "Order" };
        var status = new CandidateField { Name = "Status", Type = "string" };
        status.Proposals.Add(new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { PropertyName = "Status", Name = "allowedvalues", AllowedValues = ["Draft", "Approved", "Shipped"] },
            Evidence = "Status holds only Draft, Approved, Shipped",
            Accepted = true,
            Order = ["Draft", "Approved", "Shipped"]
        });
        order.Fields.Add(status);
        order.Fields.Add(new CandidateField { Name = "Id", Type = "int" });
        model.Entities.Add(order);

        var classes = DomainModelMapper.ToClassDefinitions(model);
        var definition = classes.Single(c => c.Name == "Order");

        Assert.HasCount(1, definition.WorkflowRules);

        var source = ValidatorTemplate.Generated(
            "Acme", definition, [], referencesRequired: false, workflowRules: definition.WorkflowRules);

        StringAssert.Contains(source, "private static void Workflow(");
        StringAssert.Contains(source, "        Workflow,");
        StringAssert.Contains(source, "stored.Status");
    }
}
```

(`PropertyRule`'s exact shape must match Task 2's usage — check it once, use it consistently across both files.)

- [ ] **Step 2: Run the test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.App.AcceptedWorkflowReachesValidatorTests"`
Expected: PASSES immediately, since tasks 1-4 already built every piece. If it fails, the failure must be diagnosed against tasks 1-4's own code, not "fixed" by adding new logic here.

- [ ] **Step 3: Commit**

```bash
git add CodeGenerator.Tests/App/AcceptedWorkflowReachesValidatorTests.cs
git commit -m "$(cat <<'EOF'
Prove an accepted, ordered Status proposal reaches the Workflow stage

DomainModelMapper -> ValidatorTemplate needed no further wiring:
tasks 3 and 4 already meet at ClassDefinition.WorkflowRules.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: `order status`/`final status` in the review loop

**Files:**
- Modify: `CodeGenerator/App/BuildAppCommand.cs`
- Test: `CodeGenerator.Tests/App/WorkflowCommandsTests.cs`

**Interfaces:**
- Consumes: `CandidateEntity.Fields`, `CandidateField.Proposals`, `RuleProposal.Kind`/`Accepted`/`Order`/`FinalValues` (all pre-existing or task 2).
- Produces: two new pure functions the review loop's switch calls, so the parsing/validation logic is testable without driving the console loop itself — mirrors why `ProposalReport` exists as a separate testable class rather than inline switch-case logic.

**Where these commands live, and why NOT `CommandHandlingService`/`IntentType`:** `add business validation` is dispatched through `CommandHandlingService.Handlers` (an `IntentType`-keyed dictionary), reached via `LiteralCommandTable`/`CommandEntityExtractor`'s NLP-ish parsing — that whole path operates on a persistent `ProjectMetaData`/class files, used OUTSIDE an active `build app` session. `order status`/`final status` instead target an in-progress `CandidateDomainModel`'s ALREADY-DISCOVERED proposals, which only exist during an active `build app` review session — the exact same reason `accept <n>`/`rules` are plain `switch (parts[0])` cases inside `BuildAppCommand.cs`'s review loop (`CodeGenerator/App/BuildAppCommand.cs`, the `switch` block containing `case "rules":`/`case "accept" when parts.Length >= 2:`), not `CommandHandlingService` entries. Neither `accept`/`rules` are documented in `CodeGenerationEngine/Core/CommandDictionary.cs` either — `order`/`final` follow the same precedent and are NOT added there.

**The review loop's grammar:** `typedLine.Split(...)` on whitespace, so `order status Draft,Approved,Shipped in Order` becomes `["order", "status", "Draft,Approved,Shipped", "in", "Order"]` — five tokens, comma-joined values as one token, class name last.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/App/WorkflowCommandsTests.cs`:

```csharp
using System.Linq;
using CodeGenerator.App;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class WorkflowCommandsTests
{
    private static CandidateDomainModel ModelWithAnAcceptedStatusProposal(string entityName = "Order")
    {
        var model = new CandidateDomainModel();
        var entity = new CandidateEntity { Name = entityName };
        var status = new CandidateField { Name = "Status", Type = "string" };
        status.Proposals.Add(new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { PropertyName = "Status", Name = "allowedvalues", AllowedValues = ["Draft", "Approved", "Shipped"] },
            Evidence = "Status holds only Draft, Approved, Shipped",
            Accepted = true
        });
        entity.Fields.Add(status);
        model.Entities.Add(entity);
        return model;
    }

    [TestMethod]
    public void Order_status_states_the_full_sequence()
    {
        var model = ModelWithAnAcceptedStatusProposal();

        var result = WorkflowCommands.OrderStatus(model, "Order", "Draft,Approved,Shipped");

        var proposal = model.Entities.Single().Fields.Single().Proposals.Single();
        CollectionAssert.AreEqual(new[] { "Draft", "Approved", "Shipped" }, proposal.Order?.ToArray());
        StringAssert.Contains(result, "Order set");
    }

    [TestMethod]
    public void Order_status_refuses_a_class_with_no_accepted_status_proposal()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Order" });

        var result = WorkflowCommands.OrderStatus(model, "Order", "Draft,Approved");

        StringAssert.Contains(result, "no confirmed status");
        Assert.IsEmpty(model.Entities.Single().Fields);
    }

    [TestMethod]
    public void Order_status_refuses_a_partial_value_list()
    {
        var model = ModelWithAnAcceptedStatusProposal();

        var result = WorkflowCommands.OrderStatus(model, "Order", "Draft,Approved");

        StringAssert.Contains(result, "must name every discovered value");
        Assert.IsNull(model.Entities.Single().Fields.Single().Proposals.Single().Order);
    }

    [TestMethod]
    public void Order_status_refuses_an_unknown_class()
    {
        var model = new CandidateDomainModel();

        var result = WorkflowCommands.OrderStatus(model, "Nonexistent", "Draft,Approved");

        StringAssert.Contains(result, "no class named");
    }

    [TestMethod]
    public void Final_status_marks_one_or_more_values()
    {
        var model = ModelWithAnAcceptedStatusProposal();

        var result = WorkflowCommands.FinalStatus(model, "Order", "Shipped");

        var proposal = model.Entities.Single().Fields.Single().Proposals.Single();
        Assert.IsTrue(proposal.FinalValues.Contains("Shipped"));
        StringAssert.Contains(result, "marked as final");
    }

    [TestMethod]
    public void Final_status_refuses_a_value_the_data_never_showed()
    {
        var model = ModelWithAnAcceptedStatusProposal();

        var result = WorkflowCommands.FinalStatus(model, "Order", "NoSuchStatus");

        StringAssert.Contains(result, "was not discovered");
    }

    [TestMethod]
    public void A_class_with_two_accepted_status_proposals_is_refused_not_guessed()
    {
        var model = ModelWithAnAcceptedStatusProposal();
        var second = new CandidateField { Name = "ReviewState", Type = "string" };
        second.Proposals.Add(new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { PropertyName = "Status", Name = "allowedvalues", AllowedValues = ["Open", "Closed"] },
            Evidence = "ReviewState holds only Open, Closed",
            Accepted = true
        });
        model.Entities.Single().Fields.Add(second);

        var result = WorkflowCommands.OrderStatus(model, "Order", "Draft,Approved,Shipped");

        StringAssert.Contains(result, "more than one confirmed status");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.App.WorkflowCommandsTests"`
Expected: compile error — `CodeGenerator.App.WorkflowCommands` does not exist yet.

- [ ] **Step 3: Add `WorkflowCommands`**

Create `CodeGenerator/App/WorkflowCommands.cs`:

```csharp
using System.Linq;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.App;

/// <summary>
/// `order status`/`final status`, pure and tested for the same reason ProposalReport is: the
/// review loop reads the console and cannot be driven from a test, so everything decidable is
/// lifted out of it.
/// </summary>
internal static class WorkflowCommands
{
    public static string OrderStatus(CandidateDomainModel model, string className, string valuesText)
    {
        var (proposal, error) = FindTheOneAcceptedStatusProposal(model, className);
        if (error is not null)
            return error;

        var stated = valuesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var discovered = proposal!.Rule.AllowedValues;

        if (stated.Length != discovered.Count || !discovered.All(v => stated.Contains(v)))
            return $"Order must name every discovered value exactly once: {string.Join(", ", discovered)}.";

        proposal.Order = stated;
        return $"Order set for {className}.Status: {string.Join(" -> ", stated)}.";
    }

    public static string FinalStatus(CandidateDomainModel model, string className, string valuesText)
    {
        var (proposal, error) = FindTheOneAcceptedStatusProposal(model, className);
        if (error is not null)
            return error;

        var stated = valuesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var discovered = proposal!.Rule.AllowedValues;
        var unknown = stated.Where(v => !discovered.Contains(v)).ToList();

        if (unknown.Count > 0)
            return $"'{string.Join("', '", unknown)}' was not discovered in {className}.Status.";

        var finals = new HashSet<string>(proposal.FinalValues);
        foreach (var value in stated)
            finals.Add(value);
        proposal.FinalValues = finals;

        return $"{string.Join(", ", stated)} marked as final for {className}.Status.";
    }

    private static (RuleProposal? Proposal, string? Error) FindTheOneAcceptedStatusProposal(
        CandidateDomainModel model, string className)
    {
        var entity = model.Entities.FirstOrDefault(e =>
            string.Equals(e.Name, className, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
            return (null, $"There is no class named '{className}'.");

        var accepted = entity.Fields
            .SelectMany(f => f.Proposals.Where(p => p.Kind == RuleProposalKind.Status && p.Accepted))
            .ToList();

        if (accepted.Count == 0)
            return (null, $"{className} has no confirmed status field yet -- accept its Status proposal first.");

        if (accepted.Count > 1)
            return (null, $"{className} has more than one confirmed status field; not supported yet.");

        return (accepted[0], null);
    }
}
```

Verified: `RuleProposal.Rule : PropertyRule` and `PropertyRule.AllowedValues : List<string>` (non-nullable, defaults to an empty list — `CodeGenerationEngine/Models/PropertyRule.cs:45`), matching the property access above exactly.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.App.WorkflowCommandsTests"`
Expected: 7/7 PASS.

- [ ] **Step 5: Wire the two switch cases into the review loop**

In `CodeGenerator/App/BuildAppCommand.cs`, find the `switch` block containing `case "rules":`/`case "accept" when parts.Length >= 2:` and add, in the same style:

```csharp
                case "order" when parts.Length >= 5
                    && string.Equals(parts[1], "status", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(parts[^2], "in", StringComparison.OrdinalIgnoreCase):
                    Console.WriteLine(WorkflowCommands.OrderStatus(model, parts[^1],
                        string.Join(' ', parts.Skip(2).Take(parts.Length - 4))));
                    break;

                case "final" when parts.Length >= 5
                    && string.Equals(parts[1], "status", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(parts[^2], "in", StringComparison.OrdinalIgnoreCase):
                    Console.WriteLine(WorkflowCommands.FinalStatus(model, parts[^1],
                        string.Join(' ', parts.Skip(2).Take(parts.Length - 4))));
                    break;
```

(`parts.Skip(2).Take(parts.Length - 4)` recovers everything between `status` and `in` as one value list, rejoined with a single space, in case the comma-separated list ever contains a stray space around a comma that survived the initial whitespace split — e.g. `Draft, Approved` typed with a space after the comma splits into two tokens, `"Draft,"` and `"Approved"`; rejoining and letting `WorkflowCommands`'s own `TrimEntries` split handle it is more forgiving than requiring exactly one token.)

Verified: the review loop's `CandidateDomainModel` variable is named `model` and the split tokens variable is `parts` (declared `string[] parts;` at `BuildAppCommand.cs:442`), matching the code above exactly.

- [ ] **Step 6: Run the focused filter for `BuildAppCommand`**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~BuildAppCommand"`
Expected: all PASS — check whether a driving-the-review-loop test class already exists (search for existing tests that pipe commands through this same switch, e.g. covering `"accept"`/`"rules"`) and, if so, add one case there proving `order status ... in <Class>` reaches `WorkflowCommands.OrderStatus` through the real loop, not just through the unit-tested pure function.

- [ ] **Step 7: Commit**

```bash
git add CodeGenerator/App/WorkflowCommands.cs CodeGenerator/App/BuildAppCommand.cs CodeGenerator.Tests/App/WorkflowCommandsTests.cs
git commit -m "$(cat <<'EOF'
Add order status / final status to the review loop

Pure functions, tested the way ProposalReport already is -- the
review loop's own switch cases are two lines each. Two accepted
status proposals on one class are refused, not guessed at.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: The Blazor review page's transition editor

**Files:**
- Modify: `CodeGenerator/Web/Components/Stages/ReviewStage.razor`
- Modify: `CodeGenerator.Tests/Web/ReviewStageTests.cs`

**Interfaces:**
- Consumes: `BuildSession.ModelEdited()` (pre-existing, per phase 4's own `Decide` overloads' pointer), `WorkflowCommands` is NOT reused here — Blazor edits `Order`/`FinalValues` directly (no text to parse; the UI already knows the exact value being moved or toggled).
- Produces: a new markup block reusing the existing `.proposal-row`-style conventions, new class names only for what's genuinely new (`.workflow-order`).

**CRITICAL project convention — do not violate:** this page has 27+ CSS class names asserted directly by bUnit tests elsewhere in the suite. Reuse existing classes exactly where the design calls for them; never rename or restructure one that already exists, even one that looks inconsistent.

- [ ] **Step 1: Write the failing tests**

Add to `CodeGenerator.Tests/Web/ReviewStageTests.cs`:

```csharp
    private static CandidateDomainModel AModelWithAnAcceptedStatusProposal()
    {
        var model = AModel();
        var order = new CandidateEntity { Name = "Order" };
        var status = new CandidateField { Name = "Status", Type = "string" };
        status.Proposals.Add(new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { PropertyName = "Status", Name = "allowedvalues", AllowedValues = ["Draft", "Approved", "Shipped"] },
            Evidence = "Status holds only Draft, Approved, Shipped",
            Accepted = true
        });
        order.Fields.Add(status);
        model.Entities.Add(order);
        return model;
    }

    [TestMethod]
    public void Shows_the_transition_editor_once_a_status_is_accepted()
    {
        using var context = new BunitContext();
        var session = SessionWith(AModelWithAnAcceptedStatusProposal());
        var rendered = context.Render<ReviewStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        var items = rendered.FindAll("[data-entity=Order] .workflow-order li");
        Assert.HasCount(3, items);
        StringAssert.Contains(rendered.Markup, "Draft");
    }

    [TestMethod]
    public void Moving_a_value_down_changes_its_stated_order()
    {
        using var context = new BunitContext();
        var model = AModelWithAnAcceptedStatusProposal();
        var session = SessionWith(model);
        var rendered = context.Render<ReviewStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("[data-entity=Order] .workflow-order li:first-child button.move-down").Click();

        var proposal = model.Entities.Single(e => e.Name == "Order").Fields.Single().Proposals.Single();
        Assert.AreEqual("Approved", proposal.Order?[0]);
        Assert.AreEqual("Draft", proposal.Order?[1]);
    }

    [TestMethod]
    public void Checking_final_marks_that_value()
    {
        using var context = new BunitContext();
        var model = AModelWithAnAcceptedStatusProposal();
        var session = SessionWith(model);
        var rendered = context.Render<ReviewStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("[data-entity=Order] .workflow-order li:last-child input.final").Change(true);

        var proposal = model.Entities.Single(e => e.Name == "Order").Fields.Single().Proposals.Single();
        Assert.IsTrue(proposal.FinalValues.Contains("Shipped"));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.Web.ReviewStageTests"`
Expected: the three new tests FAIL (no `.workflow-order` block rendered yet); existing tests still PASS.

- [ ] **Step 3: Add the markup block and code-behind methods**

Read `ReviewStage.razor` in full first. The FIELD-scoped proposal block already exists inside each field's own `<li @key="(field, _refusals)">`, at:

```razor
                            @foreach (var proposal in field.Proposals)
                            {
                                <label class="proposal-row">
                                    <input type="checkbox" class="proposal"
                                           checked="@proposal.Accepted"
                                           @onchange="e => Decide(proposal, e.Value)" />
                                    <span class="proposal-evidence">@proposal.Evidence</span>
                                </label>
                            }
```

**Put the transition editor INSIDE this same `@foreach`, per proposal, not at the entity level.** A Status proposal is field-scoped (`field.Proposals`, not `entity.Proposals`) — rendering per-proposal here means an entity with more than one confirmed status field gets one editor per field automatically, with no `FirstOrDefault`/restriction needed (unlike the console command in task 6, which DOES restrict to one, because a bare text command has no other way to pick a field — Blazor has no such problem, since the user clicks the block they mean). Change the block to:

```razor
                            @foreach (var proposal in field.Proposals)
                            {
                                <label class="proposal-row">
                                    <input type="checkbox" class="proposal"
                                           checked="@proposal.Accepted"
                                           @onchange="e => Decide(proposal, e.Value)" />
                                    <span class="proposal-evidence">@proposal.Evidence</span>
                                </label>

                                @if (proposal.Kind == RuleProposalKind.Status && proposal.Accepted)
                                {
                                    var order = (proposal.Order ?? proposal.Rule.AllowedValues).ToList();
                                    <ul class="workflow-order">
                                        @for (var i = 0; i < order.Count; i++)
                                        {
                                            var index = i;
                                            var value = order[index];
                                            <li>
                                                <button type="button" class="move-up" disabled="@(index == 0)"
                                                        @onclick="() => MoveStatusValue(proposal, order, index, -1)">↑</button>
                                                <button type="button" class="move-down" disabled="@(index == order.Count - 1)"
                                                        @onclick="() => MoveStatusValue(proposal, order, index, 1)">↓</button>
                                                <span class="proposal-evidence">@value</span>
                                                <label>
                                                    <input type="checkbox" class="final"
                                                           checked="@proposal.FinalValues.Contains(value)"
                                                           @onchange="e => ToggleFinalStatus(proposal, value, e.Value)" />
                                                    final
                                                </label>
                                            </li>
                                        }
                                    </ul>
                                }
                            }
```

Add the two new methods in the code-behind block, beside the existing `Decide` overloads:

```csharp
    /// <summary>Swaps a status value with its neighbour and re-states the order -- the up/down
    /// alternative to a comma-separated `order status` command, same effect either way.</summary>
    private void MoveStatusValue(RuleProposal proposal, List<string> currentOrder, int index, int direction)
    {
        var target = index + direction;
        if (target < 0 || target >= currentOrder.Count)
            return;

        (currentOrder[index], currentOrder[target]) = (currentOrder[target], currentOrder[index]);
        proposal.Order = currentOrder;
        Session.ModelEdited();
    }

    /// <summary>Takes or drops one value from the final set -- the checkbox alternative to
    /// `final status`.</summary>
    private void ToggleFinalStatus(RuleProposal proposal, string value, object? isChecked)
    {
        var on = isChecked is true || (isChecked is string text && bool.TryParse(text, out var parsed) && parsed);
        var finals = new HashSet<string>(proposal.FinalValues);

        if (on)
            finals.Add(value);
        else
            finals.Remove(value);

        proposal.FinalValues = finals;
        Session.ModelEdited();
    }
```

Add `using CodeGenerationEngine.Models;`/`RuleProposalKind` references if not already in scope (phase 4's `.entity-proposals` block already references `EntityRuleProposalKind`, a sibling type in the same namespace — check what's already imported before adding a duplicate `using`).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "ClassName=CodeGenerator.Tests.Web.ReviewStageTests"`
Expected: all PASS, including every pre-existing test in the file.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/Web/Components/Stages/ReviewStage.razor CodeGenerator.Tests/Web/ReviewStageTests.cs
git commit -m "$(cat <<'EOF'
Add the transition editor to the Blazor review page

Up/down buttons plus a final checkbox -- no drag-and-drop, matching
this page's existing checkbox-driven style and easier to bUnit-test.
New class name only for what's genuinely new: .workflow-order.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Run a real generated app

**Files:** none (verification only — no production code expected to change; any defect found here is fixed in the file it lives in, then this task's own notes updated).
- Create: `docs/superpowers/plans/2026-09-18-validation-phase-5-run-notes.md`

This is phase 3's and phase 4's own hard lesson, applied again. Phase 4's own version of this exact task was stopped partway through by explicit user decision and never completed — that gap is disclosed in the merged phase-4 PR, not silently repeated here. **This task is not optional and is not satisfied by a green suite**, per the spec's own Testing Rigor section — but whether it runs to completion, is shortened, or is stopped partway is the user's decision to make when this task is reached, not something assumed here in advance.

- [ ] **Step 1: Full solution suite**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror`, then `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --no-build`
Expected: 0 warnings/errors on build; every test passes. Record the exact pass count in the run-notes file.

- [ ] **Step 2: Generate an app with a genuine status lifecycle**

Using a CSV or JSON source (fastest to seed): design a source with an "Order"-like entity carrying a `Status` column whose values are a small fixed set (e.g. `Draft`, `Approved`, `Shipped`, `Cancelled`) across enough varied rows that the Status proposal fires. Drive the CLI (piped stdin, per the established non-interactive recipe) through `build app:` for a login-enabled Blazor and WPF target, reviewing the proposals, accepting the Status proposal, then typing `order status Draft,Approved,Shipped,Cancelled in Order` and `final status Cancelled in Order` before finalizing the build.

- [ ] **Step 3: Live click-through, at least one desktop target and Blazor**

Sign in, then attempt, in order, on each generated app: (1) an update moving `Draft → Shipped` (a legal skip) — confirm it SUCCEEDS; (2) an update moving `Approved → Draft` (backward) — confirm the app REFUSES with the expected message ON SCREEN; (3) an update attempted from `Cancelled` to any other value — confirm refusal naming the final status. Independently verify each success actually persisted (re-open/re-list the record), not just that no error appeared.

- [ ] **Step 4: Write `run-notes.md`**

Record: exact suite numbers, the exact source data and the exact `order status`/`final status` commands typed, each of the three checks and its outcome, any defect found and where it was fixed (in the task file it belongs to, not here), and confirmation that both the console and (if exercised) the Blazor transition editor were used to state the order in the same run.

- [ ] **Step 5: Commit**

```bash
git add docs/superpowers/plans/2026-09-18-validation-phase-5-run-notes.md
git commit -m "$(cat <<'EOF'
Run notes: a real generated app enforces phase 5's workflow rules

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Finishing

Once task 8 is committed and the full suite is green: push the branch, open a PR against `main`, merge once CI comes back green (this branch's standing instruction across phases 2-4). If task 8 is stopped or shortened by explicit user decision, disclose that plainly in the PR body, the same way phase 4's PR did — it is a known, accepted risk, not a silent omission, either way.
