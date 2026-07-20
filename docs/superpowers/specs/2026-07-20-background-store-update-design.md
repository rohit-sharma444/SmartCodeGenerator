# Background Wikipedia store update — design

**Date:** 2026-07-20
**Status:** Approved for planning

## Problem

At startup, `CodeGeneratorStarter.Run()` prints the "Code Generation Engine" banner and then
calls `ReportWikipediaStore()` **synchronously**. That method:

1. Does a network freshness check against Wikimedia (up to a 4-second timeout), and
2. If the store is stale, downloads and applies every pending daily increment — ~780 MB each,
   routinely a fortnight (~10 GB) behind — **before the prompt appears**.

The user cannot type a command until the update finishes or they press Ctrl-C. This also blocks
scripted `build app:` end-to-end runs, which drive the interactive loop through a pipe and cannot
send Ctrl-C to skip the apply.

## Goal

The prompt appears immediately. The store update runs in the **background**. Its progress is shown
as `Wikipedia store updating xx%`.

- **Interactive sessions:** the live percentage is shown in the **console window title**
  (`Console.Title`), leaving the scrollback and the live line editor completely untouched. A
  one-time line is printed after the header announcing that the update is running in the background.
- **Redirected / piped sessions** (scripted E2E, logging): no title and no cursor control are
  available, so progress is emitted as a throttled plain log line (`  Wikipedia store updating xx%`)
  on the existing ~10-second cadence, interleaved with command output. The prompt is available
  immediately — this removes the current startup block on scripted runs.

## Why the title bar (not a line above the prompt)

The interactive `CommandLineEditor` drives the console with **absolute cursor positioning**
(`Console.SetCursorPosition`, anchored at `_inputTop = Console.CursorTop`, plus `BufferHeight`
juggling and suggestion redraws). Any background thread that writes into the scrollback while the
editor is rendering will corrupt the displayed line and the cursor.

`Console.Title` is a separate surface the editor never touches, so a background writer updating it
cannot collide with input rendering. The trade-off — the live number lives in the title bar rather
than literally on a line under the header — was chosen deliberately for this reason. The after-header
announcement line still appears where originally requested; only the *live-updating* number moves to
the title.

**Rule that follows from this:** in interactive mode the background task must never write to the
scrollback buffer. Announcements, progress, and the final outcome go to the title only. (In
redirected mode there is no editor to corrupt, so log lines are fine.)

## Startup flow (`CodeGeneratorStarter`)

1. `PrintHeader()` prints the banner and help text — unchanged.
2. **Synchronous, instant:** check `File.Exists(databasePath)`.
   - If the store is **missing**, print the existing loud `NOT FOUND` warning and stop. No background
     work. (This is a local check, not a network one; keeping it in the foreground preserves the
     warn-on-missing hardening from PR #40 and the online-API fallback for fresh clones.)
3. If the store **exists**, print one line after the header:
   `Wikipedia store: checking for updates in the background (progress shown in the title bar).`
   Then launch the background task and **return immediately to the prompt**.
4. The background task performs the **entire** sequence off the main thread:
   - `DumpFreshness.Check` (the network call that previously delayed the prompt by up to 4s),
   - decide: up-to-date → title `Wikipedia store: up to date`; not-answered → quiet (no false "up to
     date"); stale → run the apply loop reporting `Wikipedia store updating xx%`.
5. On `exit`, the app cancels the background task's `CancellationToken`. Days already fully applied
   are kept: the store advances its dump-date only after a full day lands, so an interrupted update
   resumes on the next start. Killing mid-day is safe (WAL rolls back the uncommitted transaction).
   The app does not block on the task at shutdown.

`RunOnce()` (single-shot automation) is unchanged — it does not, and will not, run the store update.

## Progress plumbing

`IncrementalUpdater.Download()` and `Apply()` currently construct `ConsoleProgressBar` directly. To
drive a single overall percentage, invert that: the updater reports normalized progress through an
injected sink instead of drawing bars itself.

### Progress sink interface

```csharp
public interface IStoreUpdateProgress
{
    void BeginDay(int dayIndex, int dayCount);          // 0-based index, total pending days
    void Report(UpdatePhase phase, long done, long total); // phase = Download | Apply
    void Completed(StartupUpdateOutcome outcome, int daysApplied);
}
```

- `IncrementalUpdater` takes an `IStoreUpdateProgress` (replacing its current internal
  `ConsoleProgressBar` usage in the download/apply paths). `Download()` reports `Download` bytes;
  `Apply()` reports `Apply` bytes (compressed bytes consumed — the only total known in advance, as
  today).
- The `CountingStream` contract is preserved: the sink's `Report` **must not throw** (a progress bug
  must never truncate the bz2 stream). Sink implementations wrap their body in try/catch, mirroring
  the existing guard in `CountingStream.Read`.

### Overall-percentage aggregation

The sink computes the overall percentage; the updater only reports raw bytes. Each of `N` pending
days is an equal `1/N` slice. Within a day, download fills the first half of the slice and apply the
second half, each driven by its byte fraction:

```
phaseFraction = phase == Download ? 0.5 * (done/total)
                                  : 0.5 + 0.5 * (done/total)
overall       = (dayIndex + phaseFraction) / dayCount
percent       = clamp(round(overall * 100), 0, 100)
```

This yields a smooth 0→100% and never needs the total byte count across all days up front. Guards:
`total <= 0` contributes `0` for that phase (no divide-by-zero); percent is clamped.

### Sink implementations

- **`TitleBarProgress`** (interactive): sets
  `Console.Title = $"CodeGenerator — Wikipedia store updating {percent}%"`, throttled to ~100 ms.
  On `Completed`: `Wikipedia store: up to date` (Completed), or a short skipped/failed title. All
  `Console.Title` writes are wrapped in try/catch — setting the title can throw when no console is
  attached.
- **`LogLineProgress`** (redirected): `Console.WriteLine($"  Wikipedia store updating {percent}%")`,
  throttled to the existing ~10-second `LogInterval`. On `Completed`: one final plain line.

Selection: `Console.IsOutputRedirected` picks `LogLineProgress`, otherwise `TitleBarProgress`. The
old `ConsoleProgressBar` remains for the importer's own (foreground) usage and is not removed.

## Concurrency safety

The background task **writes** to the SQLite store while a foreground `build app:` may **read** it —
safe under WAL (one writer + many readers). `build app:`'s `ArticleRefresher` also **writes**
(`ReplaceArticle`), so two writers can meet `SQLITE_BUSY`.

**Investigation update (2026-07-20).** The original design proposed `PRAGMA busy_timeout = 5000` in
`WikipediaStore.Open()`. That was disproved during execution: **Microsoft.Data.Sqlite 10.0.9 already
retries a busy write** until the connection's `Default Timeout` (default **30 s**), independent of
the native `busy_timeout` pragma. A concurrent writer already waits and succeeds against the
unmodified store (verified empirically). A native `busy_timeout = 5000` would only *shorten* that
ceiling from ~30 s to 5 s — the wrong direction.

So the safety is real but **implicit**. The mitigation is therefore to make it **explicit**: pin
`Default Timeout = 30` on the store's connection string, with a comment, so a future edit or a
library default change cannot silently remove it. No native `busy_timeout`. This is the only change
to `WikipediaStore`, and it is behavior-preserving (the pinned value equals the prior default).

## Error handling

- **Offline / Wikimedia unreachable:** normal case. The background check fails silently (as today),
  reports "not answered", and the title/log say nothing false. The prompt is unaffected.
- **Update fails mid-run** (network drop, disk, server): the days already applied are kept; the store
  is only as stale as it was. Reflected as a failed/partial title (interactive) or final log line
  (redirected). Never crashes the app.
- **Cancelled on exit:** treated as skipped; applied days kept; no error.
- **`Console.Title` unavailable:** setter is wrapped; failure to set the title is swallowed (progress
  is cosmetic).
- **Progress callback:** must not throw into the bz2 stream (see `CountingStream` note above).

## Testing (TDD)

- **Overall-% aggregation** — pure unit tests: feed `BeginDay` / `Report` byte events across single-
  and multi-day scenarios; assert the percentage sequence is monotonic, hits the phase midpoints
  (50% at end of download), and clamps. Cover `total <= 0`.
- **Background orchestration** — with fakes following the existing `IncrementalUpdaterTests` /
  `DumpFreshnessTests` patterns: up-to-date → no apply; not-answered → nothing runs, no false
  "up to date"; stale → apply loop runs and reports; cancellation → skipped outcome, applied count
  preserved; failure → failed outcome, applied count preserved.
- **Sink string production** — assert `TitleBarProgress` / `LogLineProgress` produce the expected
  strings for given events; the actual `Console.Title` / `Console.WriteLine` side effect is behind a
  thin adapter and not asserted.
- `StartupDumpUpdate.Run` already has coverage; extend if the delegate signature changes to carry a
  day index for `BeginDay`.

## Out of scope

- Changing what a full/incremental dump contains or how articles are stripped.
- Any change to `RunOnce` automation behavior.
- Replacing `ConsoleProgressBar` for the importer.
- A live progress line rendered into the scrollback above the prompt (rejected — corrupts the editor).
