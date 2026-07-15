# NLP Intent Classification (Plan 1 of 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move intent classification into `NaturalLanguageBridge` as `NlpCommandMapper`, which resolves an NLE `SemanticResult` to a `CodeGenerationEngine` `IntentType`, and prove with a characterization test that it reaches every intent the current `IntentAnalyzer` reaches.

**Architecture:** Purely additive. Nothing is deleted or rewired in this plan. The new mapper and its tests sit alongside the existing path, which keeps serving every command. This plan's deliverable is a passing coverage gate plus a coverage number that determines Plan 2's shape. The cutover and the deletion of `CodeGenerationEngine`'s duplicate NLP stack are Plan 2, written only after this plan reports.

**Tech Stack:** C# / .NET 10, MSTest. New code lives in `NaturalLanguageBridge`; tests in `CodeGenerator.Tests` (which already references the bridge and `CodeGenerationEngine`).

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-10-nlp-codegen-separation-design.md`. Every design claim there was verified against source.
- **Do not add code-generation vocabulary to `NaturalLanguageEngine`.** No edits to any file under `NaturalLanguageEngine/`. The mapper reads NLE's generic output; NLE stays domain-neutral.
- **Additive only.** Do not modify `IntentAnalyzer`, `CommandHandlingService`, `SemanticAnalyzer`, or delete any file. Those are Plan 2.
- The corpus is `IntentRepository.GetDefaultRules()`: 31 `IntentRule { Intent, Actions[], Targets[] }`. The **gate** is the 24 intents that have both a default rule and a dispatch handler, reached via the mapper, plus 6 guard-only intents reached via a literal table.
- Preserve dead intents: the 7 rule-but-no-handler intents (`ClassInherit`, `CreateApi`, `CreateController`, `CreateRepository`, `CreateService`, `RenameProperty`, `ValidateProject`) must stay unreachable-to-a-handler. `create a repository for Customer` must keep returning `false`.
- MSTest runs method-parallel (`[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]`). Mapper must be pure/stateless.
- Do not read `intents.json` in tests. Build the corpus from an in-test fixture copied from `GetDefaultRules()` so it survives Plan 2's deletion.

### Key existing signatures (verified)

```csharp
// NaturalLanguageBridge.NlpActionProvider
public static SemanticResult? Analyze(string input);   // null on blank/throw, never throws

// NaturalLanguageEngine.Models.Semantics
class SemanticResult { IList<Entity> Entities; IList<Relationship> Relationships; Intent? Intent; double Confidence; }
class Entity { string Text; EntityType Type; ConfidenceScore Confidence; }   // ConfidenceScore has implicit <-> double
enum EntityType { Unknown, Person, Organization, Location, Date, Time, Quantity, Action, Object, Attribute, Value }
class Intent { string Name; ConfidenceScore Confidence; }

// CodeGenerationEngine.Models
enum IntentType { Unknown, Help, CreateProject, UseProject, ListProjects, CurrentProject, CreateClass, ... }  // 52 members
```

Observed NLE output (verified by running the engine):
`create a class called Customer` → `[Action "create"], [Object "a class"], [Action "called"], [Person "Customer"]`.
`Relationships` was empty on every command tested.

---

## File Structure

- **Create** `NaturalLanguageBridge/NlpCommandMapper.cs` — resolves `SemanticResult?` → `IntentType`. Owns the `(verb, target)` → `IntentType` table and the determiner set. Stateless.
- **Create** `NaturalLanguageBridge/LiteralCommandTable.cs` — exact-match table for the 6 guard-only intents plus `demo`. Consulted before the mapper by future callers; in this plan it is only unit-tested.
- **Create** `CodeGenerator.Tests/Bridge/IntentCorpus.cs` — the `Actions × Targets` fixture copied from `GetDefaultRules()`, tagged with each phrase's expected `IntentType` and whether it is a gate intent.
- **Create** `CodeGenerator.Tests/Bridge/NlpCommandMapperCoverageTests.cs` — the gate. Replays the corpus, reports misses.
- **Create** `CodeGenerator.Tests/Bridge/NlpCommandMapperTests.cs` — unit tests for the resolution wrinkles.
- **Create** `CodeGenerator.Tests/Bridge/LiteralCommandTableTests.cs` — unit tests for the literal table.

No production file outside `NaturalLanguageBridge/` is touched.

---

## Task 1: Corpus fixture + failing coverage gate

Establishes the measurement harness. The mapper is a stub, so the coverage test fails and prints exactly which intents are unreached — that report is the task's real output.

**Files:**
- Create: `NaturalLanguageBridge/NlpCommandMapper.cs`
- Create: `CodeGenerator.Tests/Bridge/IntentCorpus.cs`
- Create: `CodeGenerator.Tests/Bridge/NlpCommandMapperCoverageTests.cs`

**Interfaces:**
- Produces: `NaturalLanguageBridge.NlpCommandMapper.ResolveIntent(SemanticResult? analysis) → IntentType`
- Produces: `IntentCorpus.GateEntries → IReadOnlyList<(string Phrase, IntentType Expected)>`

- [ ] **Step 1: Create the mapper stub**

`NaturalLanguageBridge/NlpCommandMapper.cs`:

```csharp
using CodeGenerationEngine.Models;
using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageBridge;

/// <summary>
/// Translates NLE's domain-neutral analysis into a CodeGenerationEngine
/// <see cref="IntentType"/>. This is where knowledge of code-generation
/// vocabulary lives — NLE itself never learns what a "class" is.
/// Stateless: safe for the method-parallel test run.
/// </summary>
public static class NlpCommandMapper
{
    public static IntentType ResolveIntent(SemanticResult? analysis)
        => IntentType.Unknown; // implemented in Task 2
}
```

- [ ] **Step 2: Create the corpus fixture**

`CodeGenerator.Tests/Bridge/IntentCorpus.cs`. Copy the `Actions × Targets` cross-product from `IntentRepository.GetDefaultRules()` for the **24 gate intents** (rule + handler). Omit the 7 rule-but-no-handler intents; they are not gate intents. This is a hand-copied fixture, not a read of `IntentRepository`, so it survives Plan 2.

```csharp
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Bridge;

/// <summary>
/// (phrase, expected intent) pairs generated from the Actions x Targets
/// cross-product of IntentRepository.GetDefaultRules(), restricted to the 24
/// intents that have both a rule and a dispatch handler. Copied here as a
/// fixture so it outlives the deletion of IntentRepository in Plan 2.
/// </summary>
internal static class IntentCorpus
{
    // One (actions, targets, intent) row per gate rule. Phrases are the
    // cartesian product "action target".
    private static readonly (string[] Actions, string[] Targets, IntentType Intent)[] Rules =
    [
        (["list", "show", "display", "view"], ["project", "projects"], IntentType.ListProjects),
        (["use", "select", "switch", "open", "activate"], ["project"], IntentType.UseProject),
        (["current", "active", "selected"], ["project"], IntentType.CurrentProject),
        (["create", "new", "generate"], ["project", "console project", "webapi project", "class library", "worker service", "blazor project", "wpf project"], IntentType.CreateProject),
        (["create", "add", "generate"], ["class"], IntentType.CreateClass),
        (["show", "view", "display", "open"], ["class"], IntentType.ShowClass),
        (["list", "show", "display"], ["classes"], IntentType.ListClasses),
        (["list", "show", "display"], ["classes in"], IntentType.ListClassesInProject),
        (["delete", "remove"], ["class"], IntentType.DeleteClass),
        (["restore", "recover"], ["class"], IntentType.RestoreClass),
        (["restore"], ["last class"], IntentType.RestoreLastClass),
        (["list", "show"], ["deleted classes"], IntentType.ListDeletedClasses),
        (["rename"], ["class"], IntentType.RenameClass),
        (["add", "create"], ["property"], IntentType.CreateProperty),
        (["list", "show"], ["properties"], IntentType.ListProperties),
        (["delete", "remove"], ["property"], IntentType.DeleteProperty),
        (["add", "create", "generate"], ["method"], IntentType.CreateMethod),
        (["list", "show"], ["methods"], IntentType.ListMethods),
        (["create", "generate"], ["interface"], IntentType.CreateInterface),
        (["analyze"], ["project"], IntentType.AnalyzeProject),
        (["analyze"], ["class"], IntentType.AnalyzeClass),
        (["generate"], ["documentation", "design document", "project report"], IntentType.GenerateDocumentation),
        (["generate"], ["design document", "architecture document", "technical document"], IntentType.GenerateDesignDocument),
    ];

    public static IReadOnlyList<(string Phrase, IntentType Expected)> GateEntries { get; } =
        Rules
            .SelectMany(r => r.Actions
                .SelectMany(a => r.Targets.Select(t => ($"{a} {t}", r.Intent))))
            .ToList();
}
```

> Note: `GenerateDocumentation` and `GenerateDesignDocument` share the phrase `generate design document`. That collision is real in the source table and its resolution (priority) is a mapper concern surfaced by the coverage run — leave both rows in; Task 2 decides the winner and this fixture line is revisited then.

- [ ] **Step 3: Write the coverage gate test**

`CodeGenerator.Tests/Bridge/NlpCommandMapperCoverageTests.cs`:

```csharp
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
public class NlpCommandMapperCoverageTests
{
    [TestMethod]
    public void Mapper_reaches_every_gate_intent()
    {
        var misses = new List<string>();
        var unreachedIntents = new HashSet<IntentType>();

        foreach (var (phrase, expected) in IntentCorpus.GateEntries)
        {
            var analysis = NlpActionProvider.Analyze(phrase);
            var actual = NlpCommandMapper.ResolveIntent(analysis);

            if (actual != expected)
            {
                misses.Add($"  \"{phrase}\"  expected {expected}, got {actual}");
                unreachedIntents.Add(expected);
            }
        }

        if (misses.Count > 0)
        {
            var report =
                $"{misses.Count}/{IntentCorpus.GateEntries.Count} phrases misresolved.\n" +
                $"Unreached gate intents ({unreachedIntents.Count}): " +
                string.Join(", ", unreachedIntents.OrderBy(i => i.ToString())) + "\n" +
                string.Join("\n", misses);
            Assert.Fail(report);
        }
    }
}
```

- [ ] **Step 4: Run the coverage test and read the report**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~NlpCommandMapperCoverageTests"`
Expected: **FAIL.** The assertion message is the coverage report — every phrase misresolves because the stub returns `Unknown`. Record the "Unreached gate intents" line; it is the baseline Task 2 drives to zero.

- [ ] **Step 5: Commit**

```bash
git add NaturalLanguageBridge/NlpCommandMapper.cs CodeGenerator.Tests/Bridge/IntentCorpus.cs CodeGenerator.Tests/Bridge/NlpCommandMapperCoverageTests.cs
git commit -m "Add intent-coverage gate over the default-rule corpus (failing)"
```

---

## Task 2: Implement `ResolveIntent` and close the gate

Fill in the mapper until the coverage test passes. This is iterative: run the test, read the misses, adjust the table, repeat.

**Files:**
- Modify: `NaturalLanguageBridge/NlpCommandMapper.cs`
- Modify (only if the shared-phrase collision forces it): `CodeGenerator.Tests/Bridge/IntentCorpus.cs`

**Interfaces:**
- Consumes: `NlpActionProvider.Analyze`, `SemanticResult`, `Entity`, `EntityType`, `IntentType`
- Produces: fully implemented `NlpCommandMapper.ResolveIntent`

- [ ] **Step 1: Implement extraction + table lookup**

Replace the stub body:

```csharp
using CodeGenerationEngine.Models;
using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageBridge;

public static class NlpCommandMapper
{
    private static readonly HashSet<string> Determiners = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "this", "that", "these", "those",
        "my", "your", "our", "their", "some", "any", "new"
    };

    // (verb, target) -> intent. Keys are lowercase. Built to mirror the
    // Actions x Targets rows of IntentRepository.GetDefaultRules(). The verb is
    // NLE's first Action entity; the target is NLE's first Object entity with
    // leading determiners stripped. Priority resolves the one shared phrase.
    private static readonly Dictionary<(string Verb, string Target), IntentType> Table = Build();

    public static IntentType ResolveIntent(SemanticResult? analysis)
    {
        if (analysis is null)
            return IntentType.Unknown;

        var verb = FirstOfType(analysis, EntityType.Action);
        var target = StripDeterminers(FirstOfType(analysis, EntityType.Object));

        if (verb is null || target is null)
            return IntentType.Unknown;

        return Table.TryGetValue((verb, target), out var intent)
            ? intent
            : IntentType.Unknown;
    }

    private static string? FirstOfType(SemanticResult analysis, EntityType type)
    {
        var entity = analysis.Entities.FirstOrDefault(e => e.Type == type);
        return entity is null ? null : entity.Text.Trim().ToLowerInvariant();
    }

    private static string? StripDeterminers(string? text)
    {
        if (text is null)
            return null;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .SkipWhile(w => Determiners.Contains(w))
                        .ToArray();
        return words.Length == 0 ? null : string.Join(' ', words);
    }

    private static Dictionary<(string, string), IntentType> Build()
    {
        var t = new Dictionary<(string, string), IntentType>();
        void Add(IntentType intent, string[] verbs, string[] targets)
        {
            foreach (var v in verbs)
                foreach (var g in targets)
                    t[(v, g)] = intent; // last write wins; order rows by ascending priority
        }

        Add(IntentType.ListProjects, ["list", "show", "display", "view"], ["project", "projects"]);
        Add(IntentType.UseProject, ["use", "select", "switch", "open", "activate"], ["project"]);
        Add(IntentType.CurrentProject, ["current", "active", "selected"], ["project"]);
        Add(IntentType.CreateProject, ["create", "new", "generate"], ["project", "console project", "webapi project", "class library", "worker service", "blazor project", "wpf project"]);
        Add(IntentType.CreateClass, ["create", "add", "generate"], ["class"]);
        Add(IntentType.ShowClass, ["show", "view", "display", "open"], ["class"]);
        Add(IntentType.ListClasses, ["list", "show", "display"], ["classes"]);
        Add(IntentType.ListClassesInProject, ["list", "show", "display"], ["classes in"]);
        Add(IntentType.DeleteClass, ["delete", "remove"], ["class"]);
        Add(IntentType.RestoreClass, ["restore", "recover"], ["class"]);
        Add(IntentType.RestoreLastClass, ["restore"], ["last class"]);
        Add(IntentType.ListDeletedClasses, ["list", "show"], ["deleted classes"]);
        Add(IntentType.RenameClass, ["rename"], ["class"]);
        Add(IntentType.CreateProperty, ["add", "create"], ["property"]);
        Add(IntentType.ListProperties, ["list", "show"], ["properties"]);
        Add(IntentType.DeleteProperty, ["delete", "remove"], ["property"]);
        Add(IntentType.CreateMethod, ["add", "create", "generate"], ["method"]);
        Add(IntentType.ListMethods, ["list", "show"], ["methods"]);
        Add(IntentType.CreateInterface, ["create", "generate"], ["interface"]);
        Add(IntentType.AnalyzeProject, ["analyze"], ["project"]);
        Add(IntentType.AnalyzeClass, ["analyze"], ["class"]);
        Add(IntentType.GenerateDocumentation, ["generate"], ["documentation", "design document", "project report"]);
        // GenerateDesignDocument shares "generate design document" with the row
        // above. Its rule has equal priority; the source scorer breaks the tie
        // by table order. Register it last so a bare "design document" resolves
        // here, matching IntentAnalyzer's observed behaviour. Confirm against the
        // coverage run and adjust the fixture's duplicate row if needed.
        Add(IntentType.GenerateDesignDocument, ["generate"], ["architecture document", "technical document"]);
        return t;
    }
}
```

- [ ] **Step 2: Run the coverage test**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~NlpCommandMapperCoverageTests"`
Expected: it now resolves most phrases. Read the remaining misses. Likely causes and fixes, all inside the mapper (never in NLE):
- **Multi-word targets** (`class library`, `classes in`, `deleted classes`): NLE may return only the head noun as the `Object`, or split them. If the coverage report shows `class library` arriving as `Object "library"`, add the observed form as an alias key rather than assuming.
- **Verb tagged as `Object`** (e.g. `current`, `active` are not verbs): if `current project` yields no `Action`, extend `ResolveIntent` to fall back to the first entity's text as the verb. Add that fallback only if the report proves it necessary.
- **Plural vs singular target** (`projects` vs `project`): both keys are already registered; confirm.

Make the smallest change the report demands, re-run, repeat.

- [ ] **Step 3: Iterate to green**

Repeat Step 2 until the coverage test passes. If a gate intent proves genuinely unreachable through NLE's output (not just a table gap), **stop and record it** in the commit message — that is exactly the signal Plan 2's scope depends on, and per the spec the cutover is blocked until it is resolved. Do not paper over it by adding code-gen words to NLE.

- [ ] **Step 4: Confirm no existing test regressed**

Run: `dotnet test CodeGenerator.Tests`
Expected: PASS. The mapper is not yet wired into any caller, so existing behaviour is unchanged; this confirms the new files compile and the suite is green.

- [ ] **Step 5: Commit**

```bash
git add NaturalLanguageBridge/NlpCommandMapper.cs CodeGenerator.Tests/Bridge/IntentCorpus.cs
git commit -m "Resolve NLE analysis to IntentType; coverage gate passes"
```

---

## Task 3: Unit-test the resolution wrinkles

Lock down the three intent-resolution behaviours the observed NLE output forces, independent of the corpus.

**Files:**
- Create: `CodeGenerator.Tests/Bridge/NlpCommandMapperTests.cs`

**Interfaces:**
- Consumes: `NlpCommandMapper.ResolveIntent`, `NlpActionProvider.Analyze`

- [ ] **Step 1: Write the wrinkle tests**

```csharp
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
public class NlpCommandMapperTests
{
    [TestMethod]
    public void First_action_wins_over_trailing_called_verb()
    {
        // "create a class called Customer" tags both "create" and "called" as
        // Action. The first must decide the intent.
        var analysis = NlpActionProvider.Analyze("create a class called Customer");
        Assert.AreEqual(IntentType.CreateClass, NlpCommandMapper.ResolveIntent(analysis));
    }

    [TestMethod]
    public void Determiners_are_stripped_from_the_target()
    {
        var withDeterminer = NlpCommandMapper.ResolveIntent(NlpActionProvider.Analyze("create a class"));
        var without = NlpCommandMapper.ResolveIntent(NlpActionProvider.Analyze("create class"));
        Assert.AreEqual(IntentType.CreateClass, withDeterminer);
        Assert.AreEqual(without, withDeterminer);
    }

    [TestMethod]
    public void Null_analysis_resolves_to_Unknown()
    {
        Assert.AreEqual(IntentType.Unknown, NlpCommandMapper.ResolveIntent(null));
    }

    [TestMethod]
    public void Unrecognized_verb_target_pair_resolves_to_Unknown()
    {
        // "juggle the flamingo" has no rule; must not guess.
        var analysis = NlpActionProvider.Analyze("juggle the flamingo");
        Assert.AreEqual(IntentType.Unknown, NlpCommandMapper.ResolveIntent(analysis));
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~NlpCommandMapperTests"`
Expected: PASS. If `First_action_wins...` fails, `ResolveIntent` is taking the wrong `Action` — `FirstOrDefault` already selects the first, so investigate whether NLE ordered `called` before `create` (it did not in the observed run). Fix in the mapper.

- [ ] **Step 3: Commit**

```bash
git add CodeGenerator.Tests/Bridge/NlpCommandMapperTests.cs
git commit -m "Pin first-action, determiner-strip, and no-guess mapper behaviour"
```

---

## Task 4: Literal command table for guard-only intents

The 6 intents reached today only by regex guards (`ShowLearning`, `ResetLearning`, `ShowProject`, `AddValidation`, `ShowValidation`, `GenerateValidator`) plus the `demo` short-circuit are not natural language. Give them an exact-match table so Plan 2 can consult it before the mapper. This plan only builds and unit-tests it; wiring is Plan 2.

**Files:**
- Create: `NaturalLanguageBridge/LiteralCommandTable.cs`
- Create: `CodeGenerator.Tests/Bridge/LiteralCommandTableTests.cs`

**Interfaces:**
- Produces: `LiteralCommandTable.TryResolve(string input, out IntentType intent) → bool`

- [ ] **Step 1: Write the table**

```csharp
using CodeGenerationEngine.Models;

namespace NaturalLanguageBridge;

/// <summary>
/// Exact-match commands that are not natural language and should never enter the
/// NLP path: learning and view meta-commands, plus the demo short-circuit.
/// Mirrors the regex guards in IntentAnalyzer that return score 999. Matching is
/// whitespace-trimmed and case-insensitive over the whole input.
/// </summary>
public static class LiteralCommandTable
{
    private static readonly Dictionary<string, IntentType> Exact = new(StringComparer.OrdinalIgnoreCase)
    {
        ["show learning"] = IntentType.ShowLearning,
        ["list learning"] = IntentType.ShowLearning,
        ["view learning"] = IntentType.ShowLearning,
        ["reset learning"] = IntentType.ResetLearning,
        ["clear learning"] = IntentType.ResetLearning,
        ["forget learning"] = IntentType.ResetLearning,
        ["show project"] = IntentType.ShowProject,
        ["add validation"] = IntentType.AddValidation,
        ["show validation"] = IntentType.ShowValidation,
        ["list validation"] = IntentType.ShowValidation,
        ["view validation"] = IntentType.ShowValidation,
    };

    public static bool TryResolve(string input, out IntentType intent)
    {
        var key = (input ?? string.Empty).Trim();
        return Exact.TryGetValue(key, out intent);
    }
}
```

> `GenerateValidator` and the `demo` short-circuit are deliberately left out here: `demo` is a `CodeGenerator`-CLI concern, and `GenerateValidator`'s guard is a bare `\bvalidator\b` substring, not an exact command. Plan 2 decides where each lands. This table covers only the unambiguous exact-match meta-commands, and the plan's self-review notes the gap so it is a decision, not an omission.

- [ ] **Step 2: Write the table tests**

```csharp
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
public class LiteralCommandTableTests
{
    [TestMethod]
    public void Resolves_learning_meta_commands_case_insensitively()
    {
        Assert.IsTrue(LiteralCommandTable.TryResolve("  Show Learning ", out var intent));
        Assert.AreEqual(IntentType.ShowLearning, intent);
    }

    [TestMethod]
    public void Rejects_natural_language_that_merely_contains_a_keyword()
    {
        // Not an exact command; must fall through to the NLP path.
        Assert.IsFalse(LiteralCommandTable.TryResolve("show me the learning report", out _));
    }

    [TestMethod]
    public void Rejects_empty_and_null_input()
    {
        Assert.IsFalse(LiteralCommandTable.TryResolve("", out _));
        Assert.IsFalse(LiteralCommandTable.TryResolve(null!, out _));
    }
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~LiteralCommandTableTests"`
Expected: PASS.

- [ ] **Step 4: Full suite**

Run: `dotnet test`
Expected: PASS across all projects. Nothing was rewired, so no behaviour changed.

- [ ] **Step 5: Commit**

```bash
git add NaturalLanguageBridge/LiteralCommandTable.cs CodeGenerator.Tests/Bridge/LiteralCommandTableTests.cs
git commit -m "Add literal command table for non-NLP meta-commands"
```

---

## Plan 1 exit report

Before handing off to Plan 2, write a short note (in the Plan 2 kickoff, or as a commit-message summary) answering:

1. **Coverage:** did the gate pass for all 24 mapper intents? If any were unreachable through NLE, name them — the spec blocks the cutover until they are resolved.
2. **Aliases discovered:** which multi-word targets did NLE deliver differently than the rule table spelled them (e.g. `class library` → `library`)? Plan 2's entity extraction inherits these.
3. **Guards still load-bearing:** were any regex guards impossible to replace with the literal table (ambiguous substrings like `\bvalidator\b`)? Those constrain Plan 2's wiring order.

This report is the input Plan 2 needs and the reason it is not written yet.

---

## Self-Review

**Spec coverage.** Plan 1 implements the spec's "Plan 1 — intent classification" scope: `NlpCommandMapper` (Tasks 1–3), the coverage gate over `GetDefaultRules()` (Tasks 1–2), and the literal command table (Task 4). The spec's Plan 2 scope — `EntityExtractor` move, seam flip, deletion — is intentionally absent and gated on this plan's exit report, matching the spec's "written after Plan 1 reports" instruction. The dead-intent preservation constraint is carried as a Global Constraint and referenced in Task 2 Step 3.

**Placeholder scan.** No TBD/TODO. Every code step shows complete, compilable code. The one deferred decision (shared `generate design document` phrase, and where `demo`/`GenerateValidator` land) is called out explicitly with the reason, not left blank — these are genuine facts-to-be-measured, and the plan states how the measurement resolves them rather than hand-waving.

**Type consistency.** `ResolveIntent(SemanticResult?) → IntentType`, `TryResolve(string, out IntentType) → bool`, and `IntentCorpus.GateEntries → IReadOnlyList<(string, IntentType)>` are used identically everywhere they appear. `NlpActionProvider.Analyze`, `Entity.Text`, `Entity.Type`, and `EntityType.Action`/`.Object` match the verified signatures in the header.

**Risk the plan carries honestly.** The exact intent counts (24 reachable) are a hypothesis this plan is built to test, not a guarantee. Task 2 Step 3 makes an unreachable intent a stop-and-report event rather than a silently-swallowed failure, which is the correct handling given the spec gates the cutover on it.
