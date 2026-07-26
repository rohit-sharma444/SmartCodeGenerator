# Intent Wiring and Field-Type Heuristics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `class inherit`, `rename property` and `validate project` work end to end from the CLI, guard the recognition/handler invariant with a test, and fix both field-name type heuristics.

**Architecture:** Recognition is added where the existing code already puts it — an exact-match row in `LiteralCommandTable` for the argument-free command, and two regex guards in `CommandFront` for the argument-bearing ones. Each guard builds its `ParsedCommand` from its own capture groups (the `UseProjectRe` precedent) rather than delegating to `CommandEntityExtractor`, because `CommandParser.Normalize` rewrites `entity`/`model`/`object` → `class` and `field` → `property` and would corrupt user type names. Implementations go in `CodeGenerationEngine/Core/` as small public static services, matching `ClassPropertyService`. Both type heuristics move from substring matching to whole-token matching over camel/Pascal splits and are pinned together by one conformance corpus.

**Tech Stack:** C# / .NET 10, Roslyn (`Microsoft.CodeAnalysis.CSharp`), MSTest in `CodeGenerator.Tests`, xUnit in `DomainDiscovery.Tests`.

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-07-26-intent-wiring-and-type-inference-design.md` (read the "Corrected" note under Problem 1 first).
- **CI parity command** — the only build that matters: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror`. The local `CodeGenerator` Debug build hides analyzer errors and runs 1 of 3 test projects.
- **Emitted or edited C# is verified by compiling it** via `Compiler.Compile`, never by substring or `ParseText` assertions. Roslyn error recovery makes a malformed file pass syntax-tree assertions.
- **`Handled` (the handler `bool`) means the command succeeded**, not that it was recognized. `BatchCommand` rolls back on `false`. A handler that changed nothing returns `false`.
- **`CodeGenerationEngine` has zero project references** and must keep them. Do not reference `AppScaffolding` or `DomainDiscovery` from it.
- **`IntentType` members must not be removed or reordered** — the learned-intent store persists them as integers.
- New tests that touch `SessionContext`, `ProjectManager.CurrentProject` or `LearnedIntentStore` are `[DoNotParallelize]` and reset that state in `[TestInitialize]`.
- Test workspaces live under `Path.GetTempPath()` with a `Guid` suffix and are deleted in `[TestCleanup]`, swallowing `IOException`.
- Run `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx` for the full suite. Baseline is 1183 passing.

---

### Task 1: Characterization test — prove the commands are unrecognized

The spec's original premise was wrong once already. Pin the real behaviour with a running test before building on it.

**Files:**
- Create: `CodeGenerator.Tests/Bridge/RecognitionBaselineTests.cs`

**Interfaces:**
- Consumes: `NaturalLanguageBridge.CommandFront.Parse(string input, string workspace)`, `CodeGenerationEngine.Service.CommandHandlingService.IsSupported(IntentType)`.
- Produces: nothing. Tasks 6, 7 and 8 each delete their own method from this file.

- [ ] **Step 1: Write the test**

```csharp
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

/// <summary>
/// Baseline for the three commands this branch makes work. They are not merely
/// unhandled — they are unrecognized: CommandFront.Parse resolves intent from
/// LiteralCommandTable, its own regex guards, and NlpCommandMapper's table, and
/// none of the three appears in any of them. IntentRepository's rules do not
/// drive recognition.
///
/// Each method here is deleted by the task that adds the corresponding
/// recognition, and replaced by a positive assertion.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RecognitionBaselineTests
{
    [TestMethod]
    public void Validate_project_is_not_recognized_yet()
    {
        Assert.AreEqual(IntentType.Unknown, CommandFront.Parse("validate project", "").Intent);
        Assert.IsFalse(CommandHandlingService.IsSupported(IntentType.ValidateProject));
    }

    [TestMethod]
    public void Rename_property_is_not_recognized_yet()
    {
        Assert.AreEqual(
            IntentType.Unknown,
            CommandFront.Parse("rename property Sku to Code in Product", "").Intent);
        Assert.IsFalse(CommandHandlingService.IsSupported(IntentType.RenameProperty));
    }

    [TestMethod]
    public void Inherits_on_an_existing_class_is_not_recognized_yet()
    {
        Assert.AreEqual(
            IntentType.Unknown,
            CommandFront.Parse("Order inherits AuditableBase", "").Intent);
        Assert.IsFalse(CommandHandlingService.IsSupported(IntentType.ClassInherit));
    }
}
```

- [ ] **Step 2: Run and confirm all three PASS**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~RecognitionBaselineTests"`
Expected: 3 passed. If any FAILS, the premise is wrong again — stop and re-read `CommandFront.Parse` before continuing.

- [ ] **Step 3: Commit**

```bash
git add CodeGenerator.Tests/Bridge/RecognitionBaselineTests.cs
git commit -m "test(bridge): pin that class inherit, rename property and validate project are unrecognized"
```

---

### Task 2: Expose the recognition sources and gate the invariant

**Files:**
- Modify: `NaturalLanguageBridge/LiteralCommandTable.cs`
- Modify: `NaturalLanguageBridge/NlpCommandMapper.cs:38`
- Modify: `NaturalLanguageBridge/CommandFront.cs` (add `GuardedIntents`)
- Create: `CodeGenerator.Tests/Bridge/ReachableIntentGateTests.cs`

**Interfaces:**
- Produces:
  - `LiteralCommandTable.Intents` → `IReadOnlyCollection<IntentType>`
  - `NlpCommandMapper.MappedIntents` → `IReadOnlyCollection<IntentType>`
  - `CommandFront.GuardedIntents` → `IReadOnlyCollection<IntentType>` (hand-maintained; Tasks 6–8 add to it)

- [ ] **Step 1: Write the failing test**

```csharp
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

/// <summary>
/// Every intent a user can actually reach must have a handler. Recognition comes
/// from exactly three places — LiteralCommandTable, CommandFront's regex guards,
/// and NlpCommandMapper's table — and nothing checked that the handler table
/// kept up with them. Deliberately NOT gated on IntentRepository: its rules do
/// not drive recognition, so gating there would demand handlers for intents no
/// user can type.
/// </summary>
[TestClass]
public sealed class ReachableIntentGateTests
{
    private static IEnumerable<IntentType> Reachable() =>
        LiteralCommandTable.Intents
            .Concat(CommandFront.GuardedIntents)
            .Concat(NlpCommandMapper.MappedIntents)
            .Distinct();

    [TestMethod]
    public void Every_reachable_intent_has_a_handler()
    {
        var missing = Reachable()
            .Where(i => !CommandHandlingService.IsSupported(i))
            .ToList();

        Assert.IsEmpty(missing,
            "Reachable intents with no handler would print 'not supported yet': "
            + string.Join(", ", missing));
    }

    // A source that returns an empty collection would make the gate above pass
    // vacuously, which is the one way this test can rot without failing.
    [TestMethod]
    public void Each_recognition_source_is_non_empty()
    {
        Assert.IsTrue(LiteralCommandTable.Intents.Count > 0, "LiteralCommandTable.Intents is empty.");
        Assert.IsTrue(CommandFront.GuardedIntents.Count > 0, "CommandFront.GuardedIntents is empty.");
        Assert.IsTrue(NlpCommandMapper.MappedIntents.Count > 0, "NlpCommandMapper.MappedIntents is empty.");
    }

    [TestMethod]
    public void Reachable_set_contains_a_known_member_from_each_source()
    {
        CollectionAssert.Contains(LiteralCommandTable.Intents.ToList(), IntentType.Help);
        CollectionAssert.Contains(CommandFront.GuardedIntents.ToList(), IntentType.GenerateValidator);
        CollectionAssert.Contains(NlpCommandMapper.MappedIntents.ToList(), IntentType.CreateClass);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ReachableIntentGateTests"`
Expected: FAIL to compile — `LiteralCommandTable.Intents`, `CommandFront.GuardedIntents` and `NlpCommandMapper.MappedIntents` do not exist.

- [ ] **Step 3: Add the three accessors**

In `NaturalLanguageBridge/LiteralCommandTable.cs`, after the `Exact` dictionary:

```csharp
    /// <summary>The intents this table can produce. Consumed by the reachable-intent
    /// gate test, which asserts every recognizable intent has a handler.</summary>
    public static IReadOnlyCollection<IntentType> Intents => [.. Exact.Values.Distinct()];
```

In `NaturalLanguageBridge/NlpCommandMapper.cs`, after the `Table` field:

```csharp
    /// <summary>The intents this table can produce. See LiteralCommandTable.Intents.</summary>
    public static IReadOnlyCollection<IntentType> MappedIntents => [.. Table.Values.Distinct()];
```

In `NaturalLanguageBridge/CommandFront.cs`, after the `UseProjectRe` field:

```csharp
    /// <summary>The intents the regex guards in <see cref="Parse"/> can produce.
    /// Hand-maintained: the guards are individual patterns, not a table.
    /// ADD TO THIS LIST WHEN YOU ADD A GUARD — the reachable-intent gate test
    /// reads it to check the intent has a handler behind it.</summary>
    public static IReadOnlyCollection<IntentType> GuardedIntents { get; } =
    [
        IntentType.ListClassesInProject,
        IntentType.UseProject,
        IntentType.AddBusinessValidation,
        IntentType.ShowBusinessValidation,
        IntentType.AddValidation,
        IntentType.ShowValidation,
        IntentType.GenerateValidator
    ];
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ReachableIntentGateTests"`
Expected: 3 passed. The gate passes today — it is a regression guard, and Tasks 6–8 each extend `GuardedIntents`/`Intents` under its protection.

- [ ] **Step 5: Commit**

```bash
git add NaturalLanguageBridge/LiteralCommandTable.cs NaturalLanguageBridge/NlpCommandMapper.cs NaturalLanguageBridge/CommandFront.cs CodeGenerator.Tests/Bridge/ReachableIntentGateTests.cs
git commit -m "test(bridge): gate that every reachable intent has a handler"
```

---

### Task 3: `FieldTypeHeuristics` — token matching for the `create class` path

**Files:**
- Create: `CodeGenerationEngine/Core/FieldTypeHeuristics.cs`
- Modify: `CodeGenerationEngine/Core/CodeGenerator.cs:197-214` (`ResolvePropertyType`) and delete `InferTypeFromName` at `:247-267`
- Create: `CodeGenerator.Tests/Core/FieldTypeHeuristicsTests.cs`

**Interfaces:**
- Produces: `CodeGenerationEngine.Core.FieldTypeHeuristics.Infer(string fieldName)` → `string`, one of `"bool"`, `"decimal"`, `"DateTime"`, `"int"`, `"Guid"`, `"string"`. Task 4 mirrors this vocabulary; Task 5 pins the two together.

- [ ] **Step 1: Write the failing test**

```csharp
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

[TestClass]
public sealed class FieldTypeHeuristicsTests
{
    [TestMethod]
    // Cases the old substring matching got wrong.
    [DataRow("Discount", "decimal")]   // was int: "discount".Contains("count")
    [DataRow("Isbn", "string")]        // was bool: StartsWith("is")
    [DataRow("Issue", "string")]       // was bool
    [DataRow("Hash", "string")]        // was bool: StartsWith("has")
    [DataRow("Vat", "decimal")]        // was DateTime: EndsWith("at")
    [DataRow("Fluid", "string")]       // was Guid: Contains("uid")
    [DataRow("InvoiceNumber", "string")] // was int: an identifier code, not a count
    [DataRow("Duplicate", "string")]   // "rate" is a substring, not a token
    // Cases that must keep working.
    [DataRow("Price", "decimal")]
    [DataRow("TotalCost", "decimal")]
    [DataRow("IsActive", "bool")]
    [DataRow("HasLateFee", "bool")]    // bool prefix beats the Fee -> decimal token
    [DataRow("CreatedAt", "DateTime")]
    [DataRow("DueDate", "DateTime")]
    [DataRow("StockCount", "int")]
    [DataRow("RowGuid", "Guid")]
    [DataRow("Title", "string")]
    // Deliberate calls from the spec.
    [DataRow("Number", "int")]         // the bare name is a count
    [DataRow("CustomerId", "int")]     // ForeignKeys.cs requires int to see an FK
    [DataRow("Id", "int")]
    public void Infer_matches_whole_tokens(string fieldName, string expected)
    {
        Assert.AreEqual(expected, FieldTypeHeuristics.Infer(fieldName));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    public void Infer_defaults_to_string_for_no_name(string? fieldName)
    {
        Assert.AreEqual("string", FieldTypeHeuristics.Infer(fieldName!));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~FieldTypeHeuristicsTests"`
Expected: FAIL to compile — `FieldTypeHeuristics` does not exist.

- [ ] **Step 3: Create `FieldTypeHeuristics`**

```csharp
using System.Text;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Heuristic C# type for a field name, matched on whole camel/Pascal tokens.
/// </summary>
/// <remarks>
/// Substring matching typed <c>Discount</c> as int ("count"), <c>Isbn</c> as bool
/// ("is"), <c>Vat</c> as DateTime ("at") and <c>Fluid</c> as Guid ("uid"). Whole-token
/// matching removes that entire family; the vocabularies below carry the words the
/// substrings used to catch by accident.
///
/// A mirror of these tables lives in <c>DomainDiscovery.Enrichment.TypeInference</c>,
/// which serves the <c>build app:</c> path. The two projects share no reference, so
/// <c>FieldTypeConformanceTests</c> in CodeGenerator.Tests pins them to one corpus.
/// Change one, change both.
/// </remarks>
public static class FieldTypeHeuristics
{
    private static readonly string[] BoolPrefixes = ["is", "has", "can", "should", "was"];

    private static readonly string[] DecimalWords =
        ["price", "cost", "amount", "total", "subtotal", "fee", "fine",
         "balance", "salary", "discount", "rate", "tax", "vat"];

    private static readonly string[] DateWords =
        ["date", "time", "at", "created", "updated", "due", "timestamp"];

    private static readonly string[] IntWords = ["count", "quantity", "qty", "age", "stock", "id"];

    private static readonly string[] GuidWords = ["guid", "uuid"];

    public static string Infer(string fieldName)
    {
        var tokens = Tokenize(fieldName);

        if (tokens.Count == 0)
            return "string";

        // A leading Is/Has/Can/Should/Was makes the field a flag whatever follows:
        // HasLateFee is a bool, not a decimal.
        if (BoolPrefixes.Contains(tokens[0]))
            return "bool";

        // `Number` alone is a count; `<X>Number` is an identifier code
        // (InvoiceNumber, PhoneNumber, SerialNumber) and stays a string.
        if (tokens[^1] == "number")
            return tokens.Count == 1 ? "int" : "string";

        // The head noun decides: TotalCost is a cost, OrderDate is a date.
        for (var i = tokens.Count - 1; i >= 0; i--)
        {
            var type = Lookup(tokens[i]);
            if (type is not null)
                return type;
        }

        return "string";
    }

    private static string? Lookup(string token) =>
        DecimalWords.Contains(token) ? "decimal"
        : DateWords.Contains(token) ? "DateTime"
        : IntWords.Contains(token) ? "int"
        : GuidWords.Contains(token) ? "Guid"
        : null;

    /// <summary>
    /// Lower-cased camel/Pascal tokens. A run of capitals stays whole, so "ISBN"
    /// is one token and never matches the "is" prefix rule.
    /// </summary>
    private static List<string> Tokenize(string? fieldName)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (var ch in fieldName ?? string.Empty)
        {
            if (ch is '_' or ' ' or '-')
            {
                Flush();
                continue;
            }

            if (char.IsUpper(ch) && current.Length > 0 && !char.IsUpper(current[^1]))
                Flush();

            current.Append(ch);
        }

        Flush();
        return tokens;

        void Flush()
        {
            if (current.Length > 0)
                tokens.Add(current.ToString().ToLowerInvariant());
            current.Clear();
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~FieldTypeHeuristicsTests"`
Expected: PASS (24 cases).

- [ ] **Step 5: Point `ResolvePropertyType` at it and delete the old heuristic**

In `CodeGenerationEngine/Core/CodeGenerator.cs`, replace the body of `ResolvePropertyType` (`:197-214`) with:

```csharp
    private static string ResolvePropertyType(PropertyDefinition? property)
    {
        if (property == null)
            return "string";

        if (!string.IsNullOrWhiteSpace(property.Type))
            return NormalizeType(property.Type);

        return NormalizeType(FieldTypeHeuristics.Infer(property.Name ?? string.Empty));
    }
```

Then delete the whole `InferTypeFromName` method (`:247-267`). The two
`Console.WriteLine($"[CodeGenerationEngine] …")` lines go with the old body — they
printed diagnostics into the user's CLI for every untyped property.

- [ ] **Step 6: Run the affected suite**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~CodeGenerator.Tests"`
Expected: PASS. If a test asserted `Guid` where `NormalizeType` now yields
`System.Guid`, fix the expectation — `NormalizeType` behaviour is unchanged, only
the heuristic feeding it is.

- [ ] **Step 7: Commit**

```bash
git add CodeGenerationEngine/Core/FieldTypeHeuristics.cs CodeGenerationEngine/Core/CodeGenerator.cs CodeGenerator.Tests/Core/FieldTypeHeuristicsTests.cs
git commit -m "fix(codegen): infer field types from whole tokens, not substrings"
```

---

### Task 4: Mirror the rules in `DomainDiscovery.TypeInference`

**Files:**
- Modify: `DomainDiscovery/Enrichment/TypeInference.cs` (full rewrite of the body)
- Modify: `DomainDiscovery.Tests/Enrichment/TypeInferenceTests.cs`

**Interfaces:**
- Consumes: the rule order and vocabularies defined in Task 3.
- Produces: `DomainDiscovery.Enrichment.TypeInference.Infer(string fieldName)` → same six spellings as `FieldTypeHeuristics.Infer`. Callers `PropertyMiner.cs:304` and `ModelEditor.cs:100` are unchanged.

**Note:** the spec expected the two to differ in spelling (`DateTime` vs
`System.DateTime`) and the corpus to normalize. They no longer differ —
`FieldTypeHeuristics` returns the simple names and lets `NormalizeType` expand
them, exactly as the old heuristic's output did. Task 5's corpus compares
directly.

- [ ] **Step 1: Extend the existing test with the new cases (xUnit here, not MSTest)**

Add these rows to the existing `[Theory]` in `DomainDiscovery.Tests/Enrichment/TypeInferenceTests.cs`, keeping all 11 current rows:

```csharp
    [InlineData("Discount", "decimal")]
    [InlineData("Isbn", "string")]
    [InlineData("Hash", "string")]
    [InlineData("Vat", "decimal")]
    [InlineData("InvoiceNumber", "string")]
    [InlineData("Number", "int")]
    [InlineData("CustomerId", "int")]
    [InlineData("CreatedAt", "DateTime")]
    [InlineData("RowGuid", "Guid")]
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~TypeInferenceTests"`
Expected: FAIL — `Discount` returns `int`, `Isbn` returns `bool`, `CustomerId` returns `string`.

- [ ] **Step 3: Rewrite `TypeInference`**

Replace the entire contents of `DomainDiscovery/Enrichment/TypeInference.cs` with
this. The tables and rule order are identical to `FieldTypeHeuristics` by design —
that is what Task 5's corpus enforces.

```csharp
using System.Text;

namespace DomainDiscovery.Enrichment;

/// <summary>
/// Heuristic mapping from a field name to a C# type, matched on whole camel/Pascal
/// tokens. Deliberately simple: the review loop lets the user correct anything this
/// gets wrong.
/// </summary>
/// <remarks>
/// Mirrors <c>CodeGenerationEngine.Core.FieldTypeHeuristics</c>, which serves the
/// `create class` path. DomainDiscovery and CodeGenerationEngine share no project
/// reference, so the two cannot be one class; FieldTypeConformanceTests in
/// CodeGenerator.Tests — the only test project that sees both — pins them to one
/// corpus. Change one, change both.
///
/// Substring matching typed <c>Discount</c> as int ("count"), <c>Isbn</c> as bool
/// ("is") and <c>InvoiceNumber</c> as int. Whole-token matching removes that family.
/// </remarks>
public static class TypeInference
{
    private static readonly string[] BoolPrefixes = ["is", "has", "can", "should", "was"];

    private static readonly string[] DecimalWords =
        ["price", "cost", "amount", "total", "subtotal", "fee", "fine",
         "balance", "salary", "discount", "rate", "tax", "vat"];

    private static readonly string[] DateWords =
        ["date", "time", "at", "created", "updated", "due", "timestamp"];

    private static readonly string[] IntWords = ["count", "quantity", "qty", "age", "stock", "id"];

    private static readonly string[] GuidWords = ["guid", "uuid"];

    public static string Infer(string fieldName)
    {
        var tokens = Tokenize(fieldName);

        if (tokens.Count == 0)
            return "string";

        // A leading Is/Has/Can/Should/Was makes the field a flag whatever follows:
        // HasLateFee is a bool, not a decimal.
        if (BoolPrefixes.Contains(tokens[0]))
            return "bool";

        // `Number` alone is a count; `<X>Number` is an identifier code
        // (InvoiceNumber, PhoneNumber, SerialNumber) and stays a string.
        if (tokens[^1] == "number")
            return tokens.Count == 1 ? "int" : "string";

        // The head noun decides: TotalCost is a cost, OrderDate is a date.
        for (var i = tokens.Count - 1; i >= 0; i--)
        {
            var type = Lookup(tokens[i]);
            if (type is not null)
                return type;
        }

        return "string";
    }

    private static string? Lookup(string token) =>
        DecimalWords.Contains(token) ? "decimal"
        : DateWords.Contains(token) ? "DateTime"
        : IntWords.Contains(token) ? "int"
        : GuidWords.Contains(token) ? "Guid"
        : null;

    /// <summary>
    /// Lower-cased camel/Pascal tokens. A run of capitals stays whole, so "ISBN" is
    /// one token and never matches the "is" prefix rule.
    /// </summary>
    private static List<string> Tokenize(string? fieldName)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (var ch in fieldName ?? string.Empty)
        {
            if (ch is '_' or ' ' or '-')
            {
                Flush();
                continue;
            }

            if (char.IsUpper(ch) && current.Length > 0 && !char.IsUpper(current[^1]))
                Flush();

            current.Append(ch);
        }

        Flush();
        return tokens;

        void Flush()
        {
            if (current.Length > 0)
                tokens.Add(current.ToString().ToLowerInvariant());
            current.Clear();
        }
    }
}
```

The `*Id → int` rule is new to this side. It is not cosmetic:
`AppScaffolding/ForeignKeys.cs:28` requires `p.Type == "int"` before it will treat
a field as a foreign key, so mined `CustomerId` fields were invisible to FK
detection while they came back as `string`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~DomainDiscovery.Tests"`
Expected: PASS. Watch for enrichment tests that asserted a mined field's type —
a changed expectation is correct here, a changed *field set* is not.

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/Enrichment/TypeInference.cs DomainDiscovery.Tests/Enrichment/TypeInferenceTests.cs
git commit -m "fix(enrichment): infer field types from whole tokens, and type *Id as int"
```

---

### Task 5: Pin both heuristics to one conformance corpus

**Files:**
- Create: `CodeGenerator.Tests/Core/FieldTypeConformanceTests.cs`

**Interfaces:**
- Consumes: `CodeGenerationEngine.Core.FieldTypeHeuristics.Infer`, `DomainDiscovery.Enrichment.TypeInference.Infer`.

- [ ] **Step 1: Write the test**

```csharp
using CodeGenerationEngine.Core;
using DomainDiscovery.Enrichment;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// One corpus, both heuristics. `create class` uses FieldTypeHeuristics and
/// `build app:` uses TypeInference; they are separate classes because
/// CodeGenerationEngine (a leaf with no project references) and DomainDiscovery
/// share no reference. This is the only test project that sees both, so it is the
/// only place the two can be held to the same answers.
/// </summary>
[TestClass]
public sealed class FieldTypeConformanceTests
{
    [TestMethod]
    [DataRow("Discount", "decimal")]
    [DataRow("Price", "decimal")]
    [DataRow("TotalCost", "decimal")]
    [DataRow("LateFine", "decimal")]
    [DataRow("Vat", "decimal")]
    [DataRow("DueDate", "DateTime")]
    [DataRow("CreatedTime", "DateTime")]
    [DataRow("CreatedAt", "DateTime")]
    [DataRow("Quantity", "int")]
    [DataRow("StockCount", "int")]
    [DataRow("Number", "int")]
    [DataRow("CustomerId", "int")]
    [DataRow("Id", "int")]
    [DataRow("IsActive", "bool")]
    [DataRow("HasLateFee", "bool")]
    [DataRow("RowGuid", "Guid")]
    [DataRow("Isbn", "string")]
    [DataRow("Hash", "string")]
    [DataRow("Fluid", "string")]
    [DataRow("Duplicate", "string")]
    [DataRow("InvoiceNumber", "string")]
    [DataRow("Title", "string")]
    [DataRow("Name", "string")]
    public void Both_heuristics_agree(string fieldName, string expected)
    {
        Assert.AreEqual(expected, FieldTypeHeuristics.Infer(fieldName),
            $"FieldTypeHeuristics disagrees for '{fieldName}'.");
        Assert.AreEqual(expected, TypeInference.Infer(fieldName),
            $"TypeInference disagrees for '{fieldName}'. The two tables must stay in step.");
    }
}
```

- [ ] **Step 2: Run test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~FieldTypeConformanceTests"`
Expected: PASS (23 cases × 2 assertions). A failure here after Tasks 3–4 means the
two vocabularies drifted — fix the table, not the corpus.

- [ ] **Step 3: Commit**

```bash
git add CodeGenerator.Tests/Core/FieldTypeConformanceTests.cs
git commit -m "test: pin both field-type heuristics to one conformance corpus"
```

---

### Task 6: `class inherit` — recognition, base-list fix, handler

**Files:**
- Modify: `CodeGenerationEngine/Core/ClassInheritanceService.cs` (find by name; append, not replace)
- Modify: `NaturalLanguageBridge/CommandFront.cs` (new `ClassInheritRe` guard, add to `GuardedIntents`)
- Modify: `CodeGenerationEngine/Service/CommandHandlingService.cs` (handler entry + handler)
- Modify: `CodeGenerator.Tests/Bridge/RecognitionBaselineTests.cs` (delete the `Inherits_…_not_recognized_yet` method)
- Create: `CodeGenerator.Tests/Core/ClassInheritCommandTests.cs`

**Interfaces:**
- Consumes: `CommandFront.GuardedIntents` (Task 2).
- Produces: `ClassInheritanceService.AddInheritance(string workspaceRoot, string className, string baseClass, out string message)` → `bool` (signature unchanged).

**Trap this task exists to avoid:** appending a base type gives
`class Order : IAuditable, Entity`, which is **CS1722** — the base class must come
first. Insert at index 0. Only a compile assertion catches this; a substring
assertion passes.

**Second trap:** `CommandParser.Normalize` maps `entity`/`model`/`object` → `class`
and `field` → `property` (`CommandParser.cs:12-31`). A base class named `Entity`
would be rewritten to `class` if this went through `CommandEntityExtractor`. The
guard therefore fills `ClassName`/`BaseClass` from its own capture groups on the
raw text, like `UseProjectRe` at `CommandFront.cs:73-81`. Test base names avoid
the synonym keys regardless.

- [ ] **Step 1: Write the failing tests**

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class ClassInheritCommandTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_inherit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private bool Run(string input)
    {
        ProjectMetaData? project = null;
        return CommandFront.Run(input, ref project, [], _workspace, _ => { });
    }

    private static void AssertCompiles(string code)
    {
        var result = Compiler.Compile(code);
        Assert.IsTrue(result.Success,
            "Edited file does not compile:" + Environment.NewLine
            + string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    public void Recognizes_the_command_and_adds_the_base_type()
    {
        var file = Path.Combine(_workspace, "Order.cs");
        File.WriteAllText(file,
            "namespace Shop { public class AuditableBase { } public class Order { public int Id { get; set; } } }");

        Assert.IsTrue(Run("Order inherits AuditableBase"));

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, ": AuditableBase");
        AssertCompiles(code);
    }

    // Appending put the base class after the interface, which is CS1722.
    [TestMethod]
    public void Keeps_an_existing_interface_and_puts_the_base_class_first()
    {
        var file = Path.Combine(_workspace, "Order.cs");
        File.WriteAllText(file,
            "namespace Shop { public interface IAuditable { } public class AuditableBase { } "
            + "public class Order : IAuditable { public int Id { get; set; } } }");

        Assert.IsTrue(Run("Order inherits AuditableBase"));

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "IAuditable");
        AssertCompiles(code);
    }

    // The service took the first class declaration in the file, not the named one.
    [TestMethod]
    public void Targets_the_named_class_not_the_first_in_the_file()
    {
        var file = Path.Combine(_workspace, "Order.cs");
        File.WriteAllText(file,
            "namespace Shop { public class AuditableBase { } public class OrderLine { } "
            + "public class Order { } }");

        Assert.IsTrue(Run("Order inherits AuditableBase"));

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "class Order : AuditableBase");
        StringAssert.DoesNotMatch(code, new System.Text.RegularExpressions.Regex(@"class OrderLine\s*:"));
        AssertCompiles(code);
    }

    [TestMethod]
    public void Reports_failure_when_the_base_type_is_already_present()
    {
        var file = Path.Combine(_workspace, "Order.cs");
        File.WriteAllText(file,
            "namespace Shop { public class AuditableBase { } public class Order : AuditableBase { } }");

        Assert.IsFalse(Run("Order inherits AuditableBase"),
            "Nothing changed, so the command must report failure.");
    }

    [TestMethod]
    public void Reports_failure_when_the_class_is_missing()
    {
        Assert.IsFalse(Run("Ghost inherits AuditableBase"));
    }

    // `create a Customer class that inherits AuditableBase` must still create a new
    // class with a base type -- the new guard must not steal it.
    [TestMethod]
    public void Does_not_steal_the_create_class_phrasing()
    {
        Assert.AreEqual(
            IntentType.CreateClass,
            CommandFront.Parse("create a Customer class that inherits AuditableBase", _workspace).Intent);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ClassInheritCommandTests"`
Expected: the five `Run(...)` tests FAIL (command unrecognized → `Handled` false, or `IsFalse` passing for the wrong reason — check the `Recognizes_…` test fails first). `Does_not_steal_…` passes already.

- [ ] **Step 3: Add the recognition guard**

In `NaturalLanguageBridge/CommandFront.cs`, add the field after `UseProjectRe`:

```csharp
    // `Order inherits AuditableBase` on an EXISTING class. Anchored whole-sentence and
    // it cannot begin with create/generate, so `create a Customer class that inherits X`
    // still routes to CreateClass, which sets a base type at generation time.
    // Fields come from these groups, not from CommandEntityExtractor: Normalize maps
    // entity/model/object -> class, which would rewrite a base type named `Entity`.
    private static readonly Regex ClassInheritRe =
        new(@"^(?:class\s+)?(?<class>\w+)(?:\s+class)?\s+(?:inherits?|extends|derives\s+from)\s+(?<base>\w+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
```

Add `IntentType.ClassInherit` to the `GuardedIntents` list from Task 2.

In `Parse`, immediately after the `UseProjectRe` block (before the business-validation guards):

```csharp
        var inheritMatch = ClassInheritRe.Match(text);
        if (inheritMatch.Success)
            return new ParsedCommand
            {
                Intent = IntentType.ClassInherit,
                ClassName = inheritMatch.Groups["class"].Value.Trim(),
                BaseClass = inheritMatch.Groups["base"].Value.Trim(),
                OriginalSentence = input ?? string.Empty,
                Confidence = 1.0
            };
```

- [ ] **Step 4: Fix `ClassInheritanceService`**

Replace the body of `AddInheritance` in `CodeGenerationEngine/Core/ClassInheritanceService.cs` with:

```csharp
    public static bool AddInheritance(
        string workspaceRoot,
        string className,
        string baseClass,
        out string message)
    {
        message = string.Empty;

        var file = Directory
            .GetFiles(workspaceRoot, $"{className}.cs", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (file == null)
        {
            message = $"Class '{className}' not found.";
            return false;
        }

        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();

        // By name: a file may hold several types, and the base type belongs to the one
        // the user named, not to whichever is declared first.
        var classNode = root
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text.Equals(className, StringComparison.OrdinalIgnoreCase));

        if (classNode == null)
        {
            message = $"Class '{className}' not found in {Path.GetFileName(file)}.";
            return false;
        }

        if (classNode.BaseList is not null && classNode.BaseList.Types.Any(t =>
                t.Type.ToString().Equals(baseClass, StringComparison.OrdinalIgnoreCase)))
        {
            message = $"Class '{className}' already inherits from '{baseClass}'.";
            return false;
        }

        var baseType = SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(baseClass));

        // Insert first, never append: `class Order : IAuditable, AuditableBase` is CS1722.
        // WithBaseList used to REPLACE the list, silently dropping existing interfaces.
        var baseList = classNode.BaseList is null
            ? SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(baseType))
            : classNode.BaseList.WithTypes(classNode.BaseList.Types.Insert(0, baseType));

        root = root.ReplaceNode(classNode, classNode.WithBaseList(baseList));

        File.WriteAllText(file, root.NormalizeWhitespace().ToFullString());

        message = $"Class '{className}' now inherits from '{baseClass}'.";
        return true;
    }
```

- [ ] **Step 5: Add the handler**

In `CodeGenerationEngine/Service/CommandHandlingService.cs`, add to the `Handlers` dictionary after the `RestoreLastClass` entry:

```csharp
            [IntentType.ClassInherit] = HandleClassInherit,
```

and the handler beside `HandleRenameClass`:

```csharp
    private static bool HandleClassInherit(CommandExecutionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.Command.BaseClass))
        {
            Console.WriteLine("No base class specified.");
            return false;
        }

        var ok = ClassInheritanceService.AddInheritance(
            ctx.WorkspaceRoot, ctx.Command.ClassName, ctx.Command.BaseClass, out var message);

        Console.WriteLine(message);
        return ok;
    }
```

- [ ] **Step 6: Delete the stale baseline method**

Remove `Inherits_on_an_existing_class_is_not_recognized_yet` from
`CodeGenerator.Tests/Bridge/RecognitionBaselineTests.cs`.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ClassInheritCommandTests|FullyQualifiedName~ReachableIntentGateTests|FullyQualifiedName~RecognitionBaselineTests"`
Expected: all PASS.

- [ ] **Step 8: Commit**

```bash
git add CodeGenerationEngine/Core/ClassInheritanceService.cs NaturalLanguageBridge/CommandFront.cs CodeGenerationEngine/Service/CommandHandlingService.cs CodeGenerator.Tests/Core/ClassInheritCommandTests.cs CodeGenerator.Tests/Bridge/RecognitionBaselineTests.cs
git commit -m "feat(engine): make 'X inherits Y' work on an existing class"
```

---

### Task 7: `rename property` — model field, service, recognition, handler

**Files:**
- Modify: `CodeGenerationEngine/Models/ParsedCommand.cs` (add `NewPropertyName`, clear it in `Clear()`)
- Create: `CodeGenerationEngine/Core/ClassPropertyRenameService.cs`
- Modify: `NaturalLanguageBridge/CommandFront.cs` (new `RenamePropertyRe` guard, add to `GuardedIntents`)
- Modify: `CodeGenerationEngine/Service/CommandHandlingService.cs` (handler entry + handler)
- Modify: `CodeGenerator.Tests/Bridge/RecognitionBaselineTests.cs` (delete the rename method)
- Create: `CodeGenerator.Tests/Core/RenamePropertyCommandTests.cs`

**Interfaces:**
- Consumes: `CommandFront.GuardedIntents` (Task 2).
- Produces: `ClassPropertyRenameService.RenameProperty(string workspaceRoot, string className, string oldName, string newName, out string message)` → `bool`; `ParsedCommand.NewPropertyName` → `string` (defaults to `string.Empty`).

- [ ] **Step 1: Write the failing tests**

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class RenamePropertyCommandTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_renameprop_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private bool Run(string input)
    {
        ProjectMetaData? project = null;
        return CommandFront.Run(input, ref project, [], _workspace, _ => { });
    }

    private string WriteProduct(string body)
    {
        var file = Path.Combine(_workspace, "Product.cs");
        File.WriteAllText(file, body);
        return file;
    }

    private static void AssertCompiles(string code)
    {
        var result = Compiler.Compile(code);
        Assert.IsTrue(result.Success,
            "Edited file does not compile:" + Environment.NewLine
            + string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    public void Parses_the_command_into_old_new_and_class()
    {
        var command = CommandFront.Parse("rename property Sku to Code in Product", _workspace);

        Assert.AreEqual(IntentType.RenameProperty, command.Intent);
        Assert.AreEqual("Sku", command.PropertyName);
        Assert.AreEqual("Code", command.NewPropertyName);
        Assert.AreEqual("Product", command.ClassName);
    }

    [TestMethod]
    public void Renames_the_declaration_and_still_compiles()
    {
        var file = WriteProduct(
            "namespace Shop { public class Product { public string Sku { get; set; } = \"\"; } }");

        Assert.IsTrue(Run("rename property Sku to Code in Product"));

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "Code");
        Assert.IsFalse(code.Contains("Sku"), "The old name should be gone: " + code);
        AssertCompiles(code);
    }

    [TestMethod]
    public void Leaves_a_same_named_property_on_another_class_alone()
    {
        var file = WriteProduct(
            "namespace Shop { public class Other { public string Sku { get; set; } = \"\"; } "
            + "public class Product { public string Sku { get; set; } = \"\"; } }");

        Assert.IsTrue(Run("rename property Sku to Code in Product"));

        var code = File.ReadAllText(file);
        StringAssert.Contains(code, "class Other");
        StringAssert.Contains(code, "Code");
        // Other.Sku survives, so exactly one "Sku" remains.
        Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(code, @"\bSku\b").Count, code);
        AssertCompiles(code);
    }

    [TestMethod]
    public void Reports_failure_when_the_property_is_missing()
    {
        WriteProduct("namespace Shop { public class Product { public int Id { get; set; } } }");

        Assert.IsFalse(Run("rename property Sku to Code in Product"));
    }

    [TestMethod]
    public void Reports_failure_when_the_new_name_is_taken()
    {
        WriteProduct(
            "namespace Shop { public class Product { public string Sku { get; set; } = \"\"; "
            + "public string Code { get; set; } = \"\"; } }");

        Assert.IsFalse(Run("rename property Sku to Code in Product"));
    }

    [TestMethod]
    public void Reports_failure_when_the_class_is_missing()
    {
        Assert.IsFalse(Run("rename property Sku to Code in Ghost"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~RenamePropertyCommandTests"`
Expected: FAIL to compile — `ParsedCommand.NewPropertyName` does not exist.

- [ ] **Step 3: Add `NewPropertyName` to `ParsedCommand`**

In `CodeGenerationEngine/Models/ParsedCommand.cs`, beside `PropertyName`:

```csharp
    // Target name for `rename property <old> to <new>`.
    public string NewPropertyName { get; set; } = string.Empty;
```

and in `Clear()`, beside the other member resets:

```csharp
        NewPropertyName = string.Empty;
```

- [ ] **Step 4: Create the rename service**

`CodeGenerationEngine/Core/ClassPropertyRenameService.cs`:

```csharp
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Renames a property declaration in one class. Scope matches
/// <see cref="ClassRenameService"/>: the declaration only, no reference updates
/// anywhere. The success message says so, because silently renaming a declaration
/// out from under its call sites is worse when it is not stated.
/// </summary>
public static class ClassPropertyRenameService
{
    public static bool RenameProperty(
        string workspaceRoot,
        string className,
        string oldName,
        string newName,
        out string message)
    {
        message = string.Empty;

        try
        {
            var file = Directory
                .GetFiles(workspaceRoot, "*.cs", SearchOption.AllDirectories)
                .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                    .Equals(className, StringComparison.OrdinalIgnoreCase));

            if (file == null)
            {
                message = $"Class '{className}' not found.";
                return false;
            }

            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();

            var classNode = root
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text.Equals(className, StringComparison.OrdinalIgnoreCase));

            if (classNode == null)
            {
                message = $"Class '{className}' not found in {Path.GetFileName(file)}.";
                return false;
            }

            var properties = classNode.Members.OfType<PropertyDeclarationSyntax>().ToList();

            var property = properties.FirstOrDefault(p => p.Identifier.Text.Equals(oldName, StringComparison.Ordinal));

            if (property == null)
            {
                message = $"Property '{oldName}' not found in '{className}'.";
                return false;
            }

            if (properties.Any(p => p.Identifier.Text.Equals(newName, StringComparison.Ordinal)))
            {
                message = $"Property '{newName}' already exists in '{className}'.";
                return false;
            }

            var renamed = property.WithIdentifier(
                SyntaxFactory.Identifier(newName).WithTriviaFrom(property.Identifier));

            root = root.ReplaceNode(property, renamed);

            // ToFullString, not NormalizeWhitespace: renaming one identifier must not
            // reformat the user's whole file.
            File.WriteAllText(file, root.ToFullString());

            message = $"Property '{oldName}' renamed to '{newName}' in '{className}'. "
                + "References elsewhere were not updated.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = ex.Message;
            return false;
        }
    }
}
```

- [ ] **Step 5: Add the recognition guard**

In `NaturalLanguageBridge/CommandFront.cs`, after `ClassInheritRe`:

```csharp
    // `rename property <old> to <new> in <Class>`. The mapper's Table is keyed on
    // (verb, whole remainder), so it cannot match an argument-bearing form; and
    // Normalize maps field -> property, so the fields come from these groups.
    private static readonly Regex RenamePropertyRe =
        new(@"^rename\s+(?:property|field)\s+(?<old>\w+)\s+to\s+(?<new>\w+)(?:\s+(?:in|on|of)\s+(?<class>\w+))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
```

Add `IntentType.RenameProperty` to `GuardedIntents`.

In `Parse`, immediately after the `ClassInheritRe` block:

```csharp
        var renamePropertyMatch = RenamePropertyRe.Match(text);
        if (renamePropertyMatch.Success)
        {
            var renameCmd = new ParsedCommand
            {
                Intent = IntentType.RenameProperty,
                PropertyName = renamePropertyMatch.Groups["old"].Value.Trim(),
                NewPropertyName = renamePropertyMatch.Groups["new"].Value.Trim(),
                OriginalSentence = input ?? string.Empty,
                Confidence = 1.0
            };

            renameCmd.ClassName = renamePropertyMatch.Groups["class"].Success
                ? renamePropertyMatch.Groups["class"].Value.Trim()
                : SessionContext.CurrentClass ?? string.Empty;

            return renameCmd;
        }
```

`SessionContext` needs `using CodeGenerationEngine.Core;` — check whether the file
already has it and add it only if missing.

- [ ] **Step 6: Add the handler**

In the `Handlers` dictionary, after `DeleteProperty`:

```csharp
            [IntentType.RenameProperty] = HandleRenameProperty,
```

and the handler beside `HandleDeleteProperty`:

```csharp
    private static bool HandleRenameProperty(CommandExecutionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.Command.ClassName))
        {
            Console.WriteLine("No class specified for the property rename.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(ctx.Command.PropertyName)
            || string.IsNullOrWhiteSpace(ctx.Command.NewPropertyName))
        {
            Console.WriteLine("Both the current and the new property name are required.");
            return false;
        }

        var ok = ClassPropertyRenameService.RenameProperty(
            ctx.WorkspaceRoot, ctx.Command.ClassName,
            ctx.Command.PropertyName, ctx.Command.NewPropertyName, out var message);

        Console.WriteLine(message);
        return ok;
    }
```

- [ ] **Step 7: Delete the stale baseline method**

Remove `Rename_property_is_not_recognized_yet` from `RecognitionBaselineTests.cs`.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~RenamePropertyCommandTests|FullyQualifiedName~ReachableIntentGateTests|FullyQualifiedName~RecognitionBaselineTests"`
Expected: all PASS.

- [ ] **Step 9: Commit**

```bash
git add CodeGenerationEngine/Models/ParsedCommand.cs CodeGenerationEngine/Core/ClassPropertyRenameService.cs NaturalLanguageBridge/CommandFront.cs CodeGenerationEngine/Service/CommandHandlingService.cs CodeGenerator.Tests/Core/RenamePropertyCommandTests.cs CodeGenerator.Tests/Bridge/RecognitionBaselineTests.cs
git commit -m "feat(engine): add 'rename property <old> to <new> in <Class>'"
```

---

### Task 8: `validate project` — multi-file compilation, replacing the stub

**Files:**
- Modify: `CodeGenerationEngine/Core/Compiler.cs` (add `CompileSources`)
- Create: `CodeGenerationEngine/Core/ProjectCompiler.cs`
- Modify: `NaturalLanguageBridge/LiteralCommandTable.cs` (three exact entries)
- Modify: `CodeGenerationEngine/Service/CommandHandlingService.cs` (handler entry + handler)
- Modify: `CodeGenerationEngine/Core/NaturalLanguageCodeEngine.cs` (delete the `CompileProject or ValidateProject` arm at `:48-49` and `HandleCompileProject` at `:106-110`)
- Modify: `CodeGenerator.Tests/Bridge/RecognitionBaselineTests.cs` (delete the validate method — the file is now empty of tests, so delete the file)
- Create: `CodeGenerator.Tests/Core/ProjectCompilerTests.cs`

**Interfaces:**
- Produces:
  - `Compiler.CompileSources(IEnumerable<(string Path, string Code)> sources, IEnumerable<string>? additionalAssemblyPaths = null)` → `CompilationResult`
  - `ProjectCompiler.CompileWorkspace(string root)` → `ProjectCompilationResult`
  - `record ProjectCompilationResult(bool Success, int FileCount, IReadOnlyList<string> Errors, int TotalErrors)` with `string Report()`

**Handler-contract note:** the handler returns `true` whenever it *ran* the
compilation, including when the code has errors — the command did its job, and
the report says what it found. Returning `false` for "the workspace has errors"
would make `BatchCommand` roll back a successful diagnostic run. It returns
`false` only when it could not run (root missing).

- [ ] **Step 1: Write the failing tests**

```csharp
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

[TestClass]
public sealed class ProjectCompilerTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "cg_compile_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private void Write(string name, string code) =>
        File.WriteAllText(Path.Combine(_workspace, name), code);

    [TestMethod]
    public void Reports_success_for_a_clean_workspace()
    {
        Write("Customer.cs", "namespace Shop { public class Customer { public int Id { get; set; } } }");

        var result = ProjectCompiler.CompileWorkspace(_workspace);

        Assert.IsTrue(result.Success, result.Report());
        Assert.AreEqual(1, result.FileCount);
        Assert.IsEmpty(result.Errors);
    }

    // One compilation over all files, so a type declared in another file resolves.
    // Compiler.Compile(string) is one tree per call and would report CS0246 here.
    [TestMethod]
    public void Resolves_a_type_declared_in_another_file()
    {
        Write("Customer.cs", "namespace Shop { public class Customer { public Address Home { get; set; } = new(); } }");
        Write("Address.cs", "namespace Shop { public class Address { public string City { get; set; } = \"\"; } }");

        var result = ProjectCompiler.CompileWorkspace(_workspace);

        Assert.IsTrue(result.Success, result.Report());
        Assert.AreEqual(2, result.FileCount);
    }

    [TestMethod]
    public void Reports_a_real_diagnostic_with_file_and_line()
    {
        Write("Broken.cs", "namespace Shop\n{\n    public class Broken { public int Id { get; set } }\n}");

        var result = ProjectCompiler.CompileWorkspace(_workspace);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Errors.Count > 0, "Expected at least one reported error.");
        StringAssert.Contains(result.Errors[0], "Broken.cs");
        StringAssert.Contains(result.Errors[0], "(3,");
    }

    [TestMethod]
    public void Caps_the_reported_diagnostics_but_reports_the_total()
    {
        for (var i = 0; i < 30; i++)
            Write($"Broken{i}.cs", $"public class Broken{i} {{ public int Id {{ get; set }} }}");

        var result = ProjectCompiler.CompileWorkspace(_workspace);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(ProjectCompiler.MaxReportedErrors, result.Errors.Count);
        Assert.IsTrue(result.TotalErrors > ProjectCompiler.MaxReportedErrors,
            $"Expected more than {ProjectCompiler.MaxReportedErrors} errors, got {result.TotalErrors}.");
        StringAssert.Contains(result.Report(), result.TotalErrors.ToString());
    }

    [TestMethod]
    public void An_empty_workspace_succeeds_and_says_it_found_nothing()
    {
        var result = ProjectCompiler.CompileWorkspace(_workspace);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, result.FileCount);
        StringAssert.Contains(result.Report(), "No C# files");
    }

    [TestMethod]
    public void A_missing_root_fails_rather_than_claiming_success()
    {
        var result = ProjectCompiler.CompileWorkspace(Path.Combine(_workspace, "nope"));

        Assert.IsFalse(result.Success);
    }

    // bin/ and obj/ hold generated duplicates of the same types; compiling them
    // produces CS0101 noise that has nothing to do with the user's code.
    [TestMethod]
    public void Skips_bin_and_obj()
    {
        Write("Customer.cs", "namespace Shop { public class Customer { } }");
        var obj = Path.Combine(_workspace, "obj");
        Directory.CreateDirectory(obj);
        File.WriteAllText(Path.Combine(obj, "Customer.cs"), "namespace Shop { public class Customer { } }");

        var result = ProjectCompiler.CompileWorkspace(_workspace);

        Assert.AreEqual(1, result.FileCount);
        Assert.IsTrue(result.Success, result.Report());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ProjectCompilerTests"`
Expected: FAIL to compile — `ProjectCompiler` does not exist.

- [ ] **Step 3: Add `Compiler.CompileSources`**

In `CodeGenerationEngine/Core/Compiler.cs`, after `Compile`:

```csharp
    /// <summary>
    /// Compiles several files as ONE assembly, so a type declared in one file
    /// resolves from another. <see cref="Compile(string, IEnumerable{string})"/>
    /// builds a single-tree compilation and cannot do this.
    /// </summary>
    public static CompilationResult CompileSources(
        IEnumerable<(string Path, string Code)> sources,
        IEnumerable<string>? additionalAssemblyPaths = null)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);

        // path: is what puts the file name into each diagnostic's location.
        var syntaxTrees = sources
            .Where(s => !string.IsNullOrWhiteSpace(s.Code))
            .Select(s => CSharpSyntaxTree.ParseText(s.Code, parseOptions, path: s.Path))
            .ToArray();

        if (syntaxTrees.Length == 0)
        {
            return new CompilationResult
            {
                Success = false,
                Diagnostics = [],
                AssemblyBytes = null
            };
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: $"WorkspaceAssembly_{Guid.NewGuid():N}",
            syntaxTrees: syntaxTrees,
            references: BuildReferences(additionalAssemblyPaths),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();

        var emitResult = compilation.Emit(stream);

        return new CompilationResult
        {
            Success = emitResult.Success,
            Diagnostics = [.. emitResult.Diagnostics],
            AssemblyBytes = emitResult.Success ? stream.ToArray() : null
        };
    }
```

- [ ] **Step 4: Create `ProjectCompiler`**

`CodeGenerationEngine/Core/ProjectCompiler.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace CodeGenerationEngine.Core;

/// <summary>Outcome of compiling a whole workspace.</summary>
/// <param name="Success">Whether the compilation ran AND produced no errors.</param>
/// <param name="FileCount">How many .cs files were compiled.</param>
/// <param name="Errors">Formatted error lines, capped at <see cref="ProjectCompiler.MaxReportedErrors"/>.</param>
/// <param name="TotalErrors">How many errors there were in total.</param>
public sealed record ProjectCompilationResult(
    bool Success,
    int FileCount,
    IReadOnlyList<string> Errors,
    int TotalErrors)
{
    public string Report()
    {
        if (FileCount == 0)
        {
            return Success
                ? "No C# files found to validate."
                : "No C# files found to validate: the folder does not exist.";
        }

        if (Success)
            return $"{FileCount} file(s) compile against the runtime reference set.";

        var lines = new List<string>
        {
            $"{TotalErrors} error(s) in {FileCount} file(s):"
        };

        lines.AddRange(Errors);

        if (TotalErrors > Errors.Count)
            lines.Add($"... and {TotalErrors - Errors.Count} more.");

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// Compiles every .cs file under a folder as one assembly and reports real
/// diagnostics. This replaces a handler that returned the literal string
/// "Project compilation successful (stub)."
/// </summary>
/// <remarks>
/// References come from the runtime reference set, not the project's
/// PackageReferences, so a workspace using EF Core or WPF types will report
/// unresolved-type errors that `dotnet build` would not. That is why the report
/// says "compile against the runtime reference set" rather than "builds".
/// </remarks>
public static class ProjectCompiler
{
    public const int MaxReportedErrors = 20;

    public static ProjectCompilationResult CompileWorkspace(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return new ProjectCompilationResult(false, 0, [], 0);

        var files = Directory
            .GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsBuildArtifact(f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
            return new ProjectCompilationResult(true, 0, [], 0);

        var sources = new List<(string Path, string Code)>();

        foreach (var file in files)
        {
            try
            {
                sources.Add((file, File.ReadAllText(file)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable file is not a compile error; skip it rather than
                // failing the whole validation.
            }
        }

        var result = Compiler.CompileSources(sources);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        return new ProjectCompilationResult(
            Success: errors.Count == 0,
            FileCount: sources.Count,
            Errors: [.. errors.Take(MaxReportedErrors).Select(Format)],
            TotalErrors: errors.Count);
    }

    private static string Format(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var name = string.IsNullOrEmpty(span.Path) ? "(unknown)" : Path.GetFileName(span.Path);

        return $"{name}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): "
            + $"error {diagnostic.Id}: {diagnostic.GetMessage()}";
    }

    // bin/ and obj/ hold generated copies of the same types, which would report as
    // CS0101 duplicates against the user's own files.
    private static bool IsBuildArtifact(string path)
    {
        var separator = Path.DirectorySeparatorChar;

        return path.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ProjectCompilerTests"`
Expected: PASS (7 tests).

- [ ] **Step 6: Add recognition and the handler**

In `NaturalLanguageBridge/LiteralCommandTable.cs`, add to `Exact`:

```csharp
        ["validate project"] = IntentType.ValidateProject,
        ["check project"] = IntentType.ValidateProject,
        ["compile project"] = IntentType.ValidateProject,
```

In `CodeGenerationEngine/Service/CommandHandlingService.cs`, add to `Handlers`:

```csharp
            [IntentType.ValidateProject] = HandleValidateProject,
            [IntentType.CompileProject] = HandleValidateProject,
```

and the handler:

```csharp
    /// <summary>
    /// Returns true whenever the compilation RAN, even with errors found: the
    /// command did its job and the report says what it found. Returning false for
    /// "the code has errors" would make BatchCommand roll back a successful
    /// diagnostic run.
    /// </summary>
    private static bool HandleValidateProject(CommandExecutionContext ctx)
    {
        var root = string.IsNullOrWhiteSpace(ctx.CurrentProject?.ProjectPath)
            ? ctx.WorkspaceRoot
            : ctx.CurrentProject!.ProjectPath;

        var result = ProjectCompiler.CompileWorkspace(root);

        Console.WriteLine(result.Report());

        return result.FileCount > 0 || result.Success;
    }
```

- [ ] **Step 7: Delete the stub**

In `CodeGenerationEngine/Core/NaturalLanguageCodeEngine.cs`, delete the switch arm

```csharp
            IntentType.CompileProject or IntentType.ValidateProject
                => HandleCompileProject(workspaceRoot),
```

and the `HandleCompileProject` method (`:106-110`) that returned
`"Project compilation successful (stub)."`.

- [ ] **Step 8: Delete the now-empty baseline file**

`RecognitionBaselineTests.cs` has one method left
(`Validate_project_is_not_recognized_yet`). Delete the file — all three commands
now have positive tests.

```bash
git rm CodeGenerator.Tests/Bridge/RecognitionBaselineTests.cs
```

- [ ] **Step 9: Verify recognition and the gate**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ProjectCompilerTests|FullyQualifiedName~ReachableIntentGateTests"`
Expected: PASS. Add one assertion to `ProjectCompilerTests` confirming the
command routes:

```csharp
    [TestMethod]
    public void Validate_project_is_recognized()
    {
        Assert.AreEqual(
            CodeGenerationEngine.Models.IntentType.ValidateProject,
            NaturalLanguageBridge.CommandFront.Parse("validate project", "").Intent);
    }
```

- [ ] **Step 10: Commit**

```bash
git add CodeGenerationEngine/Core/Compiler.cs CodeGenerationEngine/Core/ProjectCompiler.cs CodeGenerationEngine/Core/NaturalLanguageCodeEngine.cs NaturalLanguageBridge/LiteralCommandTable.cs CodeGenerationEngine/Service/CommandHandlingService.cs CodeGenerator.Tests/Core/ProjectCompilerTests.cs
git commit -m "feat(engine): validate project compiles the workspace instead of returning a stub"
```

---

### Task 9: Delete the fake architecture generators

**Files:**
- Delete: `CodeGenerationEngine/Core/ServiceGenerator.cs`, `RepositoryGenerator.cs`, `ControllerGenerator.cs`, `ApiGenerator.cs`
- Modify: `CodeGenerationEngine/Core/NaturalLanguageCodeEngine.cs:15-18` (drop the four intents from the arm)
- Create: `CodeGenerator.Tests/Core/UnsupportedCommandArtifactTests.cs`

**Interfaces:**
- Consumes: nothing. These four classes have no callers once the switch arm is narrowed.

**Why:** `CodeGenerationEngine.Generate` handles only `CreateClass` and
`CreateInterface` and returns the literal string `"// Unsupported command"` for
anything else (`Core/CodeGenerator.cs:19`). That string is non-empty, so
`GenerateAndSave`'s `IsNullOrWhiteSpace` guard passes it to
`GeneratedCodeService.Save` and writes it to a `.cs` file. The four generators it
would otherwise reach emit namespace-less empty shells.

- [ ] **Step 1: Write the failing test**

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// `// Unsupported command` is what CodeGenerationEngine.Generate returns for any
/// intent it does not handle. It is non-empty, so GenerateAndSave's
/// IsNullOrWhiteSpace guard would have written it to a .cs file. No routed intent
/// may reach it.
/// </summary>
[TestClass]
[DoNotParallelize] // ExecuteCommand reads and writes SessionContext.
public sealed class UnsupportedCommandArtifactTests
{
    [TestMethod]
    [DataRow(IntentType.CreateService)]
    [DataRow(IntentType.CreateRepository)]
    [DataRow(IntentType.CreateController)]
    [DataRow(IntentType.CreateApi)]
    public void The_architecture_intents_are_not_routed_into_the_code_generator(IntentType intent)
    {
        var code = NaturalLanguageCodeEngine.ExecuteCommand(
            new ParsedCommand { Intent = intent, ClassName = "Customer" },
            Path.GetTempPath());

        Assert.IsFalse(code.Contains("// Unsupported command"),
            $"{intent} still reaches CodeGenerationEngine.Generate, which returns a "
            + "comment that would be written to disk as a class file.");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~UnsupportedCommandArtifactTests"`
Expected: FAIL — all four return `"// Unsupported command"`.

- [ ] **Step 3: Narrow the switch arm**

In `CodeGenerationEngine/Core/NaturalLanguageCodeEngine.cs`, replace the arm at
`:15-18` with:

```csharp
            IntentType.CreateClass or IntentType.CreateMethod or IntentType.CreateProject
            or IntentType.CreateInterface
                => CodeGenerationEngine.Generate(command),
```

The four architecture intents now fall to the default arm, which returns
`"Intent '<X>' is not fully implemented yet. Try a different command."` — a
message, not a file. They have no recognition source, so no user input reaches
this path; it only matters for a directly constructed `ParsedCommand`.

- [ ] **Step 4: Delete the four generators**

```bash
git rm CodeGenerationEngine/Core/ServiceGenerator.cs CodeGenerationEngine/Core/RepositoryGenerator.cs CodeGenerationEngine/Core/ControllerGenerator.cs CodeGenerationEngine/Core/ApiGenerator.cs
```

- [ ] **Step 5: Run the build and the test**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror`
Expected: success. A compile error naming one of the deleted classes means a
caller exists that the earlier grep missed — wire it out, do not restore the file.

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~UnsupportedCommandArtifactTests"`
Expected: PASS (4 cases).

- [ ] **Step 6: Commit**

```bash
git add -A CodeGenerationEngine/Core CodeGenerator.Tests/Core/UnsupportedCommandArtifactTests.cs
git commit -m "refactor(engine): delete the four fake architecture generators"
```

---

### Task 10: Whole-branch verification

**Files:** none — verification only.

- [ ] **Step 1: Full solution suite**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`
Expected: all pass, count ≥ 1183 + the new tests. `ProcessRunnerTests` timing out
under full-suite load is a known pre-existing flake — re-run it alone to confirm,
do not chase it.

- [ ] **Step 2: CI-parity build**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror`
Expected: clean. This is the gate CI uses; the Debug `CodeGenerator` build hides
analyzer errors.

- [ ] **Step 3: Rebuild the CLI before probing it**

`dotnet test` leaves the CLI's `bin` stale, which makes a working fix look dead.

Run: `dotnet build CodeGenerator/CodeGenerator.csproj -c Debug`

- [ ] **Step 4: Real-CLI probe with piped stdin**

Create a scratch folder with a short path (the scratchpad blows MAX_PATH), e.g.
`D:\scg-probe`, and drive the CLI non-interactively. Piped stdin works —
`CommandLineEditor` falls back to `ReadLine` when redirected.

```bash
mkdir -p /d/scg-probe
printf 'create a Product class with properties Sku string and Discount\nrename property Sku to Code in Product\nProduct inherits AuditableBase\nvalidate project\nexit\n' \
  | dotnet run --project CodeGenerator/CodeGenerator.csproj
```

Confirm from the transcript:
- `Product.cs` is created, and `Discount` is typed `decimal` (not `int`);
- the rename reports success and says references were not updated;
- `Product inherits AuditableBase` reports the class not found (there is no such
  class in the probe folder) — a specific message, not "Could not determine
  intent";
- `validate project` prints real diagnostics or a compile-clean line, and never
  the word "stub";
- no `[CodeGenerationEngine] Heuristic inferred` line appears anywhere.

Then add an `AuditableBase.cs` to the folder and re-run the inherit line to
confirm the success path edits the file.

- [ ] **Step 5: Commit any probe fixes, then report**

Report the suite count, the Release build result, and the probe transcript
findings. Do not claim completion without all three.

---

## Deviations from the spec, for the record

1. **The guards build their own `ParsedCommand`.** The spec said both new guards
   would delegate field extraction to `CommandEntityExtractor`. They must not:
   `CommandParser.Normalize` rewrites `entity`/`model`/`object` → `class` and
   `field` → `property` (`CommandParser.cs:12-31`), so a base class named
   `Entity` would arrive as `class`. `ClassesInProject` and `UseProjectRe`
   already set the precedent for filling fields from the guard's own groups.
   No `CommandEntityExtractor` change is needed for `RenameProperty` at all.
2. **The conformance corpus compares directly, not normalized.** The spec
   expected the two heuristics to return different spellings. Both now return the
   same six, so normalization would only hide drift.
3. **The base type is inserted at index 0, not appended.** Appending produces
   CS1722 ("base class must come before any interfaces"). The spec said "append".
4. **`validate project`'s handler returns `true` when it finds errors.** The spec
   implied the bool tracked whether the code compiles. It tracks whether the
   command ran, per the `Handled` = success contract; the report carries the
   verdict.
