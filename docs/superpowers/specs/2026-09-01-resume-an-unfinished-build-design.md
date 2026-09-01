# Resuming an unfinished build — design

**Date:** 2026-09-01
**Status:** approved, not yet implemented
**Branch:** `resume-a-build`

## Problem

`BuildSession` lives in memory and nowhere else. Close the last browser tab and `CircuitWatch`
reports the workflow abandoned, `SessionPhase.Abandoned` is set, and everything the session held
is gone — the description, the application type, the look, the discovered model, and every edit
made to that model on the Review stage.

The model edits are the loss that matters. Discovery can be run again; a Wikipedia fetch costs a
minute. What cannot be recovered is the half hour someone spent renaming eight classes, deleting
the fields discovery invented, promoting three suggestions, and moving two classes across the
master/transaction split. None of that is written anywhere, and nothing warns that closing the tab
will discard it.

The console's sign-off line looks like a mitigation and is not. `BuildCommandEcho` replays what was
*typed* — the description, the data source, the customization options — because that is all it can
reconstruct from a command line. A renamed class, an added field, a promoted suggestion: none of
those has a command-line form, so none of them appears in the replay line. The one thing a user
would most want back is precisely the thing the line cannot carry.

A browser tab is closed by accident far more easily than a terminal is. It is also closed
deliberately, by someone who assumes a tool that printed an address will still have their work
when they reopen it.

## Goal

Close the tab, come back, and pick up where you left off — with the model exactly as you had
edited it.

## Scope

**In:**

- A snapshot of the answers and the reviewed model, written to `.codegen/build-in-progress.json`
  after every answer and every model edit.
- A resume offer at the start of an interactive `build app`: resume, start fresh, or discard.
- Restoring a session from a snapshot, and re-reading the data source rather than storing its rows.
- Clearing the snapshot when a build completes or is cancelled — and deliberately not when it is
  abandoned.
- A `.gitignore` line for the snapshot. See *Risks* — `.codegen/` is a tracked folder here.

**Out:**

- Named, keepable build specs (`build app save shop.json`). A different feature with a different
  purpose: this one is about not losing work, not about repeatable builds. The snapshot format is
  deliberately private so it can change shape freely.
- More than one slot. One workspace, one unfinished build.
- Resuming from a snapshot written by a different version of the tool, beyond loading what it
  still recognises.
- Storing the rows that were read, or the connection string. See *Decisions*.

## Decisions taken before this was written

Both settled with Rohit, each over a real alternative:

- **Crash recovery, not repeatable builds.** A named-spec feature was the alternative and is more
  powerful — committable, diffable, a regression test for the generator. It does nothing for the
  person who just closed a tab, which is the actual complaint.
- **Save the answers, re-read the data.** Storing the rows would make resume exact and need
  nothing from the outside. It was rejected on two grounds: the file is rewritten after every
  answer, and the run recorded in
  `docs/superpowers/specs/2026-09-01-build-app-sql-data-source-design.md` read 25,069 rows from one
  table — so for a database this would copy production data into `.codegen` inside the repo, after
  every click. Saving a reference keeps the file at a few KB whatever the data size.
- **The connection string is not saved.** The manual, as of the SQL Server branch, promises "the
  connection string is never written to `.codegen/`". That promise is now load-bearing: a SQL
  resume records the database *name* and asks for the connection again.

## What is on disk

`.codegen/build-in-progress.json`, beside `app-profile.json` and `learned.json`.

```
schemaVersion    int      refuse anything higher; see Compatibility
savedAt          UTC      for "left off 20 minutes ago"
stage            string   the BuildStage name

description      string
appType          string   the target key ("wpf"), not a type name

customization    every AppCustomization member, nullable throughout

source           null, or { kind, path } — or for SQL, { kind, database }

model
  entities       [ { name, provenance, confidence, sourceTitle, role,
                     fields:        [ { name, type, provenance } ],
                     relationships: [ { target, role } ] } ]
  suggestions    same shape
  rejected       [ string ]
  decisions      [ { name, stage, outcome, reason, evidence } ]
```

**No rows. No connection string.** Both are asserted by tests rather than left as intentions —
see *Testing*.

`decisions` is saved because it is what the review loop's `why` verb reads. Without it a resumed
build answers `why` with silence, which is a silent degradation of exactly the kind this codebase
keeps guarding against.

### It needs its own DTO

`CandidateDomainModel` cannot be serialised directly. Every collection on it is get-only
(`public List<CandidateEntity> Entities { get; } = new();`) so `System.Text.Json` has nothing to
deserialise into, and the leaf types use `required init` members. The snapshot therefore carries
its own nullable-throughout DTO and maps both ways, which is the same shape
`CustomizationDefaults.Profile` already takes and for the same reason.

Nullable throughout, so a field a newer build added is filled from the default rather than with a
null or a zero.

## When it is written

**After every applied answer, and after every model edit.**

`BuildSession.Apply` is the funnel for the first: every `SetDescription`, `SetTarget`, `SetSource`,
`SetCustomization`, `SetModel` reaches it.

The second is the wrinkle, and it is the whole feature. **Review's edits do not go through
`Change`.** `ReviewStage.razor` calls `ModelEditor.RemoveEntity(Model, …)`,
`ModelEditor.AddField(Model, …)`, `BuildAppCommand.AddLine(Model, …)` and so on, all of which
mutate the model *in place* on an object the session already holds. Hooking `Apply` alone would
autosave every answer and miss every rename, deletion and promotion — which is to say it would
miss exactly the work this exists to protect.

The panel funnels all of them through one place: its `Report(bool changed, string ok, string fail)`
method, plus the two `_message = BuildAppCommand.AddLine(…)` calls beside it. That is the hook —
a `Session.ModelEdited()` that saves. Adding it to `ModelEditor` instead was rejected: that type
is pure and knows nothing about sessions or disk, and it is used by the console review loop too.

**Never on a redirected-stdin run.** `CustomizationDefaults` already refuses to read a profile on
a piped run, so that a scripted build's output cannot be changed by a file it never mentioned. The
same rule applies in the other direction here: a scripted build must not leave state that changes
the next run.

Writing is best-effort. A failure to write is not a failure to build — an unwritable `.codegen`
means no resume, not a dead command.

## The offer

At the start of `BuildAppCommand.Handle`, before the page opens:

```
An unfinished build is waiting in this workspace:
  "a shop"  —  WPF  —  8 classes reviewed
  left off at Generate, 20 minutes ago

[r]esume   start [f]resh   [d]iscard
```

The console is the right place, and it is safe to prompt there. `build app` is dispatched
**outside** `CommandColour.Run` (`CodeGeneratorStarter.cs:281-297`), deliberately, because it
prints an address and then blocks — which is why `OverwritePrompt` is visible today. A prompt
inside a buffered command would not be.

Parsing follows `OverwritePrompt` exactly, including its reasoning: blank and end-of-input take
the safe answer, and anything unrecognised re-asks rather than falling through to a default.

Here the safe answer is **start fresh**, which is worth spelling out because *resume* looks like
the kind one at first. Nothing is deleted either way — the snapshot survives an Enter, so a user
who meant to resume has lost nothing and can answer properly next time. Defaulting to resume has
a failure mode that fresh does not: someone who presses Enter without reading continues a build
they may not know exists, and can then generate an application from a stale model, or from a
colleague's abandoned session. Fresh is also exactly what today's tool does, so an Enter changes
nothing about behaviour anyone already relies on.

`discard` is the only answer that deletes anything, and it says what it is deleting first.

Only asked interactively, and only when a snapshot loads.

## Resuming

The model is restored as it was — edits, screen split, decision trail. The source is re-read.

- A file still where it was: read again, and the build carries on with the rows it had.
- A file that has moved or gone: said plainly, and the workflow opens on the **Data** stage with
  the remembered path pre-filled, rather than the build failing.
- A SQL source: the database name is shown and the connection asked for again, on the same stage.
  It cannot be otherwise without breaking the promise above.

The stage resumed to is the one that was left, except that any stage whose answer can no longer be
honoured pulls it back — a re-read that fails cannot resume at Generate.

## Clearing

- `SessionPhase.Generated` — cleared. The build finished; there is nothing to resume.
- `SessionPhase.Cancelled` — cleared. Cancelling is a decision to abandon the work, and it already
  asks for confirmation when there is a model to lose.
- `SessionPhase.Abandoned` — **kept.** This is the closed tab, and the entire point.

## Compatibility

`schemaVersion` is written and checked. A file with a *higher* version than this build understands
is treated as absent rather than partially read: a half-loaded model is worse than no offer, and
a user with two versions of the tool should not have the older one silently drop half their work.
An unreadable or malformed file is likewise treated as absent, never thrown — the behaviour
`CustomizationDefaults` and `AiConfig.Load` already have, because a broken config must leave the
CLI working exactly as it does without one.

## Testing

- **Round trip**, with a model that has been edited: a renamed class, a removed field, a promoted
  suggestion, a relationship, and a screen-split override on the customization. Everything comes
  back.
- **The two promises, as tests over the written JSON rather than as intentions:** a snapshot of a
  SQL build contains no connection string and no password anywhere in the file; a snapshot of a
  25,000-row CSV build contains no rows and stays small.
- **The hook that matters:** a model edit through `ReviewStage` writes a snapshot. Asserted at the
  panel, because asserting it on `BuildSession` alone would pass while the panel bypassed it —
  which is the defect this test exists to prevent.
- **Clearing:** cleared on Generated, cleared on Cancelled, **kept** on Abandoned.
- **The offer's parse:** r/f/d, their long forms, blank, end-of-input, and something unrecognised
  re-asking.
- **A corrupt file, and a file from a higher schemaVersion,** each producing no offer and no throw.
- **A piped run writes nothing**, and a piped run with a snapshot present ignores it.
- Then the page gets looked at in a browser. Five consecutive branches have shipped a defect that
  compiled, passed its tests, and was visible only in a running page; the last one shipped with the
  browser pass skipped and is still unverified.

## Risks

- **The `ModelEditor` bypass is the whole risk.** If a later panel edits the model without calling
  `Session.ModelEdited()`, that edit is silently outside the safety net. A test at the panel
  catches today's paths; nothing prevents tomorrow's. Worth a comment on `ModelEditor` pointing at
  this, and worth considering whether the session should own the model behind a method that cannot
  be bypassed — deliberately out of scope here, as it would touch the console review loop too.
- **A resumed build is not byte-identical to the one that was interrupted.** The rows are re-read,
  and a file that changed in between yields different data. Saying so is the mitigation; pretending
  otherwise is not available.
- **`.codegen/` is inside the user's repo and is NOT ignored.** `.codegen/learned.json` is tracked
  in this repository deliberately, and `.gitignore` ignores `.codegen_history` and `.dd-cache/`
  but not `.codegen/` itself. So the snapshot cannot rely on the folder being ignored, and it must
  not become the first transient file anyone commits by accident: this branch adds
  `.codegen/build-in-progress.json` to `.gitignore` rather than ignoring the folder, which would
  untrack `learned.json`. The file holds class and field names from the user's domain, which is
  unremarkable in itself — the reason to ignore it is that it is in-progress state, not that it is
  sensitive.
