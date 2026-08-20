# `build app` as an interactive Blazor workflow — design

**Date:** 2026-08-17
**Status:** approved, not yet implemented

## Problem

`build app` is already a seven-stage workflow. Nothing on screen says so.

The interactive run asks for a description, an application type, a data source and ten
customization values; then it discovers a domain, offers normalization findings, runs a review
loop over the candidate model, and finally scaffolds, builds and offers to run the app. That is a
pipeline with branches, revisitable decisions and two long silences in it — presented as a column
of prompts that scroll away as they are answered.

Three costs follow from that.

**The shape is invisible.** A user answering "Do you have existing data?" cannot see that four
more questions, a Wikipedia fetch and a model review stand between them and an app. They cannot
see how far along they are, and they cannot see what a wrong answer will cost them.

**Nothing can be revisited.** Every answer is final the moment Enter is pressed. Realising at the
review loop that the wrong application type was chosen means `c`, then starting over — including
the discovery fetch.

**The model review is the wrong shape for a terminal.** `RunReviewLoop` reprints the whole
candidate model after every single edit, which is why `PrintModel` is now almost entirely
commented out (`BuildAppCommand.cs:1104`) — the output was unusable, so it was silenced, and the
review loop now edits a model the user cannot see. Renaming one class in a thirty-class domain is
`rename` → class → new name → the whole model again.

## Goal

Interactive `build app` opens a Blazor page showing the whole workflow as a diagram. Nodes tick
green as they complete, the active node expands into its panel, and a completed node can be
clicked to go back and change its answer. The workflow runs end to end in the browser — including
discovery progress, the model review, generation, the build log and the run offer. The console
prints the URL, waits, and reports the result.

## Scope

**In:** hosting the workflow in-process from the CLI project; a headless `BuildSession` state
machine; the seven stage components; the diagram; wiring the interactive branch of
`BuildAppCommand.Handle`.

**Deliberately not in scope:**

- **Redirected stdin.** The `!Console.IsInputRedirected` guard at `BuildAppCommand.cs:93` stays
  exactly where it is. A piped run keeps `InlineDataSource` parsing and the typed review loop,
  byte for byte. The existing E2E tests are the guard: **if one of them has to change, the
  boundary has leaked.**
- **A wider web UI.** One route, one workflow. No home page, no `create class`, no `status`, no
  navigation shell. `BuildSession` and `StageGraph` are written generically enough that a second
  workflow could reuse them, but nothing else ships.
- **Remote access.** Loopback only. This is a local tool for the person sitting at the machine.
- **Replacing the console wizard's code.** See "The console fallback" below.
- **New projects.** See "Where the code lives" — there are none, deliberately.

## Approach

Blazor Server, **hosted inside the CLI process**. `build app` builds a `WebApplication` on a free
loopback port, opens the browser, and blocks until the workflow reaches a terminal state.

The two alternatives were rejected:

- **Spawning a separate Blazor app** costs two processes to keep alive and kill, a `dotnet run`
  cold start, and an IPC hop to get the outcome back for the console to print. No gain — the
  server is the user's own machine.
- **Static HTML plus minimal APIs** throws away the thing that makes this easy: a Blazor Server
  circuit is already a live two-way channel, which is exactly what a stage that takes fifteen
  seconds and wants to report progress needs.

In-process is cheap because **every service in the pipeline is already console-free.**
`Console.WriteLine` appears only in `BuildAppCommand` itself; `DomainDiscoveryPipeline`,
`DataSourceReaders`, `NormalizationAnalyzer`, `ModelEditor`, `AppScaffolder` and `ProcessRunner`
all take values and return values. The web path calls the same services the console path calls.
Nothing has to be untangled first.

### Where the code lives — and why there is no new project

The components live **inside the CLI project**, under `CodeGenerator/Web/`, and
`CodeGenerator.csproj` changes from `Microsoft.NET.Sdk` to `Microsoft.NET.Sdk.Web` (keeping
`<OutputType>Exe</OutputType>`; the Web SDK is the base SDK plus web targets, and nothing in it
forces an ASP.NET entry point — `Program.cs` goes on running the REPL).

A separate `CodeGenerator.Web` project was the first design and is wrong, for a concrete reason:
**the helpers the stages need are in the CLI, and the CLI is the caller.** A separate project
cannot reference back into it.

| Needed by | Lives in |
| --- | --- |
| `ApplicationTypeMenu` (node 2) | `CodeGenerator/App/` |
| `CustomizationDefaults` (node 4) | `CodeGenerator/App/` |
| `DecisionReport` (node 6, the `why` panel) | `CodeGenerator/App/` |
| `NextSteps`, `BuildReport` (node 7) | `CodeGenerator/App/` |
| `WebAppStart`, `Interrupt`, `Output` | `CodeGenerator/App/` |

Extracting all of those is a ten-type refactor across three projects, and `BuildReport` does not
even have a clean home — it takes a `ProcessResult` from `CodeGenerationEngine`, which
`AppScaffolding` does not reference. Hosting from the CLI costs one SDK attribute and moves
nothing.

It also removes the static-web-assets question entirely: a Web SDK project handles its own
`wwwroot` and `blazor.web.js` natively, which is the thing an RCL under a console host could not
be relied on to do.

**The one check to run first** (ten minutes, before any component): that `CodeGenerator` under
`Microsoft.NET.Sdk.Web` still builds, still starts its REPL, and that `CodeGenerator.Tests` still
references it and passes. The Web SDK adds implicit `Microsoft.AspNetCore.*` usings and emits
static-web-asset manifests beside the exe; neither should matter, but it is cheap to prove before
building on it.

## Flow

```
build app  |  build app: <description>  |  build app from csv <path>
   |
   +- stdin redirected --> UNCHANGED: InlineDataSource only, no prompts,
   |                       typed review loop, target resolved at `approve`
   |
   +- interactive --> BuildAppWebHost.Run(request, workspaceRoot, ai)
                        bind 127.0.0.1:0, mint a session token,
                        open http://127.0.0.1:<port>/build?t=<token>,
                        block until the session reaches a terminal state
```

What the console shows:

```
CodeGenerator >> build app

  Opening the build workflow at http://127.0.0.1:5210/build ...
  Ctrl-C here cancels.

  Generated project at D:\Work\LibrarySystem
  Generated test project at D:\Work\LibrarySystem.Tests

CodeGenerator >>
```

What the page shows:

```
  +----------+     +----------+     +----------+     +----------+
  | Describe |---->| App type |---->|   Data   |---->|   Look   |
  |    OK    |     |    OK    |     |  ACTIVE  |     | pending  |
  +----------+     +----------+     +----+-----+     +----------+
                                         |
  +----------+     +----------+     +----v-----+
  | Generate |<----|  Review  |<----| Discover |
  | pending  |     | pending  |     | pending  |
  +----------+     +----------+     +----------+

  +-- Data --------------------------------------------+
  | ( ) No data - discover the domain from the web     |
  | (o) I have a file                                  |
  |     Format  [ csv        v]                        |
  |     Path    [ D:\data\orders.csv        ] [Browse] |
  |             or drop a file here                    |
  |                                    [Back] [Next]   |
  +----------------------------------------------------+
```

## The stages

Seven nodes. Each is one `.razor` component and one case in `BuildSession`.

### 1. Describe

A textarea. Pre-filled from the command tail, so `build app: a library system` opens with the
node already green and the workflow parked on node 2 — the shortcut stays a shortcut.

Blank is a valid answer and means "I will point at a file instead", exactly as
`BuildAppWizard.AskForDescription` allows today. Blank description **and** no data source is the
one combination the workflow refuses, and it refuses it at node 3 rather than at the end.

### 2. App type

The seven rows of `ApplicationTypeMenu.Choices`, as radio cards. Labels and order come from that
existing table — it is display-only and stays the single source, so a target added later appears
here without touching this component.

Asked before discovery for the reason it is asked before discovery today: a user who wants a Web
API should not learn it was optional after a Wikipedia fetch.

### 3. Data

Two branches, mirroring `DataSourceInterview`:

- **No data** → the discovery path at node 5.
- **A file** → a format drop-down over `DataSourceReaders.Keys`, then a location.

A format with no reader in this build (`sql` today) renders disabled with
`DataSourceReaders.NotSupportedMessage` beside it, so it is refused before it is chosen rather
than after — the same courtesy `DataSourceInterview.FormatLabel` already pays.

**The location is the one place a browser cannot do what the console does.** A file input hands
over bytes and a bare file name, never a path, and `DataSourceSpec.Location` is a path. So both
are offered:

- **A path text box** — primary. Validated server-side with `File.Exists`, keeping the
  quote-stripping in `DataSourceInterview.ResolveLocation` for paths pasted from Explorer. This
  is the honest form: the server *is* the user's machine, and a path is what the CLI takes.
- **A drop zone** (`InputFile`) — convenience. Streams to a file under the workspace's
  `.dd-cache/uploads`, and that path becomes the `Location`. `maxAllowedSize` must be raised from
  the 512 KB default; the copy is deleted when the session ends.

Both produce a `DataSourceSpec` and nothing downstream can tell them apart.

### 4. Look and feel

The ten values of `AppCustomization`, pre-filled from
`Merge(CustomizationDefaults.Load(workspaceRoot), inline)` — the remembered profile with anything
typed on the command line layered over it, which is what the console path already does at
`BuildAppCommand.cs:98`.

Collapsed behind a "Customize appearance and features" toggle, matching
`CustomizationWizard.GateTitle`: it is decoration, and a user who wants the app they came for
should be able to skip it in one click. The layout field renders only for a target where
`AppLayouts.Supports(target.Key)` — asking a console app for a window shape is nonsense.

**Validation is inline and per-field, not "keep the old value on a bad answer".** The console
wizard silently keeps the current value when an accent will not parse (`ParseAccent`), because
trapping someone in a terminal loop over a mistyped colour is worse than ignoring it. A form has
no such excuse: a bad hex shows a red field and blocks Next. Same rules
(`ThemePalette.IsValidHexColour`, `NamespacePrefix.IsValid`), better reporting.

Saved through `CustomizationDefaults.Save` on leaving the node, as the console path does —
including when the toggle was left off, because declining settled it too.

### 5. Discover

Two shapes behind one node, chosen by whether node 3 produced a source.

**No source** — `DomainDiscoveryPipeline.Discover(description)` on a background task, with a
live panel replacing the console's spinner. This is the longest silence in the tool: a first
fetch for an unseen topic runs many seconds. The panel says what is happening, and the node
carries a Cancel that abandons the workflow rather than the fetch. When
`result.Topic.IsRecognized` is false the panel says so — the same sentence the console prints —
because it changes what the review at node 6 is for.

**A source** — read through `DataSourceReaders`, then `CombineReports(tables)` for the
normalization findings, rendered as a checkbox list. Each finding shows its `Headline`,
`Proposal` and, behind a disclosure, its `Evidence` lines verbatim: a finding the user cannot
inspect is a finding they cannot trust, which is why `Evidence` exists at all. Ticked findings go
to `NormalizationPlan.Apply` and, unchanged, to `DataModelBuilder.Build(tables, chosen)` — the
second argument matters, since it is what lets `why OrderItem` at node 6 answer for a class the
user never named. `ImportedData.Notes` (the row caps) render as warnings, said out loud, not
swallowed.

A reader that throws `FileNotFoundException` or `InvalidDataException` fails the node with the
reader's own message and offers Back to node 3, rather than ending the workflow.

### 6. Review

The stage that gains the most, because in the console it is currently blind.

The model renders as a table of entity cards: name, `Provenance` badge, fields with types,
mined relationships. Every operation in `ModelEditor` is a control on the card rather than a
verb plus arguments:

| Console verb | Here |
| --- | --- |
| `add <name>` | "Add class" button and a name box |
| `remove <name>` | a delete control on the card |
| `rename <old> <new>` | edit the card's name in place |
| `field <class> <name:type>` | "Add field" row on the card, with a type drop-down |
| `unfield <class> <field>` | a delete control on the field row |
| `why [name]` | an info control on the card, opening `DecisionReport.ForName`; a "Why this model?" panel for `DecisionReport.All` |
| `approve` | "Generate" — advances to node 7 |
| `cancel` | ends the workflow |

`ModelEditor` returns `bool` for changed-or-not and enforces every rule (`Id` is generated,
a field may not share the class's name, the last field and `Name` cannot be removed, only five
types are supported). The components call it and report its answer inline on the control that
was used. **The rules are not restated in the UI** — a disabled control that duplicates
`RemoveField`'s reasoning is a second copy of the rule that will drift from the first.

The chosen target and the customization summary stay visible in a sidebar, since both were
settled three nodes ago and what Generate will produce must not be a surprise.

`model.Suggestions` — candidates discovery could not shape — render as a muted "also found"
strip, each promotable with one click. They are already offered rather than deleted;
`AddEntity` promotes a matching suggestion in place, keeping its provenance and confidence.
In the console this is invisible, and it is the single largest thing the terminal hides.

### 7. Generate

Target and persistence confirm (pre-filled from node 2; SQLite hidden when
`IsPersistenceAllowed` says no, rather than offered and then refused). `LayoutNotice` renders
here as a notice when a layout was named for a target with no window.

Then, in order:

1. **Overwrite.** When `GeneratedProjectPath.Exists`, a modal with Overwrite / Rename / Cancel —
   the three answers `OverwritePrompt.Parse` accepts. A rename runs through
   `DomainAppName.FromDescription`, the same normaliser that produced the original name, and
   re-checks: a second name can collide too.
2. **Scaffold.** `AppScaffolder.Scaffold`, reporting `ProjectPath`, `TestProjectPath` and the
   file list.
3. **Build.** `dotnet build`, with `BuildReport.Lines` rendered in the outcome's colour.
4. **Run.** For everything but a class library, a Run button. A Blazor target additionally waits
   on `WebAppStart.Wait` and then `WebAppStart.OpenBrowser` — both already exist in the CLI and
   are reused unchanged.

**Streaming the build log needs a new `ProcessRunner` overload.** `ProcessRunner.Run` calls
`ReadToEndAsync` on both streams (`ProcessRunner.cs:59`), so it returns the whole output at once
and cannot report progress. Add `RunStreaming(..., Action<string> onLine)` alongside it using
`OutputDataReceived`/`ErrorDataReceived`; **leave `Run` untouched**, since `BuildAndOfferToRun`
and its tests depend on it exactly as it is.

Node 7 ends the workflow: the session goes terminal, the host shuts down, and the console prints
what was written.

## Revisiting, and what it invalidates

Clicking a completed node reopens it. Changing an answer there invalidates everything downstream
of it — the rule the whole diagram lives or dies on:

| Changed at | Invalidates |
| --- | --- |
| 1 Describe | 5 Discover onward (the description is the discovery input, and the app name) |
| 2 App type | nothing before 7 (the target is not read until Generate) — but the sidebar and the layout field at 4 update |
| 3 Data | 5 onward (a different source is a different model) |
| 4 Look and feel | nothing (customization is read only at Generate) |
| 5 Discover | 6 onward |
| 6 Review | nothing (it *is* the model) |

Invalidation discards the downstream results and returns those nodes to Pending, with a
confirmation when a discovered or hand-edited model would be lost. Nothing is silently
recomputed — a Wikipedia fetch that reruns itself because a checkbox moved is the worst version
of this feature.

## Components

### New files, all under `CodeGenerator/Web/`

```
BuildAppWebHost.cs          Run(request, workspaceRoot, ai) -> BuildOutcome
BuildOutcome.cs             what the console prints when the workflow ends
Workflow/BuildSession.cs    the state machine: stages, answers, model, transitions
Workflow/StageGraph.cs      node/edge definition; the diagram renders from this
Workflow/StageState.cs      Pending | Active | Done | Failed
Workflow/BuildStage.cs      the seven-node enum
Components/App.razor        the root component and its <head>
Components/BuildWorkflow.razor      the page: diagram + active panel + sidebar
Components/StageDiagram.razor       the node graph
Components/Stages/*.razor           one per node (7)
wwwroot/workflow.css        the diagram and panel styling
```

`CodeGenerator.csproj` changes SDK and gains nothing else — every service the stages call is
already on its reference list.

### `BuildSession`

**A plain class with no Razor in it.** Holds the stage states, the answers, the
`CandidateDomainModel`, the `AppCustomization`, and the terminal outcome. Every transition is a
method returning a result; every long stage is a method returning a `Task`.

This is the whole testability story. The state machine — including revisit invalidation, which
is the subtlest rule here — is tested as plain C# with no browser, no bUnit and no host.
Components stay thin enough that only their rendering needs component tests.

`internal`, and tested from the existing `CodeGenerator.Tests` through the
`<InternalsVisibleTo Include="CodeGenerator.Tests" />` already in `CodeGenerator.csproj`. No new
test project.

Registered as a **singleton** for the host's lifetime, so a second browser tab attaches to the
running workflow rather than starting a rival one against the same workspace.

### `BuildAppWebHost`

Owns Kestrel and the process lifetime:

- Binds `127.0.0.1:0` and reads the assigned port back, so a port in use is not a failure mode.
- Mints a one-time token; `/build` without it returns 404. **This matters**: any process on the
  machine could otherwise drive a tool that writes files and runs `dotnet`.
- Opens the browser via the existing `WebAppStart.OpenBrowser`, and prints the URL regardless —
  a machine with no default browser still gets an address to paste.
- Shuts down when the session goes terminal, when the console is interrupted (`Interrupt.Token`),
  or when the circuit closes and does not reconnect within a grace period. A workflow abandoned
  by closing the tab must not leave a CLI blocked forever.
- Returns a `BuildOutcome` the console prints. The scaffold runs in this process, so there is
  nothing to marshal.

### Shared rules — reused, not moved

Seven rules are `internal static` inside `BuildAppCommand` today: `Merge`, `AppNameFor`,
`CombineReports`, `LayoutNotice`, `IsPersistenceAllowed`, `WhyLines` and `AddLine`. `BuildSession`
needs every one of them, and a second copy of any of them would drift from the first.

Because the workflow is hosted from the same project, **`BuildSession` simply calls them where
they are.** No move, no visibility change, no test churn — the seven test files under
`CodeGenerator.Tests/App/` that exercise them (`LayoutTargetNoticeTests`,
`CustomizationMergeTests`, `AppLayoutSettingTests`, `BuildAppPersistenceParsingTests`,
`BuildAppDataSourceTests`, `BuildAppWhyCommandTests`, `BuildAppAddCommandTests`) are untouched
and go on being the proof that both front ends agree.

This is the largest single saving of hosting in-project, and it is worth saying out loud: the
first version of this design moved ten types across three projects to achieve exactly nothing
that this achieves for free.

### The console fallback

`BuildAppWizard`, `CustomizationWizard`, `CustomizationMenus`, `SelectionPrompt` and the picker
branch of `RunReviewLoop` become unreachable on the normal interactive path.

**They are kept, not deleted**, and reached when the host cannot start — a bind failure, or
`CG_NO_WEB=1` for a headless or SSH session. Deleting six tested files and their tests is a large
blast radius, and it would leave no way to run `build app` interactively on a machine where
Kestrel cannot bind. This is a fallback for a failure case, not a second product path, and it is
documented as such.

## Error handling

| Case | Behaviour |
| --- | --- |
| Port cannot be bound | Console says so, falls back to the console wizard |
| No default browser | URL printed; the workflow waits for it to be opened |
| Tab closed mid-workflow | Grace period, then the host shuts down and the console reports it was abandoned; nothing is generated |
| Ctrl-C in the console | Host shuts down, session cancelled, nothing generated |
| Discovery throws | Node 5 fails with the message, Back to node 3 offered |
| Reader throws | Same, with the reader's own message — it already speaks the user's terms |
| Scaffold fails | Node 7 shows `ScaffoldResult.Message`; the model is still there, Generate can be retried |
| Build fails | Reported red via `BuildReport.Lines`; the Run button is hidden; next-step lines still shown, since an app that does not compile yet still has seed rows to find |
| `dotnet` not on PATH | The `Win32Exception` case: build skipped with a warning, the project on disk is still reported |

## Testing

All of it in the existing `CodeGenerator.Tests`, under a new `Web/` folder. No new test project,
and therefore nothing to add to `SmartCodeGenerator.slnx`.

- **`BuildSession`** — the bulk of it, as plain unit tests: every transition, cancel at each of
  the seven nodes, the revisit invalidation table above (each row is a test), model edits
  delegating to `ModelEditor` and reporting its `bool`, and the terminal `BuildOutcome`.
- **The seven existing rule tests** — untouched and green. If one has to change, something was
  moved that should not have been.
- **`BuildAppWebHost`** — no browser needed: it binds loopback, `/build` with the token returns
  200 and without it returns 404, and the host shuts down when the session goes terminal, when
  cancelled, and when the circuit drops.
- **Components** — bUnit, added to `CodeGenerator.Tests` (it supports MSTest): the diagram renders
  each `StageState` distinctly and wires a click on a Done node; each stage panel binds its values
  and disables Next while invalid. Rendering only — the decisions are `BuildSession`'s and are
  tested there.
- **`ProcessRunner.RunStreaming`** — lines arrive incrementally, the existing `Run` is unchanged.
  Tested in `CodeGenerator.Tests/Helper/`, where `ProcessRunnerTests` already lives.
- **Redirected stdin** — the existing piped E2E tests, green **without edits**. This is the
  scope boundary's only real proof.

Every assertion goes red before it goes green. Verification is the full `SmartCodeGenerator.slnx`
in Release with `-warnaserror`, `--no-incremental` — the CodeGenerator-only Debug build hides
analyzer errors and runs one of the five test projects.

**One known trap:** adding tests to `CodeGenerator.Tests` reddens `ProcessRunnerTests` in Debug
through CPU starvation, not through any fault of the change. If that suite goes red after a task
here, stash and re-run before debugging it.

## Sequencing

1. **The SDK switch.** `CodeGenerator.csproj` to `Microsoft.NET.Sdk.Web`. Nothing else changes;
   the REPL still runs and the suite is still green. Prove it before building on it.
2. `BuildStage`, `StageState`, `StageGraph`, `BuildSession` — headless and fully tested. No UI.
3. `BuildAppWebHost` — Kestrel, loopback, token, lifetime, shutdown. Serves a stub page.
4. `StageDiagram` and the page shell: node states, click-to-revisit, the sidebar.
5. Nodes 1–4, the wizard panels.
6. Node 5, discovery and normalization.
7. Node 6, the review screen.
8. `ProcessRunner.RunStreaming`.
9. Node 7, generate, overwrite modal, streamed build log, run.
10. Wire the interactive branch of `BuildAppCommand.Handle`; console prints the URL and the
    result; the console fallback on a bind failure or `CG_NO_WEB=1`.
11. Docs: the `CommandDictionary` help entry for `build app`, `docs/commands.md`,
    `docs/architecture.md`, `CodeGenerator/README.md`, and the user manual — which currently
    describes a console interview that will no longer be what an interactive user sees.
