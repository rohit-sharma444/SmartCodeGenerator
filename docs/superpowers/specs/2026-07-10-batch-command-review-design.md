# Batch Command Detection & Review — Design Spec

**Date:** 2026-07-10
**Status:** Approved (design)
**Branch:** `29-new-feature-add-comments`
**Issue:** #29

## Goal

Let the user enter several sentences at once, show them the commands the CLI
detected, and execute **only** the ones they approve.

Today `CodeGeneratorStarter.Run()` reads one line, and
`NaturalLanguagePipeline.Handle` parses and executes it in a single breath. The
user never sees what the CLI understood before it acts. For a destructive
command such as `delete class Customer`, a misparse is discovered only after the
file is gone.

This spec adds a `batch` command that inverts that order: detect everything
first, present it, then execute the approved subset.

## Background

`CommandHandlingService.ProcessInput` already separates the two halves
internally:

```csharp
var command = ParseInput(input, workspaceRoot, nlpAction);  // -> ParsedCommand
bool result = Dispatch(context);                            // acts on disk
```

Both `ParseInput` and `Dispatch` are private, but the three stages `ParseInput`
composes are all public:

```csharp
SemanticAnalyzer.Analyze(input, nlpAction)      // -> SemanticModel
IntentAnalyzer.DetectWithConfidence(semantic)   // -> IntentMatch
EntityExtractor.Extract(semantic, intent, root) // -> ParsedCommand
```

So a preview can be built from outside `CodeGenerationEngine` without touching
it. `ParsedCommand` **is** the "structured action" issue #29 asks for; nothing
new needs inventing.

`IntentType.Unknown` already exists and already has a handler, giving detection
a natural definition of unactionable.

The CLI also has a precedent for this interaction. `BuildAppCommand` presents a
candidate domain model and loops on approve/cancel/edit, with the pure editing
logic living in the `DomainDiscovery` library (`Review/ModelEditor.cs`) and only
the console glue in the CLI project. This design follows that split.

## Decisions

Settled during brainstorming, recorded because each rules out an alternative
that looks reasonable from the code alone:

1. **Input is a multi-line block.** `batch` opens the block; a blank line or EOF
   closes it. One line is one candidate command.
2. **Unrecognized lines are listed, not dropped.** They appear in the review
   list marked unactionable, and cannot be approved. Silently dropping them
   would hide a misread sentence the user intended as a command.
3. **Approval is a per-item `y`/`n` walk**, not select-by-number.
4. **Approved sentences are re-parsed at execution time**, not dispatched from
   the stored preview.
5. **Execution stops at the first failure.** No rollback.
6. **Comments (item 7 of the issue) are scoped to the files this branch
   touches.** The remaining sweep becomes a follow-up issue.

### Why re-parse instead of dispatching the preview

Actions in a batch depend on state earlier actions create.

```
create project Foo.
use project Foo.
create class Bar.
```

`EntityExtractor.Extract` resolves targets against `workspaceRoot`, and
`Dispatch` mutates `currentProject` through the caller's `ref`. When line 3 is
parsed up front, `Foo` does not exist and no project is selected, so its
`ParsedCommand` targets the workspace. Dispatching that stored command would
write `Bar.cs` to the wrong directory.

Re-parsing each approved sentence immediately before it runs — by replaying the
raw text through the existing `NaturalLanguagePipeline.Handle` — resolves it
against live state. It also means the execution path is **completely
unmodified**, so none of the 40+ intent handlers in the `CommandHandlingService`
dispatch table can regress.

The cost is that the preview may differ from what runs. The design accepts this
and mitigates it by printing the resolved target as each action executes.

### Why detection lives in the bridge

`CodeGenerator.Tests` references `NaturalLanguageBridge` (there are already
`Bridge/` tests) but **not** the `CodeGenerator` exe. Detection logic placed in
the CLI project would be untestable, and detection — deciding whether a sentence
is a command — is exactly the part most likely to be wrong.

## Design

Three new files in `NaturalLanguageBridge`, one new file in `CodeGenerator/App`,
one modified. **No changes to `CodeGenerationEngine` or
`NaturalLanguageEngine`.**

### `NaturalLanguageBridge/DetectedAction.cs`

```csharp
public sealed record DetectedAction(
    string Sentence,          // raw text, replayed verbatim at execution
    IntentType Intent,        // Unknown => unactionable
    double Confidence,
    ParsedCommand? Preview,   // null when Unknown or when parsing threw
    string? Note)             // why it is unactionable, when it is
{
    public bool IsActionable => Intent != IntentType.Unknown;
}
```

Carrying `Sentence` on the record is what makes decision 4 work: `Preview` is
for the human, `Sentence` is what runs.

### `NaturalLanguageBridge/BatchCommandDetector.cs`

```csharp
public static IReadOnlyList<DetectedAction> Detect(
    IEnumerable<string> lines, string workspaceRoot)
```

Pure: no console, no disk. Per line it runs `NlpActionProvider.GetPrimaryAction`
for the verb, then the three public parse stages. Blank lines are dropped. Input
order is preserved.

**Detect never throws.** A line that throws, or that resolves to
`IntentType.Unknown`, becomes an unactionable `DetectedAction` whose `Note`
carries the reason. One garbage line does not destroy the batch.

Because detection calls the parse stages directly rather than `ProcessInput`, it
never reaches `LearningCoordinator.RecordOutcome`. Previewing a batch teaches the
learning layer nothing, and rejected actions teach it nothing — only approved,
executed actions are recorded.

### `NaturalLanguageBridge/ActionFormatter.cs`

Renders one `DetectedAction` as a display line. `ParsedCommand` carries ~25
fields, nearly all empty for any given intent, so this switches on intent family
to pick the salient one:

| Intent family | Field shown |
|---|---|
| Project | `ProjectName` |
| Class | `ClassName`, plus `NewClassName` for renames |
| Property | `PropertyName : PropertyType` |
| Method | `MethodName` |
| Anything else | intent name alone |

Separate from the detector because it is presentation, and the most likely to
churn.

### `NaturalLanguageBridge/ApprovalReply.cs`

```csharp
public enum ApprovalReply { Approve, Reject, Cancel, Reprompt }
public static ApprovalReply Parse(string? line);
```

`y`/`yes` approves, `n`/`no` rejects, `null` (EOF) cancels the whole batch, and
anything else — **including a bare Enter** — re-prompts. Enter must not default
to approve: a held-down Return key would otherwise walk straight through a
`DeleteClass`.

Lives in the bridge, not the CLI, so it can be unit-tested.

### `CodeGenerator/App/BatchCommand.cs`

Console glue, mirroring `BuildAppCommand`. Reads the block, calls `Detect`,
prints the numbered list, walks the approval prompts, executes.

```csharp
internal static bool IsBatch(string input);
internal void Handle(
    ref ProjectMetaData? currentProject,
    List<PropertyRule> configuredRules,
    string workspaceRoot,
    Action<ProjectMetaData?> setLastCreatedProject,
    NaturalLanguagePipeline pipeline);
```

`currentProject` is threaded by `ref` from `Run()` so that `use project Foo` on
line 2 is visible to `create class Bar` on line 3.

Like `BuildAppCommand.RunReviewLoop`, a `null` from `Console.ReadLine()` (EOF /
closed stdin) is treated as cancel and never busy-loops.

### `CodeGenerator/App/CodeGeneratorStarter.cs` (modified)

One dispatch check in `Run()`, beside the existing `BuildAppCommand` check:

```csharp
if (BatchCommand.IsBatch(input))
{
    _batch.Handle(ref currentProject, _configuredRules,
                  _workspaceRoot, r => ProjectManager.CurrentProject = r, _pipeline);
    continue;
}
```

`RunOnce()` deliberately gains **no** batch support. It is the non-interactive
scripting path, and a batch that prompts for approval is meaningless there.

## Data flow

```
batch
  │
  ▼
read lines until blank line or EOF
  │
  ▼
BatchCommandDetector.Detect(lines, workspaceRoot)      [no disk writes]
  │  per line: GetPrimaryAction -> SemanticAnalyzer
  │            -> IntentAnalyzer -> EntityExtractor
  ▼
IReadOnlyList<DetectedAction>
  │
  ▼
print numbered list (unactionable lines marked, not numbered)
  │
  ▼
per-item y/n walk  (EOF => cancel entire batch, execute nothing)
  │
  ▼
for each approved, in original order:
    pipeline.Handle(sentence, ref currentProject, ...)  [re-parses, executes]
    on exception or Handled == false  => stop
  │
  ▼
summary: succeeded / failed / not attempted / skipped
```

Worked example:

```
CodeGenerator >> batch
... Create class Customer.
... Add property Name string.
... This is just a note to self.
... Delete class Legacy.
...

Detected 4 lines, 3 actionable:

  [1] CreateClass    Customer
  [2] CreateProperty Name : string
   -  (skipped)      "This is just a note to self."  — not understood
  [3] DeleteClass    Legacy

  [1] CreateClass    Customer        approve? (y/n) y
  [2] CreateProperty Name : string   approve? (y/n) n
  [3] DeleteClass    Legacy          approve? (y/n) y

Executing 2 approved actions...
  [1] CreateClass Customer -> Customer.cs   OK
  [3] DeleteClass Legacy                    FAILED: no class 'Legacy'

Aborted. 1 succeeded, 1 failed, 0 not attempted,
         1 rejected, 1 unactionable.  (4 lines accounted for)
```

## Error handling

| Case | Behaviour |
|---|---|
| Line throws during parse | Unactionable `DetectedAction`, `Note` = error message |
| Line resolves to `Unknown` | Unactionable, `Note` = "not understood" |
| Empty block | "Nothing to do." Return without prompting |
| No actionable lines | Print list, return without prompting |
| EOF at approval prompt | Cancel batch, execute nothing |
| Bare Enter at prompt | Re-prompt |
| Approved action throws | Print `FAILED: <message>`, stop |
| Approved action returns `false` | Print `FAILED`, stop |

**There is no rollback.** A batch that fails on action 3 of 5 leaves actions 1
and 2 applied to disk. The summary distinguishes *failed* from *not attempted*
so the user can see exactly where it stopped, rather than the design pretending
the batch was atomic.

**Summary invariant.** Every non-blank input line lands in exactly one bucket —
succeeded, failed, not attempted, rejected, or unactionable — and the five
counts sum to the number of non-blank lines read. Decision 2 exists to stop a
sentence vanishing unnoticed; a summary that dropped one would reintroduce the
problem at the last step. Worth an assertion in the end-to-end check.

## Testing

Unit tests in `CodeGenerator.Tests/Bridge/` (MSTest, matching the project):

**`BatchCommandDetectorTests`**
- Several actionable sentences produce the expected `IntentType`s in input order.
- A prose line yields `IsActionable == false`, a null `Preview`, and a `Note`.
- Blank lines are dropped.
- A mixed batch reports the right actionable count.
- **Detection over create/delete sentences writes nothing to the workspace**,
  asserted against a temp directory. This is the test that proves
  present-before-execute actually holds.

**`ActionFormatterTests`** — one case per intent family, plus the fallback.

**`ApprovalReplyTests`** — `y`/`yes`/`n`/`no`/`null`/empty/garbage, and case
insensitivity. Empty must map to `Reprompt`, never `Approve`.

`BatchCommand.cs` itself is not unit-tested — `CodeGenerator.Tests` cannot
reference the CLI exe. It is kept nearly logic-free (a reader, a loop, and
`Console` calls) precisely so that little is lost. It is covered instead by
driving the real CLI end to end, which is also the only way to prove the prompt
loop behaves; a green unit suite would not.

## Comments (issue item 7)

Scoped per decision 6:

- XML doc comments on `DetectedAction`, `BatchCommandDetector`,
  `ActionFormatter`, `ApprovalReply`, `BatchCommand` — every type and member.
- Doc comments on the `CodeGeneratorStarter` members this branch changes.
- Inline comments only where they state a constraint the code cannot show,
  matching the existing house style (e.g. why Enter must not default to approve).

The full sweep — 147 of 284 source files have no `///` comment at all — is
tracked as a follow-up issue referencing #29. It is orthogonal to batch
detection, and bundling a 284-file docs diff with a behaviour change would make
both unreviewable.

## Out of scope

- Rollback / transactional batches.
- Editing a misparsed action in place before approving it (the
  `BuildAppCommand` full edit loop). Rejected in favour of the simpler y/n walk;
  the user re-enters the sentence.
- Batch support in `RunOnce()`.
- Splitting a single line into multiple sentences. `EngineBuildResult` exposes
  only `Analyzer`, so reaching NLE's `SentenceSplitter` would mean constructing
  it with its own `ITokenizer` and `ILanguageRepository` — real coupling for a
  case the block format already discourages. A line holding two sentences parses
  as one intent, and the user sees that in the review list before anything runs.
