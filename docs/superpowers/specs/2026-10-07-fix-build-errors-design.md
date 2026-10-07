# Fix build errors of a generated app — design

Branch: `fix-build-errors-prompt`. Date: 2026-10-07. Status: draft for review.

## Goal

When the build of a generated application fails, the tool asks the user at that stage:
"Fix the N build errors?" (default **Yes**). On yes, the tool finds the errors, fixes them,
rebuilds, and repeats until the build is green or it cannot make progress. It reports what it fixed
and what remains. It works on both surfaces: the console wizard and the browser Generate stage.

## Today (verified in code)

- Browser flow: `GenerateStage.razor` → `GenerateStep.Build` runs `dotnet build` through
  `ProcessRunner.RunStreaming` (3 min). `BuildReport.Lines` shows at most 10 error lines and
  "Rerun with: dotnet build <path>". The session ends as `Generated` even when the build is red;
  `GenerateStep.CanRun` then blocks the Run stage. There is no Failed state for a compile error.
- Console flow: `BuildAppCommand.BuildAndOfferToRun` builds under a spinner, prints the report, and
  asks "Run it now?" only when the build is green. It does not build when stdin is redirected
  (piped runs, scripts, app-check).
- Nothing parses compiler codes. `BuildReport.ErrorsIn` matches the text `: error `.
- Roslyn 5.3.0 is referenced (`Compiler.CompileSources`, `ProjectCompiler`), without Workspaces or
  CodeFix packages. `Class*Service` files already make Roslyn syntax edits.
- Ollama: `IOllamaClient` / `OllamaClient` (DomainDiscovery) is specific to domain models. Default
  model `qwen2.5:3b`, about 12 s per step, falls back when Ollama is down.

## Design

### 1. `BuildDiagnostics` (CodeGenerationEngine)
Pure parser: `dotnet build` output → `BuildDiagnostic { File, Line, Column, Code, Message, Severity }`.
Handles MSBuild's repeated lines (same error per target framework / project), relative paths
(resolved against the project folder), and `warning` lines (ignored). `BuildReport` keeps its public
behaviour and uses the parser for its counts.

### 2. Fix rules (CodeGenerationEngine)
`IBuildFixRule { bool CanFix(BuildDiagnostic d); FixResult Fix(FixContext ctx, BuildDiagnostic d); }`.
A rule edits only files inside the generated project folder and returns the files it changed.
First set, each one verified against the generator source before it is written (the plan checks
the real cause of each code):

| Code | Cause | Rule |
|---|---|---|
| CS0260, CS0101 | validator partial clashes with the user's class | make the declaring half `partial` / drop the duplicate |
| CS0759, CS8795 | stale write-once `Validator.Custom.cs` stub | regenerate the stub (existing `OutdatedValidatorStubs` logic) |
| CS0542 | member has the same name as its class | rename the member |
| (none) | member named `Equals`, `ToString`, `GetHashCode`, `GetType` | rename the member |
| CS0234 | namespace missing (nologin Auth folder, `System`/`Microsoft` entity-name clash) | add or correct the using / namespace |

A rule that cannot prove its fix is safe returns "not fixed"; the error then goes to the AI step.

### 3. AI step (DomainDiscovery `Llm/`)
For an error no rule fixed. A new method on the Ollama layer, `AskForFix`, sends the error, the file
name and about 30 lines around the error line, and expects the replacement for those lines only.
Settings come from `discovery.json` (endpoint, model, timeouts). The tool accepts an edit only if
(a) the file still parses and (b) the total error count does not rise after the next build. If not,
the edit is reverted. If Ollama is not running, the step is skipped and the report says so.

### 4. `BuildFixLoop` (CodeGenerationEngine, no UI)
`Run(projectPath, build, ...)`: build → parse → rules → AI step → rebuild. Stops when the build is
green, when a round does not lower the error count, or after 3 rounds. Before the first edit it
copies each file it will change to a backup folder inside `.codegen/` and keeps the list of edits
(file, rule or AI, error code). Result: `FixOutcome { Green, Rounds, Fixed[], Remaining[], Reverted[] }`.
`build` is a delegate, so tests use a fake builder and a fake AI.

### 5. Surfaces
- **Console** (`BuildAndOfferToRun`): when the build is red and the session is interactive, ask with
  `SelectionPrompt.Choose` ("Yes, fix them" default / "No, leave them"). Runs outside
  `CommandColour.Run`, like the rest of `build app`. Progress lines use the spinner, then the report.
  If the build is then green, the existing "Run it now?" question follows.
- **Browser page** (`GenerateStage.razor`): new phase "Build failed" with the error list, two buttons
  (Fix them / Leave them), a Working phase with progress lines, and a result card. The page uses the
  same `BuildFixLoop`. After a green rebuild the stage updates `GeneratedApp` (`BuildSucceeded`) so
  the Run stage opens.
- Piped or scripted runs: no prompt, no fix (they do not build today either).

### 6. Safety
- Edits stay inside the generated project folder (path check on every write).
- Backups are kept; "No change is made without a backup" is tested.
- The report never claims all errors are fixed. It says "N fixed, M remain" and lists the remaining
  errors with the file and line.
- The AI step never runs without Ollama; a timeout counts as "not fixed", not as a failure of the loop.

## Out of scope
Warnings. Runtime errors. Fixing the user's hand-written code outside the generated project.
Other build paths (`create project`, `validate project`, script `run`). A fix for the cause in the
generator itself (rules repair the symptom; each rule's report mentions the generator defect).

## Testing
- Parser: golden `dotnet build` outputs (repeated lines, relative paths, warnings).
- Each rule: a broken sample compiled with Roslyn (`CompileSources`) RED before, GREEN after; and a
  case where the rule must refuse. Demand red-then-green.
- Loop: fake builder + fake AI: converges; stops on no progress; stops at 3 rounds; reverts a bad AI
  edit; backups exist; refuses a path outside the project.
- AI client: stubbed `HttpClient` (as in `OllamaClientTests`).
- Console: prompt shown on red, not on green, not when redirected; default is Yes.
- bUnit: Generate stage phases and buttons; `CanRun` after a fixed build.
- Live: extend `tools/app-check` with a case that breaks a generated app on purpose, runs the fix,
  and checks the rebuild. Run the real console and browser flows once and look at the screenshots.
- Per task: build + focused filter. Full suite only at the end (`-m:1`). CI command: Release,
  `-warnaserror`, `SmartCodeGenerator.slnx`.

## Open points for the plan
- The exact real cause of each rule's code (read the generator source first).
- Where `.codegen/` backups live for a project built in a staging folder (`C:\cg-t`).
