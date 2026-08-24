# A Blazor chat UI for the whole command catalog — design

**Date:** 2026-08-24
**Status:** approved, not yet implemented
**Branch:** `blazor-chat-ui`

## Problem

Every command this tool has, except `build app`, is reached through one surface: a console prompt.

That prompt is the tool's only front door for around ninety commands. It shows nothing about what
is loaded, offers no way to see the catalog while typing into it, and renders a class's source as
whatever the terminal does with preformatted text. Choosing which solution to work on is not a
thing you can do at all — the workspace root is resolved once in the starter's constructor from
`PROJECTPATH` or the launch directory, and nothing reads a `.sln` or `.slnx` file anywhere in the
codebase. `ProjectManager.InitializeExistingProjects` (`CodeGenerationEngine/Core/ProjectManager.cs:332`)
globs every `*.csproj` under that root, recursively, and calls the result "the projects".

Meanwhile `build app` already opens a Blazor page in the browser, in-process, on a loopback port —
and everything else stayed in the terminal.

## Goal

A `chat` command opens a browser page with two things on it: a way to load a .NET solution or
project, and a conversation. You type a command, the command's whole output comes back below it
tinted by how it turned out, and the header always says what the next command will act on.

Anything in the catalog works there except the commands that stop to ask questions mid-flight.

## Scope

**In:** a `chat` command; extracting the loopback host mechanics so `/build` and `/chat` share one
token guard; a solution/project loader that re-roots the workspace; a chat session that runs
commands through the existing pipeline with their output captured; the transcript, composer,
autocomplete, context strip and the output renderers.

**Deliberately not in scope:**

- **`build app`, `batch` and `demo`.** All three prompt mid-flight. `build app` is excluded at the
  user's request and has its own workflow page already; the other two are declined for the same
  reason a buffered console cannot run them (`CodeGenerator/App/CommandColour.cs:20-30` says why).
  They are declined *in the chat*, with a line pointing at the console. They keep working there,
  byte for byte.
- **Changing what any command prints.** Not one `Console.WriteLine` in the engine moves. The chat
  captures what the console would have seen. The moment a renderer needs a command to print
  differently, the renderer is wrong, not the command.
- **Piped runs.** `chat` needs a browser. With stdin redirected, or `CG_NO_WEB=1` set, it declines
  and the console prompt is unaffected.
- **A private workspace per session.** `ProjectManager` is static. Loading a solution in the UI
  moves the CLI session it was launched from, and the transcript says so. Pretending otherwise
  would give two views that disagree in ways nobody can reason about.

## Architecture

### 1. Launch and host

`chat` starts a loopback Blazor host, prints the URL, waits, and returns when the session ends —
the shape `build app` already uses.

The mechanics for that all exist in `CodeGenerator/Web/BuildAppWebHost.cs`: bind `127.0.0.1:0` and
read the assignment back so a port in use is not a failure mode; mint a 16-byte token and 404
anything without it; `UseStaticWebAssets` so `wwwroot` and `_framework` resolve from a build output
that contains neither; `UseAntiforgery`, without which the component endpoint refuses to run;
`CircuitWatch` to count tabs; and a dispose path on every exit.

None of that is build-specific, and one line of it is security-critical:

```csharp
if (context.Request.Path.StartsWithSegments("/build")     // BuildAppWebHost.cs:130
    && context.Request.Query[TokenParameter] != token)
```

A `/chat` route added without touching that file is a route with **no token guard at all** — and
any process on this machine can reach loopback, where these commands write files and run `dotnet`.

So the mechanics move to `CodeGenerator/Web/LoopbackHost.cs`, parameterised by route segment and a
service-registration callback. `BuildAppWebHost` and the new `Chat/ChatWebHost` become thin
callers. The guard becomes `StartsWithSegments(route)` and exists once.

The 23 files in `CodeGenerator.Tests/Web` are the safety net for that extraction: **if one of them
has to change, the extraction is wrong.**

### 2. The workspace, and loading a solution

The starter keeps its workspace as four loose fields — `_workspaceRoot`, `_configuredRules`,
`_projectCount`, `_historyPath` — with the loading logic split between its constructor and
`ProjectManager`. The chat needs to re-root all of that at runtime, so it moves into one
`CodeGenerator/App/Workspace.cs`: `Root`, `Rules`, `ProjectCount`, `HistoryPath`, and a
`LoadFrom(path)` that re-roots. The REPL reads it; the chat mutates it. `HistoryPath` follows the
root, which needs no extra plumbing because `Run()` builds a fresh `CommandLineEditor` on every
iteration.

`CodeGenerationEngine/Core/SolutionFile.cs` parses both formats:

- `.slnx` — `<Solution><Project Path="..."/></Solution>`.
- `.sln` — the `Project("{guid}") = "Name", "path", "{guid}"` lines, filtered to `.csproj` so
  solution folders are skipped.

Project paths resolve relative to the solution file.

**The root of a loaded solution is the common ancestor of its projects, not the solution file's own
folder.** This repository's own `SmartCodeGenerator/SmartCodeGenerator.slnx` is the proof: every
path in it is `../Something/Something.csproj`, so a root taken from the solution's directory would
be `SmartCodeGenerator\`, which contains none of the seventeen projects. A workspace rooted there
finds nothing, and every command afterwards would be right about a wrong tree.

`Workspace.LoadFrom` then does what startup does, in startup's order: register the projects (the
solution's list when a solution was chosen, a recursive `*.csproj` glob when a folder was),
`ProjectManager.RetainOnlyWithin` (`ProjectManager.cs:128`),
`ProjectManager.ClearCurrentProjectIfOutside` (`:137`), reload the rules, and
`LearnedIntentStore.Initialize`. It returns what happened, and the chat records it as a turn naming
the file, the project count and the new root — because this moved the console too.

The picker is served by the host process, not the browser: a path box with live validation, plus a
list of subfolders and solution files to click, walking up and down from the current root. A
browser `<input type="file">` never hands the server a real path, and a native dialog would open
behind the browser.

### 3. Executing a message

The REPL's per-input dispatch (`CodeGeneratorStarter.cs:167-300`) is an `if`-ladder of
interceptions — exit, clear, history, help, status, demo, ai — and then the pipeline.
`RunOnce` (`:96`) has a shorter ladder in a *different order*. A third copy in the chat is how
three copies come to disagree, so classification is extracted to
`CodeGenerator/App/CommandRoute.cs`:

```csharp
internal enum CommandKind
{
    Exit, Clear, History, Help, Status, Demo, Ai, BuildApp, Batch, Pipeline
}

internal static CommandKind Classify(string input)
```

`Run()`, `RunOnce()` and `ChatSession` all use it. **The order is behaviour, not tidiness:**
`help build app` has to classify as `Help`, which `RunOnce` gets right today only because
`IsBuildApp` is anchored to the front of the input. The tests assert `Classify` agrees with every
existing `IsX` predicate, that case named explicitly.

`Chat/ChatSession.Send(text)` then:

| Kind | What happens |
| --- | --- |
| `Exit` | Ends the session; the host stops and the console prompt returns. |
| `Clear` | Empties the transcript. Command history is untouched, as in the console. |
| `BuildApp`, `Batch`, `Demo` | A declined turn, amber, naming the command and saying to run it from the console. Nothing is attempted. |
| `Help`, `Status`, `History`, `Ai` | Their existing renderers, captured. No pager — the page scrolls. |
| `Pipeline` | `NaturalLanguagePipeline.Handle` with the AI resolver and an inline confirm, captured. |

Verdict comes from `CommandColour.Verdict(recognized, handled)` (`CommandColour.cs:42`) so green,
red and amber mean in the browser exactly what they mean in the terminal: it worked, it was
understood and could not be done, the words were not understood.

`Console.SetOut` is process-global, so **commands are serialized and the composer is disabled in
every tab while one runs.** Nothing queues; a queued command against state the previous command
just changed is a command aimed at a tree that no longer exists.

### 4. Capturing output, and colour

`CodeGenerationEngine/Helper/OutputCapture.cs` swaps `Console.Out` for a `StringWriter` and
suppresses colour for the duration, restoring both in a `finally`.

Suppression is the part that needs a change to shipped code. `Output.Write` switches on
`ColourDelivery.Current` (`Output.cs:187`), which is a `static readonly` decided once per process
(`ColourTransport.cs:55`). Left alone, the ANSI transport writes escape sequences into the capture
buffer and the transcript shows them as text; the console-attributes transport repaints a console
nobody is reading. So `Output.Write` consults a suppression flag first — three lines, red then
green — and the transcript is plain text by construction rather than cleaned up afterwards.

The rejected alternative was stripping our own `AnsiCodes` sequences off the captured string. It
touches nothing shipped, and it fixes the symptom one layer late while leaving the attributes
transport still painting.

`CommandColour.Run` is *not* reused: it starts a `Spinner` that draws on the real console, and the
chat has its own indicator. What is copied from it is the discipline — the buffer is written to the
turn in a `finally`, so a command that throws still shows what it had printed before it did.

### 5. The AI confirmation

When AI recognition is on and the input is unrecognised, the pipeline calls
`confirmAi: Func<AiResolution, bool>` synchronously, from inside itself. In the console that is a
`SelectionPrompt`. In the chat, that prompt would appear on the console nobody is looking at, and
the session would hang.

So the chat's `confirmAi` flips the session to an awaiting-confirmation state, all tabs render the
question, and the worker thread blocks on a completion source until an answer arrives. **It must be
released with "no" when the session is abandoned**, or a closed tab leaves the command — and the
console behind it — waiting forever.

## The chat surface

### Transcript

`ChatTurn(Input, Kind, Verdict, Blocks)`, held on the session singleton so two tabs see one
conversation, the way `BuildSession` already works. Input renders as typed. Output renders below
it, tinted by verdict.

### Composer

Enter sends, Shift+Enter newlines, up-arrow walks the same `.codegen_history` the console uses —
one history, both surfaces. A command sent here is recorded there, so the console's up-arrow
finds it afterwards.

Autocomplete filters `CommandDictionary.Commands` (`CommandDictionary.cs:44`), the list `help` and
`docs/commands.md` are already generated from, so it cannot drift from the real catalog. Each row
shows the command shape, its description and its example.

One keyboard rule, stated because this is where such a UI usually goes wrong: **while the dropdown
is open, Enter inserts and the arrow keys move the selection; closed, Enter sends and up-arrow
recalls history.**

On an unrecognised turn, `CommandSuggester.Closest` renders as clickable chips that **fill the
composer rather than run.** One keystroke away, never a surprise execution.

### Context strip

Root, projects found, current project, rules loaded, AI on or off. All cheap reads, refreshed after
every turn.

No class count: getting one means enumerating `*.cs` across the workspace on every turn, and a
count that lags what the transcript just did is worse than no count.

### Renderers

`Chat/OutputBlocks.Parse(kind, text)` produces `Text`, `Code`, `Table(headers, rows)` or
`List(heading, items)`. It recognises exactly three shapes, each read out of the code rather than
assumed:

| Command | What it prints | Block |
| --- | --- | --- |
| `show class X` | blank, `----- X -----`, blank, the whole file (`ClassValidator.cs:84`) | `Code` — everything after the header |
| `list properties X`, `show properties X` | `Properties in X:` then two spaces, `<Type> <Name>` per line (`CommandHandlingService.cs:396`) | `Table` of Type and Name |
| `list classes`, `list deleted classes` | `Classes in <scope>`, a rule of dashes, one name per line (`ProjectManager.cs:256`) | `List` |

Everything else is `Text`. `show validation` is deliberately absent: its shape was not read, so it
gets no renderer.

**The rule that keeps this safe: text that does not match its expected shape exactly falls back to
`Text`.** These shapes are one refactor away from changing, and a renderer that half-recognises
output and mangles the rest is worse than a monospace block.

Highlighting is a hand-rolled C# tokenizer — keywords, strings, chars, comments, numbers — because
the page stays self-contained, no CDN and no web font, the same constraint `wwwroot/workflow.css`
already works under. It carries one test that outranks the rest: **the concatenation of the emitted
spans must equal the input exactly**, so highlighting can never silently drop code.

Long output collapses with a "show all", and a code block gets a copy button.

## Failure paths

| What happens | What the user gets |
| --- | --- |
| Port cannot be bound | `TryStart` returns false; the console says so and keeps its prompt, as `build app` already degrades. |
| stdin redirected, or `CG_NO_WEB=1` | `chat` declines. The console is already the answer. |
| A command throws | The turn shows what it printed before throwing, plus the message, marked failed. |
| Stop pressed mid-command | The turn's token is cancelled. Most work below it is synchronous and honours no token, so it says "stopping…" and **never claims it stopped.** |
| Last tab closed | The 10s abandon grace ends the session and the console returns. |
| Ctrl-C on the console | Same, from the other side. |
| Two tabs | One transcript, one enabled composer at a time. |

## Files

**Added**

```
CodeGenerator/Web/LoopbackHost.cs                 host mechanics, one token guard
CodeGenerator/Web/Chat/ChatWebHost.cs             /chat
CodeGenerator/Web/Chat/ChatSession.cs             transcript, dispatch, serialization, confirm
CodeGenerator/Web/Chat/ChatTurn.cs                one exchange
CodeGenerator/Web/Chat/CommandCatalog.cs          autocomplete filter over CommandDictionary
CodeGenerator/Web/Chat/OutputBlocks.cs            three shapes, everything else is text
CodeGenerator/Web/Chat/CSharpTokens.cs            lossless tokenizer
CodeGenerator/Web/Chat/FolderListing.cs           server-side picker listing
CodeGenerator/Web/Components/ChatPage.razor       the page
CodeGenerator/Web/Components/ChatTranscript.razor
CodeGenerator/Web/Components/ChatComposer.razor   input, dropdown, the Enter rule
CodeGenerator/Web/Components/ContextStrip.razor
CodeGenerator/Web/Components/SolutionPicker.razor
CodeGenerator/App/ChatCommand.cs                  the `chat` command
CodeGenerator/App/CommandRoute.cs                 classification, shared by all three callers
CodeGenerator/App/Workspace.cs                    root, rules, history path, LoadFrom
CodeGenerationEngine/Core/SolutionFile.cs         .sln and .slnx
CodeGenerationEngine/Helper/OutputCapture.cs      capture + colour suppression
CodeGenerator/wwwroot/chat.css
```

**Changed**

```
CodeGenerator/Web/BuildAppWebHost.cs              thin caller over LoopbackHost
CodeGenerator/Web/Components/App.razor            a second route; PageTitle per page
CodeGenerator/App/CodeGeneratorStarter.cs         Workspace; CommandRoute; the `chat` branch
CodeGenerationEngine/Helper/Output.cs             Write consults the suppression flag
CodeGenerationEngine/Core/CommandDictionary.cs    the `chat` entry
docs/commands.md                                  regenerated (UPDATE_COMMAND_DOCS=1)
```

## Test plan

`CodeGenerator.Tests/Web/Chat/`, MSTest 4 with bUnit 2.9, matching the existing 23 files. No new
test project.

**Unit**

- `CommandRoute.Classify` against every existing `IsX` predicate, with `help build app` mapping to
  `Help` named as its own case.
- `SolutionFile.Parse`: both formats; `../` paths; solution folders skipped; the common-ancestor
  root, asserted against a fixture shaped like this repository's own `.slnx`.
- `Workspace.LoadFrom` over temp directories: projects registered, outsiders dropped, a current
  project outside the new root cleared, rules reloaded, history path moved.
- `OutputCapture`: captures, suppresses colour under both transports, restores `Console.Out` when
  the command throws.
- `CommandCatalog.Matches`.
- `OutputBlocks.Parse`: one test per verified shape, **plus a fall-back-to-`Text` test per shape**
  fed output that is one line off.
- `CSharpTokens`: spans concatenate to the input exactly, over a real generated file.
- `ChatSession.Send`: one per kind, the three declines included; verdict tinting; the composer lock.
- The AI confirmation: blocks, an answer releases it, abandonment releases it with "no".

**bUnit**

- A turn renders with the verdict's class.
- The composer is disabled while a command runs.
- The Enter rule, both ways, dropdown open and closed.
- The confirmation question renders and both answers are wired.
- A second circuit repaints when the first one sends (mirrors `SessionBroadcastTests`).

**Host**

- **`/chat` without the token is 404.** The new route's own guard test.
- Every existing `BuildAppWebHostTests` green, unchanged, through the extraction.

**Run it**

Launch the CLI, open the page, load this repository's own `.slnx`, and work through real commands —
`use project`, `list classes`, `show class`, `list properties`, `add validation`,
`generate validator`, `status`, an unrecognised line, and each of the three declines.

On the previous Blazor round, four defects compiled clean with green tests and appeared only in a
running application. Nothing here is done until it has been used.

**CI**

`SmartCodeGenerator.slnx`, Release, `-warnaserror`. Analyzer checks need `--no-incremental` or a
clean build prints "Build succeeded" without rerunning them.

## Risks

- **The `LoopbackHost` extraction touches a shipped, security-relevant file.** Mitigation: the
  existing Web tests must pass untouched, and the new guard test covers the new route.
- **`Output.Write` is on the path of nearly every message the tool prints.** The change is one
  condition, tested red first, and the colour tests in `OutputColourTests` must stay green.
- **The renderers depend on output shapes no test currently pins.** Mitigation is the fall-back
  rule, plus a fall-back test per shape — a changed shape degrades to a monospace block instead of
  mangling.
- **Static engine state means the chat and the console share one workspace.** Accepted
  deliberately, stated in the transcript when it changes, and the reason the alternative was
  rejected is recorded above.
