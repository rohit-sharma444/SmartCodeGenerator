# NLP Stack Retirement (Plan 3 of the separation) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rewire the last consumers off `CodeGenerationEngine`'s duplicate NLP stack, then delete it (~34k lines incl. the 33k-line `Synonyms.cs`), so only `NaturalLanguageBridge` (NLE + `NlpCommandMapper` + `CommandEntityExtractor`) does NLP. Completes the spec's boundary rule.

**Architecture:** Rewire-then-delete, sequenced so every commit stays green. Four surviving consumers still reach the old stack (`BatchCommandDetector` preview, `NaturalLanguageCodeEngine.ProcessCommand`, `LearnedIntentMemory.Signature`, `SemanticCompilerRunner`); each is rewired or has its dead path removed first. Only then is the now-orphaned stack deleted, compiler-guided. The `SemanticModel` *code-analysis* subsystem (`SemanticValidator`, `ContextResolver`, `DependencyGraphBuilder`, `GenericTypeValidator`, `SemanticModel`) is **kept** — it is not command-NLP and the spec's delete list excludes it.

**Tech Stack:** C# / .NET 10, MSTest. Changes in `CodeGenerationEngine` and `NaturalLanguageBridge`; test cleanup in `CodeGenerator.Tests`.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-10-nlp-codegen-separation-design.md`, incl. the "Plan 3 design (post-Plan-2)" section. Prior plans: `.superpowers/sdd/progress.md`.
- **Do not touch `NaturalLanguageEngine/`.**
- **Every existing suite stays green at every commit** (`CodeGenerator.Tests` baseline **216**, `NaturalLanguageEngine.Tests`, `DomainDiscovery.Tests`). MSTest method-parallel; test classes touching `SessionContext.CurrentClass` stay `[DoNotParallelize]`.
- **The 7 rule-but-no-handler intents stay dead:** `create a repository for Customer` stays non-actionable (now via `Unknown`, not `CreateRepository` — see Task 1).
- **No command becomes unreachable.** The 22 mapper intents, 6 literal-table intents, `ListClassesInProject`, `GenerateValidator`, and the `add/show validation` argument forms all keep resolving.
- **Keep the `SemanticModel` code-analysis subsystem** (`SemanticValidator`, `ContextResolver`, `DependencyGraphBuilder`, `GenericTypeValidator`, `SemanticModel`, `ContextManager`). Only the command-NLP files in the delete list (Task 6) go.
- **User-facing learning is preserved:** `LearnedIntentStore` reinforce/weaken, rephrase detection, `show/reset learning`. Only the invisible *vocabulary* learning is removed.
- Rebuild `CodeGenerator` before any CLI check (`dotnet test` leaves its `bin` stale). Run all commands from `D:\SmartCodeGenerator-Repository`.

## File Structure

- **Modify** `NaturalLanguageBridge/CommandFront.cs` — set `ParsedCommand.Confidence` (NLE-derived on the mapper path, 1.0 on deterministic literal/guard paths).
- **Modify** `NaturalLanguageBridge/BatchCommandDetector.cs` — resolve preview via `CommandFront.Parse`.
- **Modify** `CodeGenerationEngine/Core/NaturalLanguageCodeEngine.cs` — delete the dead `ProcessCommand`.
- **Modify** `CodeGenerationEngine/Core/LearnedIntentMemory.cs` — self-contained `Signature`.
- **Modify** `CodeGenerationEngine/Core/LearningCoordinator.cs` — drop the vocab-learning call.
- **Modify** `CodeGenerationEngine/Core/SemanticCompilerRunner.cs` — remove the cosmetic semantic-analysis step.
- **Modify** `CodeGenerationEngine/Core/EntityExtractor.cs` — slim to the code/file half.
- **Delete** the orphaned command-NLP files + old-stack tests (Task 6).

---

## Task 1: NLE-derived confidence + rewire `BatchCommandDetector` onto `CommandFront`

Batch *preview* still resolves intent through `SemanticAnalyzer`→`IntentAnalyzer`→`EntityExtractor.Extract`, diverging from execution (which goes through `CommandFront`). Route preview through `CommandFront.Parse` so they agree, and surface NLE's confidence for the preview display.

**Files:**
- Modify: `NaturalLanguageBridge/CommandFront.cs`
- Modify: `NaturalLanguageBridge/BatchCommandDetector.cs`
- Modify: `CodeGenerator.Tests/Bridge/BatchCommandDetectorTests.cs`

**Interfaces:**
- `ParsedCommand.Confidence` (existing `double`) is populated by `CommandFront.Parse`.
- `SemanticResult.Confidence` (existing `double` on `NaturalLanguageEngine.Models.Semantics.SemanticResult`) is the NLE source.

- [ ] **Step 1: Populate `Confidence` in `CommandFront.Parse`**

Deterministic recognitions (literal table, the four guards) get full confidence; the mapper path carries NLE's score. In `CommandFront.cs`, set `Confidence` on each returned `ParsedCommand`:

- Literal-table branch and each guard branch (`ClassesInProject`, `UseProjectRe`, `AddValidationRe`, `ShowValidationRe`, `Validator`): set `Confidence = 1.0` on the returned command (for the guard branches that call `CommandEntityExtractor.Extract`, set it on the returned `cmd` before returning).
- Mapper path (final branch): capture the analysis and carry its confidence:

```csharp
SemanticResult? analysis = NlpActionProvider.Analyze(input ?? string.Empty);
var intent = NlpCommandMapper.ResolveIntent(analysis);
var command = CommandEntityExtractor.Extract(input ?? string.Empty, intent, workspace);
command.Confidence = analysis?.Confidence ?? 0d;
return command;
```

(For the literal branch that constructs `new ParsedCommand { ... }`, add `Confidence = 1.0` to the initializer.)

- [ ] **Step 2: Write the failing test for confidence propagation**

Add to `CommandFrontParseTests.cs`:

```csharp
[TestMethod]
public void Guard_command_reports_full_confidence()
{
    var cmd = CommandFront.Parse("use project MyApp", "");
    Assert.AreEqual(1.0, cmd.Confidence);
}

[TestMethod]
public void Mapper_command_carries_a_confidence()
{
    var cmd = CommandFront.Parse("create a class called Customer", "");
    Assert.IsTrue(cmd.Confidence > 0d, $"expected NLE confidence, got {cmd.Confidence}");
}
```

- [ ] **Step 3: Run to verify, then implement Step 1 until green**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~CommandFrontParseTests"`
Expected: the two new tests fail before Step 1's edits, pass after.

- [ ] **Step 4: Rewire `BatchCommandDetector.DetectOne`**

Replace the body of `DetectOne` so it resolves through the bridge front instead of the old stack:

```csharp
private static DetectedAction DetectOne(string sentence, string workspaceRoot)
{
    try
    {
        var command = CommandFront.Parse(sentence, workspaceRoot);

        if (command.Intent == IntentType.Unknown)
            return new DetectedAction(sentence, IntentType.Unknown, command.Confidence, null, "not understood");

        // Recognizing an intent is not the same as being able to run it.
        if (!CommandHandlingService.IsSupported(command.Intent))
            return new DetectedAction(
                sentence, command.Intent, command.Confidence, null,
                $"{command.Intent} is not supported yet");

        return new DetectedAction(sentence, command.Intent, command.Confidence, command, null);
    }
    catch (Exception ex)
    {
        return new DetectedAction(sentence, IntentType.Unknown, 0, null, $"parse failed: {ex.Message}");
    }
}
```

Remove the now-unused `using CodeGenerationEngine.Core;` / `using CodeGenerationEngine.Service;` only if nothing else in the file needs them (`CommandHandlingService.IsSupported` needs `.Service`; `ParsingStateSnapshot` in `Detect` needs `.Core`). Keep both — they are still used.

- [ ] **Step 5: Update `BatchCommandDetectorTests`**

The `create a repository for Customer` case changes: the mapper resolves it to `Unknown` (it never reached `CreateRepository`), so preview now reports `Unknown` + "not understood" rather than `CreateRepository` + "not supported". Both are non-actionable — the invariant holds, the label changes. Update that test:

```csharp
// was: asserts Intent == CreateRepository, Note contains "not supported"
var actions = BatchCommandDetector.Detect(["create a repository for Customer"], root);
Assert.AreEqual(1, actions.Count);
Assert.AreEqual(IntentType.Unknown, actions[0].Intent);
Assert.IsFalse(actions[0].IsActionable);
Assert.IsNull(actions[0].Preview);
Assert.IsNotNull(actions[0].Note);
```

Leave the other batch tests (`create a Customer class` → `CreateClass`, the multi-line CreateClass/DeleteClass/ListProjects case, `the big red car` → `Unknown`, trimming) unchanged — verify they still pass through `CommandFront`.

- [ ] **Step 6: Full suite + commit**

Run: `dotnet test CodeGenerator.Tests`
Expected: PASS. Then:

```bash
git add NaturalLanguageBridge/CommandFront.cs NaturalLanguageBridge/BatchCommandDetector.cs CodeGenerator.Tests/Bridge/CommandFrontParseTests.cs CodeGenerator.Tests/Bridge/BatchCommandDetectorTests.cs
git commit -m "Rewire batch preview onto CommandFront; NLE-derived confidence"
```

---

## Task 2: Delete `NaturalLanguageCodeEngine.ProcessCommand`

`ExecuteCommand(ParsedCommand)` (the live code-gen path) does not re-analyse. Only the dead `ProcessCommand(sentence)` ties this file to `SemanticAnalyzer`/`IntentAnalyzer`/`EntityExtractor.Extract`/`EnglishLanguageModel`/`CommandParser`. Its sole caller is the old-stack test `EntityExtractorMethodTests`.

**Files:**
- Modify: `CodeGenerationEngine/Core/NaturalLanguageCodeEngine.cs`
- Delete: `CodeGenerator.Tests/Core/EntityExtractorMethodTests.cs`

- [ ] **Step 1: Confirm `ProcessCommand`'s only caller**

Run: `grep -rn "\.ProcessCommand(" CodeGenerationEngine/ CodeGenerator/ CodeGenerator.Tests/ --include=*.cs | grep -v /obj/`
Expected: only `CodeGenerator.Tests/Core/EntityExtractorMethodTests.cs`. If a production caller appears, STOP and report.

- [ ] **Step 2: Delete the method and the test file**

Remove the `ProcessCommand` method (the `public static string ProcessCommand(string sentence, string workspaceRoot)` block) from `NaturalLanguageCodeEngine.cs`. Keep `ExecuteCommand` and every private helper it uses. Delete `CodeGenerator.Tests/Core/EntityExtractorMethodTests.cs` (it tested the old `EntityExtractor.Extract` + `ProcessCommand`; that extraction behaviour is now covered by `CommandEntityExtractorTests`). Remove any `using` in `NaturalLanguageCodeEngine.cs` left unused (e.g. `System.Text` if only `ProcessCommand` used the `StringBuilder`; verify — `HandleListClasses` also uses `StringBuilder`, so keep it).

- [ ] **Step 3: Build + full suite + commit**

Run: `dotnet build && dotnet test CodeGenerator.Tests`
Expected: PASS.

```bash
git add CodeGenerationEngine/Core/NaturalLanguageCodeEngine.cs
git rm CodeGenerator.Tests/Core/EntityExtractorMethodTests.cs
git commit -m "Delete dead NaturalLanguageCodeEngine.ProcessCommand + its old-stack test"
```

---

## Task 3: Self-contained `LearnedIntentMemory.Signature`; drop vestigial vocab learning

`Signature` uses `EnglishLanguageModel.Normalize` + `TextTokenizer.Tokenize`; `LearningCoordinator` calls `IntentLearningEngine.Learn`, which writes `VocabularyRepository` that only ever fed the deleted `IntentAnalyzer`. Cut both ties.

**Files:**
- Modify: `CodeGenerationEngine/Core/LearnedIntentMemory.cs`
- Modify: `CodeGenerationEngine/Core/LearningCoordinator.cs`

**Interfaces:**
- Consumes: `CodeGenerationEngine.Core.CommandParser.Normalize(string)` (survives).

- [ ] **Step 1: Rewrite `Signature` without the old stack**

Replace the body of `LearnedIntentMemory.Signature` (keep the summary, the canonical shape, and the `string.Empty` guard):

```csharp
public static string Signature(string sentence)
{
    if (string.IsNullOrWhiteSpace(sentence))
        return string.Empty;

    // Normalize with the surviving command normalizer, then reduce to distinct,
    // lower-cased, sorted tokens so phrasings differing only in word order or
    // duplicates collapse together. (Previously also ran EnglishLanguageModel +
    // TextTokenizer, both retired with the duplicate NLP stack.)
    var normalized = CommandParser.Normalize(sentence);

    var tokens = normalized
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(t => t.ToLowerInvariant())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(n => n, StringComparer.Ordinal);

    return string.Join(' ', tokens);
}
```

Note: signatures shift format vs. the old normalizer, so previously-persisted `intents.json` entries stop matching — acceptable (learning is local runtime state; both the record and consult paths use this same function, so they stay mutually consistent).

- [ ] **Step 2: Drop the vocab-learning call**

In `LearningCoordinator.RecordOutcome`, remove the block:

```csharp
if (command != null)
    IntentLearningEngine.Learn(sentence, command);
```

Keep the surrounding `Reinforce`/`Weaken`/`Save` logic. The `command` parameter stays on `RecordOutcome`'s signature (callers pass it; it is still used for nothing else here, but removing the parameter is out of scope — leave it).

- [ ] **Step 3: Focused + full suite**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~Learning"` then `dotnet test CodeGenerator.Tests`
Expected: PASS. `PipelineLearningTests`/`LearningCommandsTests` exercise reinforce + `show/reset learning`; they must stay green (they assert on `LearnedIntentStore`, not vocabulary).

- [ ] **Step 4: Commit**

```bash
git add CodeGenerationEngine/Core/LearnedIntentMemory.cs CodeGenerationEngine/Core/LearningCoordinator.cs
git commit -m "Make learned-intent signatures self-contained; drop vestigial vocab learning"
```

---

## Task 4: Remove the cosmetic semantic step from `SemanticCompilerRunner`

`SemanticCompilerRunner.RunSemanticAndCompile` runs `SemanticAnalyzer.Analyze(code)` on generated C# and `SemanticValidator.Validate` — but prints "Validate succeeded." **unconditionally** (the result gates nothing; the real gate is `Compiler.Compile`). This is the last live tie from a survivor to `SemanticAnalyzer`. Remove the dead step, keep the user-visible message and the compile gate.

**Files:**
- Modify: `CodeGenerationEngine/Core/SemanticCompilerRunner.cs`

- [ ] **Step 1: Drop the semantic-analysis lines**

Replace lines 16–25 (the `SemanticAnalyzer.Analyze` + `SemanticValidator.Validate` + error loop + unconditional success print) with just the success message, so the method becomes the annotation-prefix + compile gate:

```csharp
// Semantic pre-check removed with the duplicate NLP stack: it ran an
// English-language analyzer over generated C# and printed success
// unconditionally, gating nothing. Compilation below is the real gate.
Console.WriteLine(" \n Validate succeeded. ");

var result = Compiler.Compile(code);
```

(Keep the `[Required]`/annotation using-injection above it and the `Compiler.Compile` result handling below it exactly as they are.) Remove the now-unused `SemanticValidator` reference if `SemanticCompilerRunner` no longer uses it — but note `SemanticValidator` itself is KEPT (other code-analysis callers); this task only stops `SemanticCompilerRunner` from calling `SemanticAnalyzer`.

- [ ] **Step 2: Build + CLI smoke + full suite**

Run: `dotnet test CodeGenerator.Tests` (expect PASS), then rebuild and smoke to confirm the CLI still prints "Validate succeeded." and "Compiled.":

`dotnet build CodeGenerator && printf 'create a class called Widget\nexit\n' | dotnet run --project CodeGenerator --no-build`
Expected: the create-class output with "Validate succeeded." and "Compiled." Delete any `prompts.json` from the repo root before committing.

- [ ] **Step 3: Commit**

```bash
git add CodeGenerationEngine/Core/SemanticCompilerRunner.cs
git commit -m "Drop cosmetic SemanticAnalyzer step from the compile runner"
```

---

## Task 5: Slim `EntityExtractor` to its code/file half

With `EntityExtractor.Extract` no longer called (Task 1 removed the batch caller; Task 2 removed the `ProcessCommand` caller; the command path stopped calling it in Plan 2), delete the prose methods, leaving only the code/file half.

**Files:**
- Modify: `CodeGenerationEngine/Core/EntityExtractor.cs`

- [ ] **Step 1: Confirm `Extract` (and the prose helpers) have no callers**

Run: `grep -rn "EntityExtractor\.Extract\b\|EntityExtractor\.ExtractClassName\|EntityExtractor\.ExtractMethods\|EntityExtractor\.ExtractProjectInfo\|EntityExtractor\.ExtractRenameInfo\|EntityExtractor\.ExtractPropertyInfo" CodeGenerationEngine/ CodeGenerator/ CodeGenerator.Tests/ --include=*.cs | grep -v /obj/`
Expected: no matches (the bridge uses `CommandEntityExtractor`, not `EntityExtractor`, for prose). If any remain, STOP and resolve before deleting.

- [ ] **Step 2: Delete the prose methods**

In `EntityExtractor.cs`, delete every prose-extraction method and its private helpers, keeping exactly: `GenerateValidator(ParsedCommand)`, `ExtractProperties(string classFile)`, and `ExtractNamespace(string classFile)` (the Roslyn/file helper). Remove now-unused `using`s (e.g. the `TextTokenizer`/regex imports the prose half needed — but keep `Microsoft.CodeAnalysis.CSharp[.Syntax]`, which `ExtractProperties`/`ExtractNamespace` use, and `System.Text.RegularExpressions` only if a kept method still uses it — verify). The file becomes a small code/file-analysis helper.

- [ ] **Step 3: Build + full suite + commit**

Run: `dotnet build && dotnet test CodeGenerator.Tests`
Expected: PASS. Compiler errors name any prose helper still referenced — resolve by confirming it belongs to the code/file half (keep) or was truly orphaned (delete).

```bash
git add CodeGenerationEngine/Core/EntityExtractor.cs
git commit -m "Slim EntityExtractor to its code/file (Roslyn) half"
```

---

## Task 6: Delete the orphaned duplicate NLP stack + old-stack tests

Everything that reached the old stack has been rewired. Delete the command-NLP files, compiler-guided. Keep the `SemanticModel` code-analysis subsystem.

**Files:**
- Delete (production): `Dictionary/Synonyms.cs`, `Core/TextTokenizer.cs`, `Tokenizer/Tokenizer.cs`, `Core/IntentAnalyzer.cs`, `Core/SemanticAnalyzer.cs`, `Core/EnglishLanguageModel.cs`, `Core/EntityRecognizer.cs`, `Core/RelationExtractor.cs`, `Core/SemanticIntentAnalyzer.cs`, `Core/SemanticPipeline.cs`, `Core/IntentLearningEngine.cs`, `Core/VocabularyRepository.cs`, `Core/VocabularyStore.cs` (all under `CodeGenerationEngine/`).
- Delete (tests): `CodeGenerator.Tests/Core/IntentAnalyzerLearningTests.cs`, `CodeGenerator.Tests/Core/IntentAnalyzerNlpTests.cs`.

- [ ] **Step 1: Verify each production target is orphaned before deleting**

For each file above, confirm no surviving code references its type. Run per symbol, e.g.:
`grep -rn "\bIntentAnalyzer\b\|\bSemanticAnalyzer\b\|\bTextTokenizer\b\|\bEnglishLanguageModel\b\|\bEntityRecognizer\b\|\bRelationExtractor\b\|\bSemanticIntentAnalyzer\b\|\bSemanticPipeline\b\|\bIntentLearningEngine\b\|\bVocabularyRepository\b\|\bVocabularyStore\b\|\bSynonyms\b" CodeGenerationEngine/ CodeGenerator/ CodeGenerator.Tests/ --include=*.cs | grep -v /obj/`  (also check `\bTokenizer\b` separately — it substring-matches `TextTokenizer`, so grep `[^t]Tokenizer\b` or eyeball hits)
The only hits should be the files' own definitions and the two old-stack test files (deleted below). **If a survivor outside the delete set references one of these (e.g. an unexpected `SemanticModel`-subsystem tie), STOP and report** — that is a new rewire, not a delete.

- [ ] **Step 2: Delete the files**

```bash
git rm CodeGenerationEngine/Dictionary/Synonyms.cs \
  CodeGenerationEngine/Core/TextTokenizer.cs CodeGenerationEngine/Tokenizer/Tokenizer.cs \
  CodeGenerationEngine/Core/IntentAnalyzer.cs CodeGenerationEngine/Core/SemanticAnalyzer.cs \
  CodeGenerationEngine/Core/EnglishLanguageModel.cs CodeGenerationEngine/Core/EntityRecognizer.cs \
  CodeGenerationEngine/Core/RelationExtractor.cs CodeGenerationEngine/Core/SemanticIntentAnalyzer.cs \
  CodeGenerationEngine/Core/SemanticPipeline.cs CodeGenerationEngine/Core/IntentLearningEngine.cs \
  CodeGenerationEngine/Core/VocabularyRepository.cs CodeGenerationEngine/Core/VocabularyStore.cs \
  CodeGenerator.Tests/Core/IntentAnalyzerLearningTests.cs CodeGenerator.Tests/Core/IntentAnalyzerNlpTests.cs
```

- [ ] **Step 3: Build; resolve residual references compiler-guided**

Run: `dotnet build`
For each error: if it is a surviving file still importing a deleted symbol via an unused `using`, remove the `using`; if it is a real dependency the investigation missed, STOP and report (it means a consumer was not rewired). Do NOT delete any `SemanticModel`-subsystem file (`SemanticValidator`, `ContextResolver`, `DependencyGraphBuilder`, `GenericTypeValidator`, `SemanticModel`, `ISemanticAnalyzer`, `ContextManager`) to make an error go away — those are kept; if one genuinely became orphaned, report it as a decision rather than deleting.

- [ ] **Step 4: Full suite + CLI smoke**

Run: `dotnet test` (all projects) — expect PASS.
Then rebuild and smoke a representative spread through the bridge:

`dotnet build CodeGenerator && printf 'create a class called Customer\nadd validation Email should not be blank on Email in Customer\nlist classes in Demo\nshow learning\nexit\n' | dotnet run --project CodeGenerator --no-build`
Expected: each command handled through the bridge; no crash. Delete any `prompts.json` from the repo root.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Delete the duplicate NLP stack; bridge is the only NLP path"
```

---

## Task 7: Verify the boundary rule holds

A final characterization that `CodeGenerationEngine` no longer analyzes raw user text.

**Files:**
- Create: `CodeGenerator.Tests/Bridge/NlpBoundaryTests.cs`

- [ ] **Step 1: Assert the deleted types are gone and the seam is clean**

```csharp
using System.Linq;
using System.Reflection;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
public class NlpBoundaryTests
{
    [TestMethod]
    public void CodeGenerationEngine_no_longer_carries_the_duplicate_nlp_stack()
    {
        var asm = typeof(ParsedCommand).Assembly; // CodeGenerationEngine
        string[] retired =
        {
            "IntentAnalyzer", "SemanticAnalyzer", "TextTokenizer", "Tokenizer",
            "EnglishLanguageModel", "EntityRecognizer", "RelationExtractor",
            "SemanticIntentAnalyzer", "SemanticPipeline", "IntentLearningEngine",
            "VocabularyRepository", "VocabularyStore", "Synonyms"
        };
        var present = asm.GetTypes().Select(t => t.Name).ToHashSet();
        var leaked = retired.Where(present.Contains).ToList();
        Assert.IsEmpty(leaked, "still present: " + string.Join(", ", leaked));
    }
}
```

- [ ] **Step 2: Run + commit**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~NlpBoundaryTests"` (expect PASS), then full `dotnet test`.

```bash
git add CodeGenerator.Tests/Bridge/NlpBoundaryTests.cs
git commit -m "Pin the NLP boundary: CodeGenerationEngine carries no duplicate stack"
```

---

## Self-Review

**Spec coverage.** Implements the spec's "Plan 3 design (post-Plan-2)": the four consumer rewires (Tasks 1–4), the `EntityExtractor` slim (Task 5), the compiler-guided deletion (Task 6), and old-stack test cleanup (Tasks 2, 6). The three user decisions are honored: NLE-derived batch confidence (Task 1), vestigial vocab-learning deleted (Task 3), one staged plan. Task 4 (`SemanticCompilerRunner`) is an addition beyond the spec's list, forced by discovering the last live `SemanticAnalyzer` consumer; it is a provably non-functional step (unused result, unconditional success print), so its removal loses no behaviour.

**Placeholder scan.** No TBD/TODO. Rewrites show complete code; deletions name exact files. The one judgement-bearing step (Task 6 Step 3) is explicitly a stop-and-report on any survivor tie, not a blanket delete — the correct handling given the `SemanticModel` subsystem is kept.

**Type consistency.** `ParsedCommand.Confidence` (double), `SemanticResult.Confidence` (double), `DetectedAction(string, IntentType, double, ParsedCommand?, string?)`, `CommandFront.Parse(string, string) → ParsedCommand`, and `LearnedIntentMemory.Signature(string) → string` match their definitions across tasks.

**Risk carried honestly.** The `create a repository for Customer` batch-preview label changes from "CreateRepository not supported" to "Unknown / not understood" (Task 1 Step 5) — non-actionability is preserved, only the wording changes, and the test is updated to match. Signature format changes (Task 3) reset local learned memory — acceptable runtime state. Task 6 is gated on Task 1 Step 1 / Task 5 Step 1 / Task 6 Step 1 verification greps so a missed consumer stops the plan rather than breaking the build silently.
