# NLP Entity Extraction + Seam Cutover (Plan 2 of the separation) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `NaturalLanguageBridge` the sole producer of `ParsedCommand` from user text, so raw user text stops entering `CodeGenerationEngine` as a runtime entry point: move `EntityExtractor`'s prose-parsing half into the bridge, add an orchestrating front, flip the `ProcessInput(string)` seam to `Execute(ParsedCommand)`, and close the three handler call sites that still re-parse raw input.

**Architecture:** The bridge already owns intent classification (`NlpCommandMapper`, Plan 1). This plan adds the field-extraction half beside it: a `CommandEntityExtractor` (the moved prose regex, still parsing the raw sentence via `CommandParser.Normalize` — **not** rebased on NLE entities, per the spec's post-Plan-1 correction) and a `CommandFront` orchestrator that runs the literal table → residual pattern guards → mapper → extractor to build one `ParsedCommand`. `CodeGenerationEngine.CommandHandlingService` gains a public `Execute(ParsedCommand, …)` (its old post-parse body) and loses `ProcessInput(string, …)` once every caller routes through the bridge.

**Tech Stack:** C# / .NET 10, MSTest. New production code in `NaturalLanguageBridge`; edits in `CodeGenerationEngine` (`CommandHandlingService`, `ProjectManager`, `EntityExtractor`) and `CodeGenerator` (CLI). Tests in `CodeGenerator.Tests`.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-10-nlp-codegen-separation-design.md`, incl. its **post-Plan-1 correction** (EntityExtractor moves as-is, raw-sentence regex; not rebased on NLE entities). Plan 1 exit report: `.superpowers/sdd/plan-1-exit-report.md`.
- **Do not touch `NaturalLanguageEngine/`.** No code-gen vocabulary is added to NLE.
- **The bridge owns all prose parsing.** `CodeGenerationEngine` must, at the end of this plan, receive a finished `ParsedCommand` and never analyze raw user text as an entry point. `ParsedCommand.OriginalSentence` still carries the sentence as provenance; handlers read fields, not prose.
- **No command may become unreachable at any commit.** Every one of the 22 mapper gate intents, the 6 literal-table intents, `ListClassesInProject`, and `GenerateValidator` stays reachable.
- **The 7 rule-but-no-handler intents stay dead.** `create a repository for Customer` must keep returning `false` (`CommandOutcomeTests`).
- **Preserve `SessionContext.CurrentClass` write ordering** — the moved extractor sets it at the same points it does today.
- **Every existing suite stays green at every commit** (`CodeGenerator.Tests`, `NaturalLanguageEngine.Tests`, `DomainDiscovery.Tests`). MSTest runs method-parallel; new bridge code must be pure/stateless.
- **Rebuild `CodeGenerator` before any end-to-end CLI check** — `dotnet test` leaves the CLI `bin` stale.
- Run all commands from the repo root `D:\SmartCodeGenerator-Repository`.

## Scope — and what is deferred to Plan 3

**In scope (this plan):** move the prose extractor to the bridge; the `CommandFront` orchestrator; `Execute(ParsedCommand)`; flip the CLI and all test call sites off `ProcessInput(string)`; close the three raw-text handler sinks; remove `ProcessInput`/`ParseInput`; slim `EntityExtractor` to its code/file half.

**Deferred to Plan 3 (deletion of the duplicate NLP stack), with evidence.** The spec calls phase-5 deletion "mechanical cleanup." The inbound-reference graph proves otherwise — surviving subsystems depend on the deletion-list files:

| Deletion-list file | Live callers that survive this plan |
|---|---|
| `TextTokenizer` | `LearnedIntentMemory`, `IntentLearningEngine`, `SemanticIntentAnalyzer` |
| `Tokenizer` | `LearnedIntentMemory`, `IntentLearningEngine`, `SemanticIntentAnalyzer` |
| `EnglishLanguageModel` | `LearnedIntentMemory`, `NaturalLanguageCodeEngine` |
| `IntentAnalyzer` | `NaturalLanguageCodeEngine` |
| `SemanticAnalyzer` | `NaturalLanguageCodeEngine`, `SemanticCompilerRunner`, `ParsingStateSnapshot` |

Deleting these requires rewiring the learning layer and `NaturalLanguageCodeEngine` off them, and deciding the fate of ~7 old-stack test files (`IntentAnalyzerLearningTests`, `IntentAnalyzerNlpTests`, `TwoMethodDiagnosticTests`, etc.). That is a design effort, written after this plan's cutover is green so the compiler enumerates the true orphans. Only `Dictionary/Synonyms.cs` (33k lines, referenced solely by `IntentAnalyzer`) orphans cleanly — it still waits for Plan 3 because its sole caller does. This plan leaves the old stack dormant-but-compiling; the double intent detection disappears the moment the CLI stops calling `ProcessInput` (Task 5), with no separate work.

---

## File Structure

- **Create** `NaturalLanguageBridge/CommandEntityExtractor.cs` — the prose-parsing half of `EntityExtractor`, moved verbatim, re-based to take a raw sentence and normalize it with `CommandParser.Normalize`. Fills a `ParsedCommand`'s fields for a given `IntentType`.
- **Create** `NaturalLanguageBridge/CommandFront.cs` — orchestrator. `Parse(input, workspace) → ParsedCommand` and `Run(input, ref project, …) → bool`. Runs literal table → pattern guards → `NlpCommandMapper` → `CommandEntityExtractor`; `Run` then calls `CommandHandlingService.Execute`.
- **Modify** `CodeGenerationEngine/Service/CommandHandlingService.cs` — add `Execute(ParsedCommand, …)`; retire `ProcessInput`/`ParseInput`; the 3 sink handlers read command fields.
- **Modify** `CodeGenerationEngine/Core/ProjectManager.cs` — `UseProject`/`ListClassesInProject` take a resolved project name.
- **Modify** `CodeGenerationEngine/Core/EntityExtractor.cs` — slim to `GenerateValidator` + `ExtractProperties(string classFile)` once its prose methods have moved.
- **Modify** `NaturalLanguageBridge/NaturalLanguagePipeline.cs`, `CodeGenerator/App/BatchCommand.cs` — call the bridge front.
- **Modify** the ~8 test files that call `CommandHandlingService.ProcessInput`.
- **Create** test files under `CodeGenerator.Tests/Bridge/` for the extractor and the front.

---

## Task 1: Move the prose extractor into the bridge (additive)

Copy `EntityExtractor`'s prose-parsing methods into a new bridge class that parses the raw sentence. Additive: the original `EntityExtractor` is untouched, so nothing breaks. The CodeGenerationEngine copy is slimmed later (Task 7), once its last caller (`ParseInput`) is gone.

**Files:**
- Create: `NaturalLanguageBridge/CommandEntityExtractor.cs`
- Create: `CodeGenerator.Tests/Bridge/CommandEntityExtractorTests.cs`

**Interfaces:**
- Consumes: `CodeGenerationEngine.Models.ParsedCommand`, `CodeGenerationEngine.Models.IntentType`, `CodeGenerationEngine.Models.SessionContext`, `CodeGenerationEngine.Core.CommandParser.Normalize(string)`.
- Produces: `NaturalLanguageBridge.CommandEntityExtractor.Extract(string rawSentence, IntentType intent, string workspace) → ParsedCommand`.

- [ ] **Step 1: Create the class by moving the prose methods verbatim**

Create `NaturalLanguageBridge/CommandEntityExtractor.cs`. Copy from `CodeGenerationEngine/Core/EntityExtractor.cs` **only these members, byte-for-byte** (their bodies are unchanged — this is a relocation, not a rewrite):

`Extract`, `ExtractInheritanceInfo`, `ExtractInterfaceInfo`, `ExtractInterfaceName`, `ExtractServiceName`, `ExtractRepositoryName`, `ExtractControllerName`, `ExtractValidationInfo`, `ExtractPropertyInfo`, `ExtractClassInfo`, `ExtractMethodInfo`, `ExtractMethods`, `ParseMethod`, `ExtractClassName`, `ExtractEnglishProperties`, `ExtractProperties(string sentence, ParsedCommand command)`, `ExtractPropertySection`, `ParseProperty`, `ExtractProjectInfo`, `ExtractRenameInfo`, `ParseParameters`, and any `private` helper they call that is **not** used by the two methods staying behind (`GenerateValidator`, `ExtractProperties(string classFile)`) — e.g. `ToPascalCase`. Do **not** move `GenerateValidator` or `ExtractProperties(string classFile)`.

Then apply exactly these four edits to the moved code:

1. Header — namespace and usings:

```csharp
using CodeGenerationEngine.Core;      // CommandParser
using CodeGenerationEngine.Models;    // ParsedCommand, IntentType, SessionContext
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NaturalLanguageBridge;

/// <summary>
/// Prose-to-<see cref="ParsedCommand"/> field extraction for code-generation
/// commands. Moved out of CodeGenerationEngine so raw user text is parsed in the
/// bridge, never in the code generator. Still regex-over-sentence: NLE's entity
/// output is too lossy for structured extraction (see the Plan 1 exit report),
/// so it parses the normalized raw sentence, not NLE entities. Stateless.
/// </summary>
public static class CommandEntityExtractor
```

2. `Extract`'s signature and its first two lines change from taking a `SemanticModel` to taking the raw sentence and normalizing it here:

```csharp
// before (in EntityExtractor):
//   public static ParsedCommand Extract(SemanticModel semantic, IntentType intent, string workspace)
//   { var sentence = semantic.NormalizedText; ... var tokens = TextTokenizer.Tokenize(sentence); ... }

public static ParsedCommand Extract(string rawSentence, IntentType intent, string workspace)
{
    var sentence = CommandParser.Normalize(rawSentence);

    var command = new ParsedCommand
    {
        Intent = intent,
        OriginalSentence = rawSentence
    };
    // ...rest of the switch is unchanged...
```

3. The one `TextTokenizer` use — the `ListMethods` branch reads `tokens[2].Normalized`. Replace the `var tokens = TextTokenizer.Tokenize(sentence);` line and that branch's token access with a plain split so the `TextTokenizer` dependency is dropped:

```csharp
// replace: var tokens = TextTokenizer.Tokenize(sentence);
var tokens = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);

// in the ListMethods branch, replace: command.ClassName = tokens[2].Normalized;
command.ClassName = tokens[2];
```

4. Leave every regex, every helper body, and the `SessionContext.CurrentClass` writes exactly as they are.

- [ ] **Step 2: Write the failing test**

`CodeGenerator.Tests/Bridge/CommandEntityExtractorTests.cs`:

```csharp
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
public class CommandEntityExtractorTests
{
    [TestMethod]
    public void Extracts_class_name_for_create_class()
    {
        var cmd = CommandEntityExtractor.Extract("create a class called Customer", IntentType.CreateClass, "");
        Assert.AreEqual("Customer", cmd.ClassName);
    }

    [TestMethod]
    public void Extracts_rename_source_and_target_positionally()
    {
        var cmd = CommandEntityExtractor.Extract("rename class Foo to Bar", IntentType.RenameClass, "");
        Assert.AreEqual("Foo", cmd.ClassName);
        Assert.AreEqual("Bar", cmd.NewClassName);
    }

    [TestMethod]
    public void Carries_the_raw_sentence_as_provenance()
    {
        var cmd = CommandEntityExtractor.Extract("create a class called Customer", IntentType.CreateClass, "");
        Assert.AreEqual("create a class called Customer", cmd.OriginalSentence);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~CommandEntityExtractorTests"`
Expected: FAIL to compile until Step 1's class exists; once it compiles, PASS. If `RenameClass` fails, confirm `ExtractRenameInfo` and its helpers were all moved (a missing private helper is the usual cause).

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~CommandEntityExtractorTests"`
Expected: PASS.

- [ ] **Step 5: Confirm nothing regressed**

Run: `dotnet test CodeGenerator.Tests`
Expected: PASS. The original `EntityExtractor` is untouched, so the old path is unchanged; this only adds the new class.

- [ ] **Step 6: Commit**

```bash
git add NaturalLanguageBridge/CommandEntityExtractor.cs CodeGenerator.Tests/Bridge/CommandEntityExtractorTests.cs
git commit -m "Move prose entity extraction into the bridge (additive)"
```

---

## Task 2: `CommandFront.Parse` — build a full `ParsedCommand` from input

Wire intent + extraction into one bridge entry that produces a `ParsedCommand`, including the two residual guards Plan 1 found un-absorbable by the literal table. Additive; still not wired into any caller.

**Files:**
- Create: `NaturalLanguageBridge/CommandFront.cs`
- Create: `CodeGenerator.Tests/Bridge/CommandFrontParseTests.cs`

**Interfaces:**
- Consumes: `LiteralCommandTable.TryResolve`, `NlpActionProvider.Analyze`, `NlpCommandMapper.ResolveIntent`, `CommandEntityExtractor.Extract`, `ParsedCommand`, `IntentType`.
- Produces: `NaturalLanguageBridge.CommandFront.Parse(string input, string workspace) → ParsedCommand`.

- [ ] **Step 1: Write the front's `Parse`**

`NaturalLanguageBridge/CommandFront.cs`:

```csharp
using System.Text.RegularExpressions;
using CodeGenerationEngine.Models;
using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageBridge;

/// <summary>
/// The single place user text becomes a <see cref="ParsedCommand"/>. Order:
/// literal meta-commands (exact table) → residual pattern guards that the
/// literal table can't express → the NLE-driven intent mapper → prose field
/// extraction. CodeGenerationEngine receives the result and never re-parses text.
/// Stateless.
/// </summary>
public static class CommandFront
{
    // Plan 1 found these two guards un-absorbable by LiteralCommandTable:
    // "classes in <project>" (NLE drops the trailing "in"), and the bare
    // \bvalidator\b substring. Both read the raw sentence here, ahead of the mapper.
    private static readonly Regex ClassesInProject =
        new(@"^(?:list|show|display)\s+classes\s+in\s+(?<project>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Validator =
        new(@"\bvalidator\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ParsedCommand Parse(string input, string workspace)
    {
        var text = (input ?? string.Empty).Trim();

        if (LiteralCommandTable.TryResolve(text, out var literalIntent))
            return new ParsedCommand { Intent = literalIntent, OriginalSentence = input ?? string.Empty };

        var classesIn = ClassesInProject.Match(text);
        if (classesIn.Success)
            return new ParsedCommand
            {
                Intent = IntentType.ListClassesInProject,
                ProjectName = classesIn.Groups["project"].Value.Trim(),
                OriginalSentence = input ?? string.Empty
            };

        if (Validator.IsMatch(text))
        {
            var validatorCmd = CommandEntityExtractor.Extract(input ?? string.Empty, IntentType.GenerateValidator, workspace);
            validatorCmd.Intent = IntentType.GenerateValidator;
            return validatorCmd;
        }

        SemanticResult? analysis = NlpActionProvider.Analyze(input ?? string.Empty);
        var intent = NlpCommandMapper.ResolveIntent(analysis);
        return CommandEntityExtractor.Extract(input ?? string.Empty, intent, workspace);
    }
}
```

- [ ] **Step 2: Write the failing test**

`CodeGenerator.Tests/Bridge/CommandFrontParseTests.cs`:

```csharp
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
public class CommandFrontParseTests
{
    [TestMethod]
    public void Create_class_resolves_intent_and_name()
    {
        var cmd = CommandFront.Parse("create a class called Customer", "");
        Assert.AreEqual(IntentType.CreateClass, cmd.Intent);
        Assert.AreEqual("Customer", cmd.ClassName);
    }

    [TestMethod]
    public void Classes_in_project_guard_sets_intent_and_project_name()
    {
        var cmd = CommandFront.Parse("list classes in MyProject", "");
        Assert.AreEqual(IntentType.ListClassesInProject, cmd.Intent);
        Assert.AreEqual("MyProject", cmd.ProjectName);
    }

    [TestMethod]
    public void Literal_meta_command_bypasses_the_mapper()
    {
        var cmd = CommandFront.Parse("show learning", "");
        Assert.AreEqual(IntentType.ShowLearning, cmd.Intent);
    }

    [TestMethod]
    public void Unrecognized_input_is_Unknown()
    {
        var cmd = CommandFront.Parse("juggle the flamingo", "");
        Assert.AreEqual(IntentType.Unknown, cmd.Intent);
    }

    [TestMethod]
    public void Repository_intent_stays_dead()
    {
        // CreateRepository has no handler; the mapper must not reach it.
        var cmd = CommandFront.Parse("create a repository for Customer", "");
        Assert.AreNotEqual(IntentType.CreateRepository, cmd.Intent);
    }
}
```

- [ ] **Step 3: Run to verify it fails, then passes**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~CommandFrontParseTests"`
Expected: FAIL before Step 1 compiles, PASS after. If `Classes_in_project_guard...` fails, the guard regex or its group name is off. If `Repository_intent_stays_dead` fails, a mapper table row is reaching a dead intent — stop and re-check `NlpCommandMapper.Build()`; do not "fix" it by adding a handler.

- [ ] **Step 4: Full suite**

Run: `dotnet test CodeGenerator.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add NaturalLanguageBridge/CommandFront.cs CodeGenerator.Tests/Bridge/CommandFrontParseTests.cs
git commit -m "Add CommandFront.Parse: input to ParsedCommand in the bridge"
```

---

## Task 3: `CommandHandlingService.Execute(ParsedCommand, …)`

Extract the post-parse body of `ProcessInput` into a public `Execute` that takes a prebuilt command. `ProcessInput` stays for now and delegates, so every existing caller keeps working.

**Files:**
- Modify: `CodeGenerationEngine/Service/CommandHandlingService.cs`
- Create: `CodeGenerator.Tests/Core/ExecuteParsedCommandTests.cs`

**Interfaces:**
- Produces: `CommandHandlingService.Execute(ParsedCommand command, ref ProjectMetaData? currentProject, List<PropertyRule> configuredRules, string workspaceRoot, Action<ProjectMetaData?> setLastCreatedProject) → bool`.

- [ ] **Step 1: Add `Execute`, make `ProcessInput` delegate**

In `CommandHandlingService.cs`, replace the body of `ProcessInput` (the current lines 30–66) so parsing stays there but dispatch moves into `Execute`:

```csharp
public static bool ProcessInput(
    string input,
    ref ProjectMetaData? currentProject,
    List<PropertyRule> configuredRules,
    string workspaceRoot,
    Action<ProjectMetaData?> setLastCreatedProject,
    string? nlpAction = null)
{
    // The "demo" shortcut bypasses parsing entirely and prints generated code.
    if (input.Equals("demo", StringComparison.OrdinalIgnoreCase))
    {
        var (demoCode, _) = BuildDemo();
        Console.WriteLine(StripCommentMarkers(demoCode));
        return true;
    }

    var command = ParseInput(input, workspaceRoot, nlpAction);
    return Execute(command, ref currentProject, configuredRules, workspaceRoot, setLastCreatedProject);
}

/// <summary>
/// Executes an already-parsed command. This is the seam: the bridge is the
/// producer of <see cref="ParsedCommand"/>; CodeGenerationEngine only executes.
/// </summary>
public static bool Execute(
    ParsedCommand command,
    ref ProjectMetaData? currentProject,
    List<PropertyRule> configuredRules,
    string workspaceRoot,
    Action<ProjectMetaData?> setLastCreatedProject)
{
    var context = new CommandExecutionContext
    {
        RawInput = command.OriginalSentence,
        Command = command,
        WorkspaceRoot = workspaceRoot,
        ConfiguredRules = configuredRules,
        CurrentProject = currentProject,
        SetLastCreatedProject = setLastCreatedProject
    };

    bool result = Dispatch(context);

    LearningCoordinator.RecordOutcome(command.OriginalSentence, command.Intent, result, command);

    currentProject = context.CurrentProject;
    return result;
}
```

Note: `RawInput` now comes from `command.OriginalSentence`. `ProcessInput`'s own `ParseInput` builds a `ParsedCommand` whose `OriginalSentence` is set by `EntityExtractor.Extract`; confirm the old extractor sets it (it sets `OriginalSentence` in the CreateClass/other branches — if any branch leaves it empty, set `command.OriginalSentence = input` in `ParseInput` before returning). The three sink handlers still read `ctx.RawInput`; with `RawInput = OriginalSentence` they keep working through Task 6, which then removes that reliance.

- [ ] **Step 2: Write the failing test**

`CodeGenerator.Tests/Core/ExecuteParsedCommandTests.cs`:

```csharp
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class ExecuteParsedCommandTests
{
    [TestMethod]
    public void Execute_runs_the_handler_for_a_prebuilt_command()
    {
        LearnedIntentStore.UseInMemory();
        var workspace = Path.Combine(Path.GetTempPath(), "cg_exec_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var command = new ParsedCommand
            {
                Intent = IntentType.CreateClass,
                ClassName = "Customer",
                OriginalSentence = "create a class called Customer"
            };
            ProjectMetaData? project = null;

            var handled = CommandHandlingService.Execute(command, ref project, [], workspace, _ => { });

            Assert.IsTrue(handled);
            Assert.IsNotEmpty(Directory.GetFiles(workspace, "Customer.cs", SearchOption.AllDirectories));
        }
        finally
        {
            LearnedIntentStore.UseInMemory();
            try { Directory.Delete(workspace, recursive: true); } catch (IOException) { }
        }
    }

    [TestMethod]
    public void Execute_reports_failure_for_a_dead_intent()
    {
        LearnedIntentStore.UseInMemory();
        var command = new ParsedCommand { Intent = IntentType.CreateRepository, OriginalSentence = "create a repository for Customer" };
        ProjectMetaData? project = null;

        var handled = CommandHandlingService.Execute(command, ref project, [], Path.GetTempPath(), _ => { });

        Assert.IsFalse(handled);
    }
}
```

- [ ] **Step 3: Run to verify it passes**

Run: `dotnet test CodeGenerator.Tests --filter "FullyQualifiedName~ExecuteParsedCommandTests"`
Expected: PASS.

- [ ] **Step 4: Full suite**

Run: `dotnet test CodeGenerator.Tests`
Expected: PASS — `ProcessInput` still exists and delegates, so all existing callers are unchanged.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Service/CommandHandlingService.cs CodeGenerator.Tests/Core/ExecuteParsedCommandTests.cs
git commit -m "Extract Execute(ParsedCommand) from ProcessInput"
```

---

## Task 4: `CommandFront.Run` + flip the CLI

Give the front a Parse+Execute convenience, and route the CLI (`NaturalLanguagePipeline`, `BatchCommand`) through it. After this task the CLI no longer calls `CommandHandlingService.ProcessInput`.

**Files:**
- Modify: `NaturalLanguageBridge/CommandFront.cs`
- Modify: `NaturalLanguageBridge/NaturalLanguagePipeline.cs`
- Modify: `CodeGenerator/App/BatchCommand.cs` (call site at line 211)

**Interfaces:**
- Produces: `CommandFront.Run(string input, ref ProjectMetaData? project, List<PropertyRule> configuredRules, string workspace, Action<ProjectMetaData?> setLastCreatedProject, Action<SemanticResult?>? onAnalyzed = null) → bool`.

- [ ] **Step 1: Add `Run` to `CommandFront`**

Append to `CommandFront`:

```csharp
using CodeGenerationEngine.Service;  // add to the using block at the top
// ...

/// <summary>Parse then execute one command. The demo shortcut and the NLE
/// analysis callback (for display) live here so callers need only one entry.</summary>
public static bool Run(
    string input,
    ref ProjectMetaData? project,
    List<PropertyRule> configuredRules,
    string workspace,
    Action<ProjectMetaData?> setLastCreatedProject,
    Action<SemanticResult?>? onAnalyzed = null)
{
    if ((input ?? string.Empty).Trim().Equals("demo", StringComparison.OrdinalIgnoreCase))
    {
        var (demoCode, _) = CommandHandlingService.BuildDemo();
        Console.WriteLine(demoCode.Replace("//", string.Empty));
        onAnalyzed?.Invoke(null);
        return true;
    }

    if (onAnalyzed is not null)
        onAnalyzed(NlpActionProvider.Analyze(input ?? string.Empty));

    var command = Parse(input ?? string.Empty, workspace);
    return CommandHandlingService.Execute(command, ref project, configuredRules, workspace, setLastCreatedProject);
}
```

(The `onAnalyzed` re-analysis keeps the display behaviour `NaturalLanguagePipeline` has today. NLE is serialized and cheap here; a second analyze for display only fires when a display callback is supplied.)

- [ ] **Step 2: Point `NaturalLanguagePipeline.Handle` at `Run`**

Replace `NaturalLanguagePipeline.Handle`'s body (current lines 26–39):

```csharp
public PipelineOutcome Handle(
    string input,
    ref ProjectMetaData? project,
    List<PropertyRule> configuredRules,
    string workspaceRoot,
    Action<ProjectMetaData?> setLastCreatedProject,
    Action<SemanticResult?>? onAnalyzed = null)
{
    SemanticResult? captured = null;
    bool handled = CommandFront.Run(
        input, ref project, configuredRules, workspaceRoot, setLastCreatedProject,
        a => { captured = a; onAnalyzed?.Invoke(a); });

    return new PipelineOutcome(handled, captured);
}
```

- [ ] **Step 3: `BatchCommand` needs no change**

`CodeGenerator/App/BatchCommand.cs:211` calls `pipeline.Handle(...)` on the `NaturalLanguagePipeline` instance, which Step 2 just flipped onto the bridge; there are no direct `CommandHandlingService.ProcessInput` calls anywhere in `CodeGenerator/` (verified). So `BatchCommand` is already routed. Re-confirm before moving on: `grep -rn "ProcessInput" CodeGenerator/ --include=*.cs` should return nothing.

- [ ] **Step 4: Full suite + CLI smoke**

Run: `dotnet test CodeGenerator.Tests`
Expected: PASS. Then rebuild and smoke the CLI:

Run: `dotnet build CodeGenerator && printf 'create a class called Customer\nexit\n' | dotnet run --project CodeGenerator`
Expected: the class-creation output appears (the CLI now routes through the bridge). Delete any `prompts.json` written to the repo root before committing.

- [ ] **Step 5: Commit**

```bash
git add NaturalLanguageBridge/CommandFront.cs NaturalLanguageBridge/NaturalLanguagePipeline.cs CodeGenerator/App/BatchCommand.cs
git commit -m "Flip the CLI onto the bridge front (Parse + Execute)"
```

---

## Task 5: Migrate the test call sites off `ProcessInput`

Every remaining `CommandHandlingService.ProcessInput(...)` caller is a test. Route each through `CommandFront.Run`, which parses in the bridge exactly as the CLI now does. This is what makes `ProcessInput` deletable in Task 7.

**Files (all under `CodeGenerator.Tests/`):**
- Modify: `Core/CommandOutcomeTests.cs`, `Core/LearningCommandsTests.cs`, `Core/PipelineLearningTests.cs`, `Core/SampleProjectsReproTests.cs`, `Core/SaveWorkspaceReproTests.cs` (4 call sites), `Core/TwoMethodDiagnosticTests.cs`, `Core/WorkspaceRootResolutionTests.cs`.

**Interfaces:**
- Consumes: `CommandFront.Run`.

- [ ] **Step 1: Replace each call**

The transformation is mechanical and identical everywhere. Each site looks like:

```csharp
CommandHandlingService.ProcessInput(input, ref project, rules, workspace, setLast /*, nlpAction*/);
```

becomes:

```csharp
CommandFront.Run(input, ref project, rules, workspace, setLast);
```

Drop the trailing `nlpAction` argument (the bridge derives it). Add `using NaturalLanguageBridge;` to each file. Where a test wrapped the call in a private `Process(...)`/helper (e.g. `CommandOutcomeTests.Process`, line 42), change only that one helper. Do not change assertions — behaviour is identical, since `Run` parses the same text through the same bridge the CLI uses.

Enumerate every site first: `grep -rn "CommandHandlingService.ProcessInput" CodeGenerator.Tests/`.

- [ ] **Step 2: Full suite**

Run: `dotnet test CodeGenerator.Tests`
Expected: PASS. In particular `CommandOutcomeTests.ProcessInput_IntentWithNoHandler_ReportsFailure` (the `create a repository for Customer` → `false` pin) must stay green — if it flips to green-via-wrong-path or fails, the mapper is reaching a dead intent; stop and inspect.

- [ ] **Step 3: Commit**

```bash
git add CodeGenerator.Tests/
git commit -m "Migrate test call sites from ProcessInput to CommandFront.Run"
```

---

## Task 6: Close the three raw-text handler sinks

Three handlers still consume `ctx.RawInput` (`command.OriginalSentence`) and re-parse prose inside `CodeGenerationEngine`. Make them read the fields the bridge already fills.

**Files:**
- Modify: `CodeGenerationEngine/Core/ProjectManager.cs` (`UseProject`, `ListClassesInProject`)
- Modify: `CodeGenerationEngine/Service/CommandHandlingService.cs` (the 3 handler registrations / `HandleGenerateValidator`)

**Interfaces:**
- Produces: `ProjectManager.UseProject(string projectRef) → bool`, `ProjectManager.ListClassesInProject(string projectName) → bool` (both now take a resolved value, not raw prose).

- [ ] **Step 1: `UseProject` takes a project reference, not a sentence**

`ProjectManager.UseProject` currently slices `input["use project ".Length..]`. Replace that first line so it accepts the already-resolved reference:

```csharp
public static bool UseProject(string projectRef)
{
    var value = (projectRef ?? string.Empty).Trim();
    bool success;
    if (int.TryParse(value, out var index))
        success = ProjectManager.Select(index);
    else
        success = ProjectManager.Select(value);
    // ...unchanged from here...
```

The bridge must fill `command.ProjectName` for `UseProject`. Add a guard branch to `CommandFront.Parse` mirroring the `ClassesInProject` one, before the mapper:

```csharp
private static readonly Regex UseProjectRe =
    new(@"^(?:use|select|switch|open|activate)\s+project\s+(?<project>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
// ...in Parse, after the ClassesInProject block:
var useMatch = UseProjectRe.Match(text);
if (useMatch.Success)
    return new ParsedCommand
    {
        Intent = IntentType.UseProject,
        ProjectName = useMatch.Groups["project"].Value.Trim(),
        OriginalSentence = input ?? string.Empty
    };
```

Then change the handler registration in `CommandHandlingService.Handlers`:

```csharp
[IntentType.UseProject] = ctx => ProjectManager.UseProject(ctx.Command.ProjectName),
```

- [ ] **Step 2: `ListClassesInProject` takes a project name**

Replace `ProjectManager.ListClassesInProject`'s first line:

```csharp
public static bool ListClassesInProject(string projectName)
{
    projectName = (projectName ?? string.Empty).Trim();
    // ...unchanged lookup by projectName from here...
```

Change the handler registration:

```csharp
[IntentType.ListClassesInProject] = ctx => ProjectManager.ListClassesInProject(ctx.Command.ProjectName),
```

(The `ClassesInProject` guard from Task 2 already fills `ProjectName`.)

- [ ] **Step 3: `GenerateValidator` handler stops re-extracting**

In `HandleGenerateValidator`, delete the line `EntityExtractor.ExtractClassName(ctx.RawInput, command);` — the bridge's validator guard already ran `CommandEntityExtractor.Extract`, so `command.ClassName` is filled. The rest of the handler (project select, `EntityExtractor.GenerateValidator(command)`) is unchanged.

- [ ] **Step 4: Write/adjust tests**

Add to `CommandFrontParseTests`:

```csharp
[TestMethod]
public void Use_project_guard_sets_project_name()
{
    var cmd = CommandFront.Parse("use project MyApp", "");
    Assert.AreEqual(IntentType.UseProject, cmd.Intent);
    Assert.AreEqual("MyApp", cmd.ProjectName);
}
```

- [ ] **Step 5: Full suite**

Run: `dotnet test CodeGenerator.Tests`
Expected: PASS. Any test that exercised `use project X` / `list classes in X` / a validator command through `CommandFront.Run` now flows field-based; confirm they stay green.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerationEngine/Core/ProjectManager.cs CodeGenerationEngine/Service/CommandHandlingService.cs NaturalLanguageBridge/CommandFront.cs CodeGenerator.Tests/Bridge/CommandFrontParseTests.cs
git commit -m "Close the three raw-text handler sinks; handlers read command fields"
```

---

## Task 7: Remove `ProcessInput`/`ParseInput` and slim `EntityExtractor`

Now that no caller passes raw text to `CodeGenerationEngine`, delete the string entry points and the moved prose methods, leaving `EntityExtractor` with only its code/file half.

**Files:**
- Modify: `CodeGenerationEngine/Service/CommandHandlingService.cs`
- Modify: `CodeGenerationEngine/Core/EntityExtractor.cs`

- [ ] **Step 1: Confirm `ProcessInput` has no callers**

Run: `grep -rn "ProcessInput" CodeGenerationEngine/ CodeGenerator/ CodeGenerator.Tests/ --include=*.cs | grep -v "/obj/"`
Expected: only the definition in `CommandHandlingService.cs` (and possibly test *names* containing the word — those are fine). If any real caller remains, migrate it (Task 4/5 pattern) before continuing.

- [ ] **Step 2: Delete `ProcessInput` and `ParseInput`**

Remove the `ProcessInput` method and the private `ParseInput` method from `CommandHandlingService.cs`. `Execute`, `Dispatch`, `BuildDemo`, and the handlers stay. This orphans the `SemanticAnalyzer`/`IntentAnalyzer`/`EntityExtractor.Extract` calls that `ParseInput` made — that is the point; their deletion is Plan 3, but they are now unreferenced by the command path.

- [ ] **Step 3: Slim `EntityExtractor` to its code/file half**

In `CodeGenerationEngine/Core/EntityExtractor.cs`, delete every method that was moved in Task 1 (the whole prose-extraction set). Keep exactly `GenerateValidator(ParsedCommand)` and `ExtractProperties(string classFile)` and any private helper they still use. Remove now-unused `using`s (e.g. the `TextTokenizer` reference is gone). If `GenerateValidator`/`ExtractProperties(classFile)` shared a helper with the moved code (e.g. `ToPascalCase`), keep a copy of that helper here.

- [ ] **Step 4: Build and full suite**

Run: `dotnet build && dotnet test`
Expected: PASS across all test projects. Compiler errors here name any residual coupling — resolve by confirming the symbol belongs to the code/file half (keep it) or the prose half (it should have moved). Do not delete any `SemanticAnalyzer`/`IntentAnalyzer`/`TextTokenizer` file — that is Plan 3.

- [ ] **Step 5: CLI smoke**

Run: `dotnet build CodeGenerator && printf 'create a class called Customer\nlist classes in Demo\nexit\n' | dotnet run --project CodeGenerator`
Expected: both commands route through the bridge and behave as before. Delete any `prompts.json` from the repo root.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerationEngine/Service/CommandHandlingService.cs CodeGenerationEngine/Core/EntityExtractor.cs
git commit -m "Remove ProcessInput/ParseInput; slim EntityExtractor to its code/file half"
```

---

## Plan 2 exit note (for Plan 3)

Record before handing off to the deletion plan:

1. **What is now dormant:** `CommandHandlingService.ParseInput` is gone, so `SemanticAnalyzer.Analyze`, `IntentAnalyzer.Detect`, and the old `EntityExtractor.Extract` are no longer on the command path. Confirm with `grep` which of the deletion-list files still have live callers (expected survivors per the scope table: the learning layer and `NaturalLanguageCodeEngine`).
2. **Learning-layer entanglement:** `LearnedIntentMemory`/`IntentLearningEngine`/`SemanticIntentAnalyzer` still call `TextTokenizer`/`Tokenizer`/`EnglishLanguageModel`; `NaturalLanguageCodeEngine` still calls `IntentAnalyzer`/`SemanticAnalyzer`/`EnglishLanguageModel`. Plan 3 must rewire or keep-minimal these before deleting.
3. **Old-stack tests:** `IntentAnalyzerLearningTests`, `IntentAnalyzerNlpTests`, `TwoMethodDiagnosticTests`, and peers characterize the duplicate stack directly. Plan 3 decides delete vs. migrate.

---

## Self-Review

**Spec coverage.** This plan implements the spec's Plan 2 phases 3 (move `EntityExtractor`, close the three sinks, preserve `SessionContext.CurrentClass` ordering) and 4 (flip `ProcessInput` → `Execute`), under the post-Plan-1 correction (raw-sentence regex, not NLE-entity rebase). Phase 5 (deletion) and phase 6 (double-detection removal) are addressed: phase 6 falls out of Task 5 automatically; phase 5 is explicitly deferred to Plan 3 with an inbound-reference table proving it is a design effort, not mechanical cleanup — a scoping decision the spec's own "Plan 2's shape changes" clause licenses.

**Placeholder scan.** No TBD/TODO. The one relocation task (Task 1) names the exact methods to move and the exact four edits rather than re-pasting ~1,000 unchanged lines — a precise instruction, not a placeholder. Every new-code step shows complete code.

**Type consistency.** `CommandEntityExtractor.Extract(string, IntentType, string) → ParsedCommand`, `CommandFront.Parse(string, string) → ParsedCommand`, `CommandFront.Run(string, ref ProjectMetaData?, List<PropertyRule>, string, Action<ProjectMetaData?>, Action<SemanticResult?>?) → bool`, and `CommandHandlingService.Execute(ParsedCommand, ref ProjectMetaData?, List<PropertyRule>, string, Action<ProjectMetaData?>) → bool` are used identically everywhere they appear. `ProjectManager.UseProject`/`ListClassesInProject` keep their `bool` return; only the argument's meaning changes (resolved value, not sentence), and both call sites are updated in the same task.

**Risk carried honestly.** The plan keeps a brief window (Tasks 1–6) where the prose extractor exists in both `EntityExtractor` and `CommandEntityExtractor`. That duplication is intentional (keeps every commit green) and is removed in Task 7. `CommandParser.Normalize` remains in `CodeGenerationEngine` because surviving code (`LearnedIntentMemory`, `NaturalLanguageCodeEngine`) still calls it; the bridge reaching across the existing bridge→engine reference to use it is consistent with the dependency arrows in the spec.
