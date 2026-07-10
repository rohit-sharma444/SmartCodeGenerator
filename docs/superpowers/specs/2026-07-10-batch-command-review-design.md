# Batch Command Detection & Review — Design Spec

**Date:** 2026-07-10
**Status:** Approved (design)
**Branch:** `29-new-feature-add-comments`
**Issue:** #29

## Goal

Let the user enter several sentences at once, show them the commands the CLI
detected, and execute **only** the ones they approve. If any approved action
fails, revert the whole batch.

Today `CodeGeneratorStarter.Run()` reads one line, and
`NaturalLanguagePipeline.Handle` parses and executes it in a single breath. The
user never sees what the CLI understood before it acts. For a destructive
command such as `delete class Customer`, a misparse is discovered only after the
file is gone.

This spec adds a `batch` command that inverts that order: detect everything
first, present it, execute the approved subset, and roll back on failure.

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

So a preview can be built from outside `CodeGenerationEngine` without modifying
it. `ParsedCommand` **is** the "structured action" issue #29 asks for; nothing
new needs inventing.

`IntentType.Unknown` already exists and already has a handler, giving detection
a natural definition of unactionable.

The CLI also has a precedent for this interaction. `BuildAppCommand` presents a
candidate domain model and loops on approve/cancel/edit, with the pure editing
logic living in the `DomainDiscovery` library (`Review/ModelEditor.cs`) and only
the console glue in the CLI project. This design follows that split.

### Where mutable state lives

Rollback has to restore all of it, so it is worth listing:

| State | Location |
|---|---|
| Generated class files | inside `workspaceRoot` |
| `dotnet new` project trees | inside `workspaceRoot` (via `DotNetProjectFactory`) |
| Recycle bin (`DeletedClassRegistry`) | inside `workspaceRoot` |
| Learning store | `<workspaceRoot>/.codegen/learned.json` **and** the in-memory `LearnedIntentStore.Current` that writes it |
| **Project registry** | **`AppContext.BaseDirectory/ProjectManager.dat` — outside the workspace** |
| `ProjectManager.CurrentProject` | in-memory static |
| **`ContextManager.Current`** | **in-memory static `SemanticContext`, mutated by `SemanticAnalyzer.Analyze`** |
| **`SessionContext`** | **in-memory static class, mutated by `EntityExtractor.Extract`** |

A workspace-only snapshot would silently miss the last four.

It would also miss the in-memory half of the learning store, which this table
originally recorded as a file and nothing else. Restoring `learned.json` is not
enough: `LearnedIntentStore.Current` is process-wide, survives the restore
holding whatever the rolled-back commands taught it, and the next `Save()`
writes all of it back. `Restore` therefore re-reads the file into memory.

The two contexts deserve special mention, because **parsing a sentence mutates
process-wide state twice over**, and both leaks defeat this feature.

`SemanticAnalyzer.Analyze` calls `ContextResolver.Resolve(model)`, and
`model.Context` **is** `ContextManager.Current` — a process-wide singleton.
`Resolve` writes `Context.LastEntity`, and later fills each entity's
`ParentName` from it.

`EntityExtractor.Extract` writes `SessionContext.CurrentClass` whenever a
sentence names a class or interface (`EntityExtractor.cs:39`, `:56`), and reads
it back as a fallback when a sentence names none (`:107`). `SessionContext.Clear()`
is never called anywhere in the codebase.

Parsing therefore has side effects, which matter here in a way they never did
for single commands:

- **Detection would pollute everything after it.** Detecting lines 1..N leaves
  `LastEntity` at line *N*'s entity, so executing line 1 afterwards re-parses it
  against context that exists only because the user looked at the review list.
  Worse, previewing `create a Customer class` — even if the user rejects it —
  leaves `SessionContext.CurrentClass = "Customer"` set permanently, so a later
  command that omits a class name silently resolves against a class that was
  never created.
- **Rollback must restore both.** Executed handlers mutate them too.

Both are reachable and settable: `ContextManager.Current` exposes the live
object with settable properties (`CurrentProject`, `CurrentClass`,
`CurrentNamespace`, `CurrentFile`, `LastEntity`), and `SessionContext` exposes
static settable properties (`CurrentProject`, `CurrentClass`,
`CurrentNamespace`). A single `ParsingStateSnapshot` captures and restores both,
so the two can never drift apart.

Note also that `DeleteClass` is not a true delete: `ClassDeletionService` moves
the file to the recycle bin, and `ClassRestoreService` brings it back.

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
5. **Execution stops at the first failure and rolls the whole batch back.** The
   batch is all-or-nothing, not a prefix of applied actions.
6. **Rollback is a workspace snapshot + exact restore**, not per-action inverse
   commands.
7. **An oversized workspace warns and defaults to abort**, rather than silently
   running without rollback.
8. **Comments (item 7 of the issue) are scoped to the files this branch
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
against live state. It also means no intent handler in the
`CommandHandlingService` dispatch table has to change, so none of them can
regress.

**Amended during implementation.** The original wording claimed the execution
path was *completely unmodified*. That turned out to be untenable: rollback
triggers on a handler returning `false`, and two places returned `true` after
doing nothing — `Dispatch` for the 21 `IntentType` values with no handler, and
`HandleCreateClass` after printing validation errors. A batch counted those as
applied and skipped its rollback. Both now report failure, and detection asks
`CommandHandlingService.IsSupported` so an unrunnable intent is never offered
for approval in the first place. The handlers themselves are still untouched.

The cost is that the preview may differ from what runs. The design accepts this
and mitigates it by printing the resolved target as each action executes.

### Why snapshot instead of per-action inverse commands

An undo log needs an inverse for each of the ~20 mutating intents:
`CreateClass` → delete the file, `DeleteClass` → `ClassRestoreService`,
`CreateProject` → remove the directory tree (but only if it did not pre-exist),
`AddValidation` → restore the file's prior content, and so on.

Each inverse is a separate chance to be subtly wrong, and the failure mode of a
wrong inverse is **deleting the user's code instead of restoring it**. Worse,
every future handler silently has no inverse until someone remembers to write
one.

Every handler bottoms out in the filesystem. One snapshot is therefore
generically correct for all of them, present and future. It costs a copy; the
undo log costs correctness in twenty places.

### Why detection lives in the bridge

`CodeGenerator.Tests` references `NaturalLanguageBridge` (there are already
`Bridge/` tests) but **not** the `CodeGenerator` exe. Detection logic placed in
the CLI project would be untestable, and detection — deciding whether a sentence
is a command — is exactly the part most likely to be wrong.

## Design

Four new files in `NaturalLanguageBridge`, one new file in
`CodeGenerationEngine/Core`, one new file in `CodeGenerator/App`, one modified.
**No existing file in `CodeGenerationEngine` or `NaturalLanguageEngine` is
modified.**

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

No console, no disk. Per line it runs `NlpActionProvider.GetPrimaryAction` for
the verb, then `SemanticAnalyzer.Analyze` → `IntentAnalyzer.DetectWithConfidence`
→ `EntityExtractor.Extract`. Blank lines are dropped. Input order is preserved.

`Detect` captures a `ParsingStateSnapshot` on entry and restores it in a
`finally`, because `SemanticAnalyzer.Analyze` mutates `ContextManager.Current`
and `EntityExtractor.Extract` mutates `SessionContext` (see *Where mutable state
lives*). **Previewing a batch must leave the process exactly as it found it**,
or execution — and every command after it — re-parses against context that only
exists because the user looked at the list.

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

### `CodeGenerationEngine/Core/WorkspaceSnapshot.cs`

New file; nothing existing in the project changes. It lives here rather than in
the bridge because restoring means touching `ProjectManager` and its registry.

```csharp
public sealed record SnapshotSizing(int FileCount, long Bytes);

public sealed class WorkspaceSnapshot : IDisposable
{
    public static SnapshotSizing Measure(string workspaceRoot);
    public static WorkspaceSnapshot Capture(string workspaceRoot); // throws on failure
    public string SnapshotPath { get; }
    public void Restore();      // exact restore; on throw, snapshot is retained
    public void Dispose();      // deletes the temp copy
}
```

**Excluded from capture:** `bin`, `obj`, `.git`, `TestResults`, `node_modules`.
These are load-bearing, not cosmetic — this repository is 483 files / 15 MB with
them excluded and 3,091 files with them included.

**Captured beyond the workspace:** `AppContext.BaseDirectory/ProjectManager.dat`,
stored alongside the tree, plus the in-memory `ProjectManager.CurrentProject` and
a `ParsingStateSnapshot` (covering `ContextManager.Current` and `SessionContext`).

`Restore()` makes the workspace match the snapshot exactly:

1. delete files present now but absent from the snapshot (this is what removes a
   `dotnet new` project tree),
2. overwrite files whose contents differ,
3. recreate files that were deleted,
4. remove directories left empty,
5. restore `ProjectManager.dat` and call `ProjectManager.LoadProject()`,
6. reset `ProjectManager.CurrentProject` to the captured value,
7. restore the `ParsingStateSnapshot` — both `ContextManager.Current` and
   `SessionContext`.

`Capture` failing (disk full, permissions) aborts the batch **before any action
executes**. `Restore` failing leaves the workspace in an unknown state, so the
snapshot directory is deliberately **not** deleted and its path is printed.
Losing the snapshot on a failed restore is the worst outcome this feature can
produce, and is the one case where leaking a temp directory is correct.

### `CodeGenerator/App/BatchCommand.cs`

Console glue, mirroring `BuildAppCommand`. Reads the block, calls `Detect`,
prints the numbered list, walks the approval prompts, snapshots, executes.

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
line 2 is visible to `create class Bar` on line 3; on rollback it is reset to
the captured value.

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
  ├── nothing approved ──> done, no snapshot taken
  ▼
WorkspaceSnapshot.Measure  ──over cap──> warn, [p]roceed / [a]bort (Enter = abort)
  │
  ▼
WorkspaceSnapshot.Capture   (failure => abort, nothing executed)
  │
  ▼
for each approved, in original order:
    pipeline.Handle(sentence, ref currentProject, ...)  [re-parses, executes]
    on exception or Handled == false:
        snapshot.Restore()   ──> revert everything, report, stop
  │
  ▼
all succeeded: snapshot.Dispose()
summary: applied / failed / rolled back / not attempted / rejected / unactionable
```

Worked example, failing batch:

```
CodeGenerator >> batch
... Create project Foo.
... Create class Bar.
... This is just a note to self.
... Delete class Legacy.
...

Detected 4 lines, 3 actionable:

  [1] CreateProject  Foo
  [2] CreateClass    Bar
   -  (skipped)      "This is just a note to self."  — not understood
  [3] DeleteClass    Legacy

  [1] CreateProject  Foo      approve? (y/n) y
  [2] CreateClass    Bar      approve? (y/n) y
  [3] DeleteClass    Legacy   approve? (y/n) y

Snapshotting workspace (483 files, 15 MB)...
Executing 3 approved actions...
  [1] CreateProject Foo -> Foo/Foo.csproj   OK
  [2] CreateClass   Bar -> Foo/Bar.cs       OK
  [3] DeleteClass   Legacy                  FAILED: no class 'Legacy'

Rolling back...
  reverted [2] CreateClass   Bar
  reverted [1] CreateProject Foo
  workspace restored to pre-batch state

Aborted. 0 applied, 1 failed, 2 rolled back, 0 not attempted,
         0 rejected, 1 unactionable.  (4 lines accounted for)
```

## Error handling

| Case | Behaviour |
|---|---|
| Line throws during parse | Unactionable `DetectedAction`, `Note` = error message |
| Line resolves to `Unknown` | Unactionable, `Note` = "not understood" |
| Empty block | "Nothing to do." Return without prompting |
| No actionable lines | Print list, return without prompting |
| EOF at approval prompt | Cancel batch, execute nothing |
| Bare Enter at approval prompt | Re-prompt |
| Nothing approved | Return; no snapshot taken |
| Workspace over cap (5,000 files or 200 MB) | Warn, prompt `[p]roceed`/`[a]bort`; Enter or EOF aborts |
| `Capture` fails | Abort before executing anything |
| Approved action throws | Restore, report, stop |
| Approved action returns `false` | Restore, report, stop |
| `Restore` fails | Keep the snapshot directory, print its path, tell the user the workspace is in an unknown state |

**What rollback does not undo.** Console output already printed. Side effects
outside the workspace that are not `ProjectManager.dat` — chiefly the NuGet
package cache `dotnet new` may populate, which is harmless. Files written by
another process into the workspace *while the batch runs* will be clobbered by
the restore; batching is an interactive, single-operator activity and this is
accepted.

**Summary invariant.** Every non-blank input line lands in exactly one bucket —
applied, failed, rolled back, not attempted, rejected, or unactionable — and the
six counts sum to the number of non-blank lines read. Decision 2 exists to stop
a sentence vanishing unnoticed; a summary that dropped one would reintroduce the
problem at the last step. Worth an assertion in the end-to-end check.

Note that `applied` is always either the full set of approved actions or zero.
There is no partial state, so a failing batch reports `0 applied`.

## Testing

Unit tests in `CodeGenerator.Tests/` (MSTest, matching the project):

**`Bridge/BatchCommandDetectorTests`**
- Several actionable sentences produce the expected `IntentType`s in input order.
- A prose line yields `IsActionable == false`, a null `Preview`, and a `Note`.
- Blank lines are dropped.
- A mixed batch reports the right actionable count.
- **Detection over create/delete sentences writes nothing to the workspace**,
  asserted against a temp directory. This is the test that proves
  present-before-execute actually holds.
- **Detection leaves both parsing singletons unchanged**, even when the batch's
  sentences name entities. Set `ContextManager.Current.LastEntity` and
  `SessionContext.CurrentClass` to sentinels before `Detect`, assert both
  survive. Both assertions must be discriminating: `LastEntity` is written by
  `ContextResolver.Resolve`, and `SessionContext.CurrentClass` by
  `EntityExtractor.Extract` on any `create class` sentence. Asserting on
  `ContextManager.Current.CurrentClass` instead would prove nothing — nothing in
  the detection path ever writes it.

**`Bridge/ActionFormatterTests`** — one case per intent family, plus the fallback.

**`Bridge/ApprovalReplyTests`** — `y`/`yes`/`n`/`no`/`null`/empty/garbage, and
case insensitivity. Empty must map to `Reprompt`, never `Approve`.

**`Core/WorkspaceSnapshotTests`** — against temp directories:
- capture then restore with no changes is a no-op;
- a file created after capture is deleted by restore;
- a file modified after capture has its original contents restored;
- a file deleted after capture is recreated;
- a directory created after capture is removed;
- `bin`/`obj`/`.git` contents are neither captured nor disturbed by restore;
- `ProjectManager.dat` is captured and restored;
- `ContextManager.Current` and `SessionContext` are captured and restored;
- `Measure` counts exclude the excluded directories;
- when `Restore` fails, the snapshot directory still exists.

`BatchCommand.cs` itself is not unit-tested — `CodeGenerator.Tests` cannot
reference the CLI exe. It is kept nearly logic-free (a reader, a loop, and
`Console` calls) precisely so that little is lost. It is covered instead by
driving the real CLI end to end, which is also the only way to prove the prompt
loop and the rollback path behave; a green unit suite would not.

The end-to-end check must include a deliberately failing batch — approve
`create project Foo` then `delete class Nonexistent` — and assert the workspace
afterwards is byte-identical to before.

## Comments (issue item 7)

Scoped per decision 8:

- XML doc comments on `DetectedAction`, `BatchCommandDetector`,
  `ActionFormatter`, `ApprovalReply`, `WorkspaceSnapshot`, `BatchCommand` —
  every type and member.
- Doc comments on the `CodeGeneratorStarter` members this branch changes.
- Inline comments only where they state a constraint the code cannot show,
  matching the existing house style (e.g. why Enter must not default to approve,
  and why a failed restore must not delete the snapshot).

The full sweep — 147 of 284 source files have no `///` comment at all — is
tracked as a follow-up issue referencing #29. It is orthogonal to batch
detection, and bundling a 284-file docs diff with a behaviour change would make
both unreviewable.

## Out of scope

- Per-action inverse commands / an undo log. Superseded by the snapshot.
- Skipping the snapshot when every approved action is read-only
  (`list`/`show`/`analyze`/`help`). A worthwhile optimization, but it needs a
  maintained set of which intents mutate; add it once the feature is proven.
- Editing a misparsed action in place before approving it (the
  `BuildAppCommand` full edit loop). Rejected in favour of the simpler y/n walk;
  the user re-enters the sentence.
- Batch support in `RunOnce()`.
- Splitting a single line into multiple sentences. `EngineBuildResult` exposes
  only `Analyzer`, so reaching NLE's `SentenceSplitter` would mean constructing
  it with its own `ITokenizer` and `ILanguageRepository` — real coupling for a
  case the block format already discourages. A line holding two sentences parses
  as one intent, and the user sees that in the review list before anything runs.
