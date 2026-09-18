# Task 8 run notes — validation-phase-5

What running two real generated apps actually showed, per the task 8 brief. Consistent with
every prior phase's own history: the full suite was green throughout, and the one defect that
mattered was invisible to it — found only by clicking through a running app.

## Step 1 — full solution suite

`dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror`
first failed with 5 MSTEST0037 errors (`Assert.IsTrue(x.Contains(y))` / `Assert.IsFalse(...)`
in `WorkflowStageEmissionTests.cs`, `WorkflowCommandsTests.cs`, `ProposalReportTests.cs`,
`BuildAppWorkflowCommandTests.cs`, `ReviewStageTests.cs` — tasks 4, 6 and 7's own test files).
Debug builds only warn; this is CI's real command, which turns the same analyzer suggestion into
a build error. Fixed by swapping each for `Assert.Contains`/`Assert.DoesNotContain` (commit
`0a62696`).

After that fix:

- Build: `Build succeeded. 0 Warning(s). 0 Error(s).`
- `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --no-build -c Release`:

  **4313/4313 passed, 0 failed, 0 skipped** — 228 NaturalLanguageEngine.Tests + 407
  DataIngestion.Tests + 93 CodeIntelligence.Tests + 640 DomainDiscovery.Tests + 2945
  CodeGenerator.Tests. Re-run again after the task-8 production fix below (same numbers,
  confirming no regression): 4313/4313.

## Step 2 — generating an app with a genuine status lifecycle

Source: `D:\SampleProjects\data\orders.csv`, 16 rows, one `Order` entity:

```
CustomerName,Status,Total,OrderDate
Alice Johnson,Draft,120.50,2026-01-05
Bob Smith,Draft,85.00,2026-01-06
Carol White,Draft,200.00,2026-01-07
David Brown,Draft,45.25,2026-01-08
Emma Davis,Approved,150.75,2026-01-09
Frank Miller,Approved,99.99,2026-01-10
Grace Wilson,Approved,300.00,2026-01-11
Henry Moore,Approved,60.00,2026-01-12
Isla Taylor,Shipped,175.00,2026-01-13
Jack Anderson,Shipped,220.50,2026-01-14
Karen Thomas,Shipped,89.99,2026-01-15
Liam Jackson,Shipped,410.00,2026-01-16
Mia White,Cancelled,55.00,2026-01-17
Noah Harris,Cancelled,130.00,2026-01-18
Olivia Martin,Cancelled,72.50,2026-01-19
Paul Thompson,Cancelled,95.00,2026-01-20
```

`orders.csv` → table name `orders` → `NameNormalizer.ToUserTypedEntityName` singularizes to
entity `Order`, exactly matching the brief's `in Order` examples.

Drove the built CLI (`CodeGenerator/bin/Release/net10.0/CodeGenerator.exe`) from
`D:\SampleProjects` with piped stdin — once per target, since `approve` only reaches one target
per review session:

```
CodeGenerator.exe "build app from csv D:\SampleProjects\data\orders.csv"
```
stdin:
```
rules
accept 3
order status Draft,Approved,Shipped,Cancelled in Order
final status Cancelled in Order
approve wpf
```
(repeated identically with `approve blazor` for the second target). `rules` listed 5 proposals;
`#3` was `Order.Status — Status holds only Draft, Approved, Shipped, Cancelled in all 16 rows`
(the Status-kind proposal). Accepting only `#3` — not `accept all` — was deliberate:
`WorkflowCommands` refuses `order status`/`final status` when an entity has more than one
*accepted* Status-kind proposal, and `CustomerName`'s own low-cardinality column also earns one
here; leaving it unaccepted keeps `Order` at exactly one.

Both builds generated cleanly (`Generated project at D:\SampleProjects\Orders`,
`...\Orders.Web`), and both emitted validators carry the identical `Workflow` stage:

```csharp
var __order = new[] { "Draft", "Approved", "Shipped", "Cancelled" };
var __from = System.Array.IndexOf(__order, stored.Status);
var __to = System.Array.IndexOf(__order, item.Status);

if (__from >= 0 && __to >= 0 && __to < __from)
    errors.Add($"Status cannot move backward from '{stored.Status}' to '{item.Status}'.");
else if (__from >= 0 && new[] { "Cancelled" }.Contains(stored.Status) && !Equals(stored.Status, item.Status))
    errors.Add($"Status cannot change once it is '{stored.Status}'.");
```

Both apps built with `dotnet build -c Release`: `0 Warning(s)`, `0 Error(s)`.

### Console and Blazor transition editor, in the same run

The console path above is exactly Task 6's `order status`/`final status` commands, typed and
accepted twice (once per target build). Separately, in the same task-8 session, a throwaway MSTest
harness (`CodeGenerator.Tests/Manual/Task8WebHarnessTests.cs`, deleted before committing — not a
plan deliverable) built a `BuildSession` from the **same** `orders.csv` model, walked it to the
`Review` stage exactly the way `ReviewStageTests.SessionWith` does, and started the real
`BuildAppWebHost` used by `build app`'s browser workflow. Driving that page with the
`browser-automation` skill (`--eval`, since headless `console.IsInputRedirected` in this shell
never lets the CLI open its own browser workflow):

1. Checked the `Status holds only Draft, Approved, Shipped, Cancelled in all 16 rows` proposal's
   checkbox → the Task 7 transition editor (`.workflow-order`) rendered with all 4 values, in
   scan order, `↑` disabled on `Draft`, `↓` disabled on `Cancelled`, no `final` checked.
2. Clicked `↓` on `Draft` → order became `Approved, Draft, Shipped, Cancelled` (confirmed via a
   **fresh navigation** to the same URL — the model lives in the server-side `BuildSession`
   singleton, not the page, so this proves the mutation landed on the real object).
3. Clicked `↑` on the now-second `Draft` → order restored to `Draft, Approved, Shipped,
   Cancelled`.
4. Checked `final` on `Cancelled` → confirmed via another fresh navigation:
   `[{"text":"↑ ↓ Draft final","finalChecked":false}, ..., {"text":"↑ ↓ Cancelled final","finalChecked":true}]`.

Both paths — the typed console commands that actually finalized the two builds, and the Blazor
transition editor's up/down/final controls on an equivalent in-progress session over the same
model — were exercised in this one task-8 run.

## Step 3 — live click-through

Signed in as `admin` / `admin` on both. Three checks, each independently re-verified by
re-reading the record from a fresh list/grid render (not by trusting "no error appeared"):

| # | Transition | WPF result | WPF re-read | Blazor result | Blazor re-read |
|---|---|---|---|---|---|
| 1 | Draft → Shipped (Alice Johnson, legal skip) | `Saved.` | `Shipped` | navigated back to the list (success) | `Shipped` |
| 2 | Approved → Draft (Emma Davis, backward) | `Cannot save: Status cannot move backward from 'Approved' to 'Draft'.` | unchanged, `Approved` | identical message, shown on the editor page | unchanged, `Approved` |
| 3 | Cancelled → Draft (Mia White, off the final status) | `Cannot save: Status cannot move backward from 'Cancelled' to 'Draft'.` | unchanged, `Cancelled` | identical message | unchanged, `Cancelled` |

All 6 outcomes (3 checks × 2 targets) pass, word-for-word identical refusal text on both
targets — the same generated validator, exercised through two different UIs.

**On check 3's wording:** the brief asks for "refusal naming the final status," and the message
does name it (`'Cancelled'`), but through the *backward* branch, not the `Status cannot change
once it is 'Cancelled'` branch — because `Cancelled` is also the *last* value in the stated
order, `__to < __from` is already true for every in-order target, so the backward check's `if`
always wins before the `else if` naming "final" specifically is ever reached. With this order and
final choice (the brief's own suggested recipe), the literal "cannot change once it is" wording is
provably unreachable from a real running app: any value a user can actually type either fails
Field's `allowedvalues` check first (not in the set) or is in-order-earlier than `Cancelled` (hits
backward first). This is a message-priority nuance in Task 4's `Workflow` stage emission, not a
functional gap — the transition is correctly refused either way, and the final status is named —
so it was not changed here; flagged for the controller's awareness rather than fixed, per the
brief's escalate-rather-than-guess instruction for anything not small and obvious.

### WPF click-through mechanics

A short-pathed UIA harness (`D:\wpfharness`, `net10.0-windows`, `System.Windows.Automation`) drove
the real `Orders.exe`: signed in, navigated to the `Order` screen, selected each row by
`CustomerName` (its cell text, matched via UIA's `Custom`/`Text` descendants — the app's
`DataGrid` auto-generates read-only columns in property order `Id, CustomerName, Status, Total,
OrderDate, CreatedOn, ModifiedOn`), typed into the `Status` field (the header's "key strip", not
the panel below — `FieldGroups.Key` pulls `Status` and `OrderDate` out of `EntryForm`'s panels
into `EntryHeader`, per its own doc comment), clicked the screen-level `Save` button, and read the
status bar (`New` button's previous sibling `Text` element). Re-selecting the row and re-reading
its grid cell (rather than trusting the status bar alone) is the independent persistence check.
**The generated app's own runtime `Order.json`/`User.json` (under `bin/.../net10.0-windows/`,
separate from `Data/Seed/`) persist across relaunches** — both had to be deleted before each rerun
to reseed from `orders.csv`, or a rerun silently re-tested already-mutated data.

### Blazor click-through mechanics

Launched via `dotnet run --project . -c Release --no-build` (running the bare built `.dll`
directly 500'd on its own static-asset manifest — `dotnet run` was required). Driven with the
`browser-automation` skill, whose `--script` mode is confirmed broken on Windows in this
environment exactly as the project's own memory says (`ERR_UNSUPPORTED_ESM_URL_SCHEME`: a Windows
drive letter is read as a URL scheme) — worked around with a locally patched copy of `browser.mjs`
(`pathToFileURL(resolve(scriptPath)).href` in place of the raw path concatenation; the
one-line, well-understood fix, not a rewrite) rather than fighting the unpatched tool. A single
monolithic `page.evaluate` also failed ("Execution context was destroyed... navigation") because
signing in is a real full-page POST/redirect, not a client-side route change — the fix was driving
the flow with real Playwright `page`/locator calls (which survive navigations) instead of one
in-page `eval`. A refused save leaves the draft "dirty" (the typed value stayed in the box even
though nothing was written), so the generated app's own `NavigationLock` fires a
`confirm("Discard your changes?")` on the way back to the list; headless Playwright auto-dismisses
an unhandled `confirm()`, which silently blocked navigation until a `page.on('dialog', d =>
d.accept())` handler was added. Runtime `Order.json`/`User.json` under
`Orders.Web/bin/Release/net10.0/` needed the same reset-between-runs treatment as WPF.

## Defect found and fixed

**Both desktop's `RecordEditorViewModel` and Blazor's `RecordEditor.razor` built their Update-path
`ValidationContext` with `default` for `Stored`**, instead of the record actually on disk.
`RepositoryTemplate`'s own `Update` (`var stored = GetById(GetId(item));`), `MasterScreenTemplate`'s
delete guard, and Blazor's own `Repository.Update` all already thread the real value through — only
these two per-record editors did not. Harmless until Task 4: no earlier validation stage read
`ValidationContext.Stored`, so this was silent. The `Workflow` stage is the first one that does
(`if (stored is not null)`), so `default` meant that guard could never be true — a desktop or
Blazor Save let **any** status transition through, including the two this task explicitly checks.
Confirmed live: before the fix, all three WPF checks reported `Saved.` (see the WPF click-through
run in the report file, before the fix).

Fixed in the files it lives in:
- `AppScaffolding/Screens/RecordEditorTemplate.cs` — added a `_stored` field (conditional on
  `validation`, matching the existing `_validator` field's own convention) set once in the
  constructor from the pre-clone `record`, and passed it instead of `default`.
- `AppScaffolding/Screens/BlazorScreenTemplates.cs` — same fix, but `_stored` is re-set every time
  `Load()` runs, since `RecordEditor.razor` is one component instance reused as the URL moves from
  record to record.

Both new fields are conditional on `validation` being on (a new unconditional field would be
unread, and so a warning, under `-warnaserror` in a `novalidation` app). Commit `8a923cf`. Verified
by re-running the full suite (4313/4313, no regression) and by re-running the live click-through
(Step 3's table above, captured after the fix).

## Self-review

- Both fixes mirror an existing, already-correct sibling in the same codebase
  (`RepositoryTemplate.Update`, `MasterScreenTemplate`'s delete guard) rather than inventing a new
  pattern — low-risk, high-confidence, and the kind of "small, obvious" fix the brief allows
  without escalating first.
- One regression was introduced and caught before it reached a commit: the first attempt at the
  WPF fix wrote `_stored = record;` as a plain quoted C# string ending in `\n`, not `\r\n` — a
  single LF-only line inside an otherwise all-CRLF emitted file, exactly the byte-neutrality trap
  this project's own memory describes. `EmittedLineEndingTests` and `FieldHighlightTests` caught
  it on the very next full-suite run; fixed by using an explicit `\r\n` escape, matching the
  established `ClaimsUsing` precedent in the same file.
- The message-priority nuance in check 3 (above) is disclosed, not silently absorbed into "it
  passed."
- The temporary `Task8WebHarnessTests.cs` harness and the locally patched `browser-fixed.mjs` are
  both scratch/verification tooling, not plan deliverables; neither is part of this commit (`git
  status` before committing showed only the two intended file sets).

## Concerns

- The message-priority nuance above: worth a follow-up issue if the controller wants the `Workflow`
  stage's `else if` (final-value refusal) to take priority over the backward-move check when both
  are true, so the more specific message surfaces even when the final status is also the last
  value in the stated order. Not fixed here — out of this task's "small, obvious" bar.
- **`ViewModelTemplates.cs`'s `EntityViewModel<T>.SaveCommand` has the identical `default` for
  `Stored` — and this is not a minor edge case: it is the desktop screen type EVERY layout except
  `AppLayout.Business` uses for EVERY entity.** `MainViewModelTemplate.Screen` emits
  `EntityViewModel<T>` whenever `roles is null` (falling back to `MasterScreenViewModel`/
  `TransactionScreenViewModel` — which route through the now-fixed `RecordEditorViewModel` — only
  when `roles` is supplied). Both `WpfTarget.cs` and `WinFormsTarget.cs` set
  `screenRoles = customization.EffectiveLayout == AppLayout.Business ? roles ?? EntityRoleMap.Empty
  : null` — so `roles` is `null`, and `EntityViewModel<T>` is what gets emitted, for all four of
  `Tabs`, `Sidebar`, `MasterDetail` and `Dashboard`. **Concretely: today, a WPF or WinForms app
  generated with any layout other than Business ignores every Workflow rule on Save, silently,
  on every entity** — the exact defect this task found and fixed, still live on 4 of 5 desktop
  layouts. (The app this task built and clicked through used `Business`, via `MasterScreenViewModel`,
  which is why the fix and the live verification above are both real — they just do not reach this
  other, larger surface.)

  Unlike the two fixes above, `EntityViewModel<T>`'s screen edits records **in place** in an
  `ObservableCollection` with no separate clone, so there is no already-available "before" value to
  pass — supplying one correctly needs re-reading the original from the store before the in-place
  edit, a different, larger change than "use what's already there." Not fixed here — the controller
  has ruled it out of Task 8's scope, which needs a materially bigger change than this task's other,
  small and obvious fixes. Flagged for a follow-up task.
