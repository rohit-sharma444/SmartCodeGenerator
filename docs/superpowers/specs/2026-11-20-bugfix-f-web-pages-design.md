# Bug fixes F: web pages (bug hunt #2)

Found 2026-11-20 by a read-only audit of the `/build` workflow in headless Chromium (orders.csv,
Console target) and by code reading, on main b5cae20. F1-F4 were reproduced in the browser
(PROVEN); F5 needs a slow Ollama discovery and is TRACED. The chat page was not driven in a
browser. The D fixes of PR #174 were not re-reported.

Environment note: with .NET SDK 11, Development Blazor builds request
`/_framework/dotnet-browser-tools/hot-reload-settings.json`, which 404s and never ends; that
console error is not a bug, and a browser test must not wait for `networkidle`.

## F1. A build still running records its app after the user changed the answers (PROVEN, high)

**Repro:** Generate with target WPF, click "Write it". While it builds, open the
Application Type node, choose Console, Continue (the diagram drops Generate and Run to not
reached). Wait for the build.

**Failure:** the Generate node then shows "OrderTracking" (the WPF folder) and Run says "ready
to start" while the target is Console; Generate shows "Wrote 57 file(s) / Compiled"; Run offers
"Run it now" for the WPF project with Console next steps. The diagram draws Generate as not
reached while it holds an app. The dead panel also calls `Navigation.Offer(Continue)`, so the
button on the node now on screen says "Continue →" from the dead panel.

**Cause:** `GenerateStage.razor` `WriteAndBuild`: after the awaited build it calls
`Session.SetGenerated(...)` and `Navigation.Offer(Continue)` without checking `_disposed` or
whether the session still wants this app. `FixErrors` in the same file does check
(`wanted`/`stale`).

**Fix:** capture a token before the write (the target key plus a model/answers version the
session bumps on every change). After the build: if the panel is disposed or the token no longer
matches, do not call `SetGenerated` or `Navigation.Offer`; only record the folder in `Written`.

**Tests (bUnit):** a blocking `BuildRunner`; click `button.write`; `Session.GoTo(AppType)` and
change the target; release the build. `Session.Generated` is null and the navigation label is
unchanged.

## F2. A refused Review edit throws away the generated app (PROVEN, high)

**Repro:** after generating, open Review classes and type `1bad` as a class name: "Could not
rename..." and the model is unchanged - but Generate goes back to "Write it and build it" and
Run to "Start it and watch it come up".

**Cause:** `ReviewStage.razor` `Report(...)` calls `Session.ModelEdited()` even when `changed`
is false (on purpose, so the model is saved), and `BuildSession.ModelEdited()` (~112-127) drops
`Generated`/`Started` unconditionally. `AddClass` and `Promote` also call it after a refusal.

**Fix:** `ModelEdited(bool changed = true)`: always save, but invalidate Generate/Run only when
`changed`. Pass `changed` from `Report`; in `AddClass`/`Promote` pass whether the model changed
(entity count rose, or the editor's own answer).

**Tests:** set Generated, render Review, `.Change("1bad")` on `input.entity-name`:
`Session.Generated` is not null. A real rename still drops it.

## F3. Review edits do not repaint a second tab (PROVEN, medium)

**Repro:** tabs A and B on Review. In A remove CustomerCity ("Removed CustomerCity."). B still
lists it; changing its type in B gives "Could not change that type." and only then drops the row.

**Cause:** `BuildSession.ModelEdited()` raises `Changed` only when `Generated` is set. Its comment
says "the other tab is repainted by the edit's own broadcast", but no such broadcast exists.

**Fix:** raise `Changed` in `ModelEdited()` on every real edit (with F2's `changed` flag).
`BuildWorkflow.Repaint` already marshals it to each tab.

**Tests:** subscribe to `Session.Changed`; remove a field through the Review UI; `Changed` fired.
A refused edit does not fire it.

## F4. A refused draft traps the user on the node (PROVEN, medium)

**Repro:** on Data put `nosuch.csv` in Path, then click the green Look node, then "←". The page
stays on Data both times and only says "No file at ...\nosuch.csv."

**Cause:** `BuildWorkflow.razor` `Revisit` (~344-363): when `ShouldCommitBeforeLeaving` holds and
`ApplyAsync()` returns false, it arms `_afterConfirming` with the GoTo and returns. That branch is
for the discard question only; a panel's own refusal takes it too. `Continue()` also arms
`_afterConfirming` on any refusal and never clears it on success, so a later discard question
raised without its own callback (`DiscoverStage.Again` on the no-data path; TRACED) would run the
stale GoTo/Advance on "Change anyway".

**Fix:** in `Revisit`, after a failed apply, wait only when `Session.PendingDiscard` is not null;
otherwise drop the draft and `GoTo(stage)`. Arm `_afterConfirming` only when `PendingDiscard` is
set, and clear it on a successful Continue.

**Tests:** render Data, `.Change` the path to a missing file, click the Describe node:
`Session.Current == Describe`. Continue with a refusal then a success: `_afterConfirming` is null.

## F5. Leaving Discover while discovery runs later moves the user and applies a stale model (TRACED, medium)

**Cause:** `DiscoverStage.razor` `Run`, owner branch after `await task`: it calls
`Session.SetModel(outcome.Model, ...)` and `Session.Advance()` whatever `Session.Current` is now,
even if the panel is disposed. The busy view has no Cancel.

**Failure:** a user who waits minutes, clicks Describe and changes the description is pulled off
that node when discovery ends, and gets a model for the old description (Model was null, so the
new description invalidated nothing).

**Fix:** record the Description/Source discovery started from; when it ends apply the model only
if they still match, and `Advance()` only if `Current == Discover`. Add a Cancel button that
cancels the discovery through a CancellationToken passed to `DiscoveryStep.Discover`.

**Tests:** an injected discover delegate awaiting a TaskCompletionSource; `GoTo(Describe)` and
`SetDescription("other")`; complete it: `Current == Describe` and `Model` is null. Cancel ends the
busy view and leaves Model null.

## Order of work

F2+F3 (one change to `ModelEdited`) → F1 → F4 → F5. After all: the Web bUnit tests, the
CodeGenerator suite, then a browser run of each repro against a real `/build` host.
