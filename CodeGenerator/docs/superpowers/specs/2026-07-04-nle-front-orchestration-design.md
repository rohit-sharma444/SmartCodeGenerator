# Design: NLE-led front driving CodeGenerator (dependency inversion via a bridge)

**Date:** 2026-07-04
**Status:** Approved design, pending implementation plan
**Supersedes the coupling direction of:** `2026-07-04-nle-intent-augmentation-design.md`
(the +25 scoring logic from that work is kept and re-wired, not discarded).

## Goal

Make NaturalLanguageEngine (NLE) the **front** of the flow: the user's natural
language is analyzed by NLE first, and NLE's output feeds into CodeGenerator,
which generates the code. Concretely, invert the current dependency so NLE sits
at the **top** of the graph instead of the bottom, and route NLE's detected
action verb into CodeGenerator's intent detection.

## Background & the two hard constraints

The previous change wired `CodeGenerationEngine -> NLE` (CodeGenerator's parser
calls NLE internally). The user now wants the opposite: **NLE -> CodeGenerator**.

1. **Circular reference.** A project graph cannot contain both directions. So the
   NLE dependency must move to the top and CodeGenerationEngine must stop
   referencing NLE.
2. **NLE is domain-blind.** For `create a Customer class with Email string` NLE
   yields only generic output (Action `create`, Person `a Customer`, Object
   `Email`) — never "class", "property", or "type = string". So NLE cannot fully
   specify generated code. Its realistic contribution is the **action verb**,
   which selects intent; the domain details (class name, typed properties,
   methods) still come from CodeGenerator's own text extraction.

`SemanticResult` (and every NLE model) is an NLE type, so any code that touches
NLE's output must reference NLE. Therefore all NLE-facing code must live **above**
CodeGenerationEngine.

## Decisions (locked)

- **Structure:** a thin orchestrator front (not making the NLE *project* itself
  the entry — that would make a general-purpose NLP library depend on a C# code
  generator, backwards coupling).
- **Link:** feed-in / invert the coupling. NLE runs once at the top; its verb is
  injected into CodeGenerator, which no longer references NLE.
- **Front app:** reuse the existing `CodeGenerator.exe` (keep its REPL, history,
  project management) as the NLE-led front.
- **New library name:** `NaturalLanguageBridge`.

## Architecture

Before: `CodeGenerator.exe -> CodeGenerationEngine -> NLE`

After:

```
CodeGenerator.exe (front REPL)
   -> NaturalLanguageBridge -> { NaturalLanguageEngine , CodeGenerationEngine }
   -> CodeGenerationEngine            (types it already uses directly)
CodeGenerationEngine                  -> (no NLE reference)
NaturalLanguageEngine                 -> (nothing)
```

No cycle. `NaturalLanguageBridge` is the real orchestrator; the exe is a thin
REPL shell over it. CodeGenerationEngine becomes NLE-agnostic and accepts a
plain `string? nlpAction`.

## Data flow (per command)

```
REPL reads a line
  -> NaturalLanguageBridge.NaturalLanguagePipeline.Handle(input, ...)
       analysis = NlpActionProvider.Analyze(input)     // NLE runs ONCE
       onAnalyzed(analysis)                             // exe prints NLE's output
       nlpAction = NlpActionProvider.GetPrimaryAction(analysis)   // e.g. "create"
       CommandHandlingService.ProcessInput(input, ..., nlpAction)
            -> IntentAnalyzer scores with the injected verb (+25)  // NLE feeds intent
            -> EntityExtractor pulls class/props/types from the text
            -> code generated & saved
```

## Components

### New: `NaturalLanguageBridge` (class library)
- Path: `CodeGenerator/NaturalLanguageBridge/NaturalLanguageBridge.csproj`
  (net10.0, `Nullable`/`ImplicitUsings` enable). References
  `NaturalLanguageEngine.csproj` and `CodeGenerationEngine.csproj`.
- `NlpActionProvider` (moved up from `CodeGenerationEngine/Core/Nlp/`,
  namespace becomes `NaturalLanguageBridge`). Keeps the lazy-singleton engine and
  the serialize-with-lock behavior (NLE's pipeline is not thread-safe). API:
  - `SemanticResult? Analyze(string input)` — NLE run; `null` on blank/failure.
  - `string? GetPrimaryAction(SemanticResult? result)` — pure verb extraction.
  - `string? GetPrimaryAction(string input)` — convenience (`Analyze` then extract);
    retained so the existing NLE-backed tests keep working.
- `NaturalLanguagePipeline` — the orchestrator:
  - `PipelineOutcome Handle(string input, ref ProjectMetaData? project, List<PropertyRule> rules, string workspaceRoot, Action<ProjectMetaData?> setLastCreated, Action<SemanticResult?>? onAnalyzed = null)`
  - Runs `Analyze` once, invokes `onAnalyzed(analysis)` (so the caller can display
    NLE's output *before* generation), extracts the verb, calls
    `CommandHandlingService.ProcessInput(..., nlpAction)`, and returns
    `PipelineOutcome { bool Handled; SemanticResult? Analysis; }`.

### Changed: `CodeGenerationEngine` (drop NLE, accept injected signal)
- `CodeGenerationEngine.csproj`: remove the `ProjectReference` to NLE.
- Delete `Core/Nlp/NlpActionProvider.cs` (moved to the bridge).
- `IntentAnalyzer`: remove `using CodeGenerationEngine.Core.Nlp;` and the internal
  `NlpActionProvider.GetPrimaryAction(sentence)` call. Add an injected parameter:
  `Detect(string sentence, string? nlpAction = null)` and
  `DetectWithConfidence(string sentence, string? nlpAction)`. `GetNlpActionScore`
  (+25) and `CalculateScore(..., nlpAction)` are unchanged in logic.
- `SemanticAnalyzer.Analyze(string input, string? nlpAction = null)` — pass
  `nlpAction` to `IntentAnalyzer.Detect(input, nlpAction)`.
- `CommandHandlingService.ProcessInput(..., string? nlpAction = null)` and
  `ParseInput(input, workspaceRoot, string? nlpAction)` — thread `nlpAction` down
  to `SemanticAnalyzer.Analyze`.
- All new parameters default to `null`, so any existing caller keeps compiling and
  degrades to pure keyword scoring when no signal is supplied.

### Changed: `CodeGenerator.exe` (NLE-led front)
- `CodeGenerator.csproj`: add `ProjectReference` to `NaturalLanguageBridge`
  (keeps the existing `CodeGenerationEngine` reference).
- `App/CodeGeneratorStarter.cs`: construct one `NaturalLanguagePipeline`; in the
  `Run` loop, replace the direct `CommandHandlingService.ProcessInput(...)` call
  with `pipeline.Handle(input, ref currentProject, _configuredRules,
  _workspaceRoot, setLast, onAnalyzed: DisplayNleAnalysis)`. Add a private
  `DisplayNleAnalysis(SemanticResult?)` that prints intent + entities (skips
  quietly when `null`).

### Solution files
- Add `NaturalLanguageBridge` to `SmartCodeGenerator.slnx` (and to
  `CodeGenerator.slnx` if that solution is used for builds).

## Testing

- **`IntentAnalyzer` injection tests** (in `CodeGenerator.Tests`, no NLE needed —
  pass `nlpAction` strings directly):
  - Regression rows: canonical commands with their realistic verb still resolve to
    the expected `IntentType`.
  - Value-add: `Detect("validate Foo", "validate")` -> `ValidateProject`, and
    `Detect("validate Foo", null)` -> `Unknown` (proves the injected signal is what
    lifts it past the 40 threshold).
  - Additive-only: a null signal leaves scores unchanged.
- **Bridge tests** (add a `NaturalLanguageBridge` project reference to
  `CodeGenerator.Tests`; real NLE engine):
  - `NlpActionProvider.GetPrimaryAction` returns the correct verb for command
    strings and `null` for blank/non-actionable input (the moved tests).
  - `NaturalLanguagePipeline.Handle` over a temp workspace routes
    `create a Customer class with Name string` to `Handled == true` with a
    non-null `Analysis`, and `onAnalyzed` receives the analysis (end-to-end
    NLE -> codegen path).
- Full existing `CodeGenerator.Tests` suite stays green.

## Error handling

- `NlpActionProvider.Analyze` catches all NLE exceptions and returns `null`
  (NLE throws on blank input); `GetPrimaryAction(null)` returns `null`.
- A `null` signal makes CodeGenerator fall back to today's pure keyword scoring,
  so a missing/failed NLE analysis never breaks command handling.
- The REPL keeps functioning if NLE analysis fails; `DisplayNleAnalysis(null)`
  prints nothing.

## Risks & mitigations

- **Rework of just-built code.** The +25 logic and its intent stay put; only the
  *source* of the verb changes (injected vs self-fetched). Optional-parameter
  defaults keep every existing call site compiling.
- **Perceived value vs. reality.** NLE "feeding" generation means its *action
  verb* selects intent; domain details remain CodeGenerator's job. This is the
  ceiling of what NLE's output can contribute and is stated plainly so the result
  isn't over-sold.
- **Two solutions may list the projects differently.** Add the bridge to whichever
  solution the build/test commands actually use; verify a clean solution build.
