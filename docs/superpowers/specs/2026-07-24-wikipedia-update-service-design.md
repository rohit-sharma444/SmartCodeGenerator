# Wikipedia store update as a Windows Service — design

**Date:** 2026-07-24
**Status:** Approved for planning

## Problem

The daily-increment update currently runs *inside the CodeGenerator CLI process*.
`StartWikipediaStoreUpdate()` ([`CodeGeneratorStarter.cs:256-292`](../../../CodeGenerator/App/CodeGeneratorStarter.cs))
launches `BackgroundStoreUpdate.Start(...)` as a fire-and-forget task, and `exit` cancels it
([`CodeGeneratorStarter.cs:138`](../../../CodeGenerator/App/CodeGeneratorStarter.cs)).

Consequences:

- The store only advances while somebody has the CLI open. A machine nobody opened the tool on
  falls arbitrarily far behind, at ~780 MB a day of pending increments.
- A long update is repeatedly abandoned by short CLI sessions. Days that fully landed are kept, but
  the day in progress is thrown away each time.
- Nothing catches up at boot.

## Goal

A Windows Service owns the daily update. It checks at machine start, and then daily, independent of
whether anyone runs the CLI.

## Decisions

| Decision | Choice | Reason |
| --- | --- | --- |
| Ownership | Service is **sole owner** | One writer, one downloader. No contention, no double 780 MB/day spend. |
| Cadence | At service start, then every 24h | No wall-clock arithmetic; a rarely-on machine still catches up at every boot. |
| Observability | Status file **and** Event Log | The file drives live display in the CLI; the Event Log is the durable record and the standard place to look when the service won't start. |
| Account | `LocalSystem` | The store is a local `D:\` path; the only network need is outbound HTTPS. |
| Start type | Automatic (Delayed Start) | Still "when the computer starts", without an 800 MB download fighting the boot storm. |
| Install | Elevated PowerShell script wrapping `sc.exe` | No MSI, no new dependency. |

## Architecture

### New project: `WikipediaUpdateService`

A .NET Worker Service referencing `DomainDiscovery`, hosted via
`Microsoft.Extensions.Hosting.WindowsServices` → `AddWindowsService()`.

**The core update logic is already console-free and is reused unchanged.**
`BackgroundStoreUpdate.Run(staleness, progress, apply, cancellation)`
([`BackgroundStoreUpdate.cs:15-34`](../../../DomainDiscovery/Wikipedia/Dump/BackgroundStoreUpdate.cs))
takes an injected `IStoreUpdateProgress` and knows nothing about consoles. Only `Start()` is
console-flavoured. So the service calls `Run` with its own sink.

`OverallPercentProgress`, `StartupDumpUpdate`, `DumpFreshness`, `IncrementalUpdater` and
`StoreUpdateText` are all reused as-is.

**Deleted:** `BackgroundStoreUpdate.Start()` and its console helpers `BuildProgress`, `SetTitle`,
`WriteLineSafe` — nothing calls them once the CLI stops updating. `StoreUpdateText` stays; the CLI
still uses it to render status read from the file.

### Worker loop

```text
ExecuteAsync(stoppingToken):
  while (!stoppingToken.IsCancellationRequested):
      RunOnce(stoppingToken)                       // never throws
      await Task.Delay(IntervalHours, stoppingToken)

RunOnce:
  store     = new WikipediaStore(config.StorePath)
  staleness = new DumpFreshness().Check(store)
  if !staleness.IsStale:  record current-or-unanswered; return
  new BackgroundStoreUpdate().Run(
      staleness, statusProgress,
      (day, token) => updater.UpdateDay(day, config.WorkingDirectory, token),
      stoppingToken)
```

`RunOnce` swallows everything, exactly as `Start()` does today. Being unable to update is never
fatal — the service keeps running on the next tick and the store is merely as stale as it was.

**Resumability is unchanged and is what makes a service stop safe.** `IncrementalUpdater` commits a
day at a time and moves the store's dump date only once a day has *fully* landed. A service stop, a
shutdown, or a crash mid-day costs the day in progress and nothing else; the next start resumes at
that day. `sc.exe failure` is configured with restart actions so a crash does not silently end
updating.

### Status file

New `StoreUpdateStatus` record, serialised to `Dump/update-status.json`:

```json
{
  "state": "applying",
  "percent": 43,
  "pendingDays": 6,
  "currentDay": 20260718,
  "lastSuccessUtc": "2026-07-23T04:11:02Z",
  "lastError": null,
  "serviceVersion": "1.0"
}
```

- **Written atomically**: temp file in the same directory, then `File.Move(overwrite: true)`. A
  reader must never see a half-written document.
- **Throttled to ~1s.** Progress callbacks fire per byte-chunk; the disk must not be hammered.
- **`StatusFileProgress`** is an `IStoreUpdateProgress` wrapping the existing
  `OverallPercentProgress` aggregator. It **must not throw** — the `CountingStream` contract holds
  as it does today: a progress failure must never truncate the bz2 stream. All write failures are
  swallowed.
- **The reader is tolerant.** Missing, empty, partial, or corrupt file all read as "unknown", never
  an exception. A status file is a convenience, not a source of truth — the store's own dump date is.

### Event Log

Source `SmartCodeGen Wikipedia Updater`. Milestones only, not progress: service start/stop, check
outcome, each day applied, run finished, failures with the exception message.

Creating an event source requires elevation, so **the install script registers it**, not the
service at runtime. If the source is missing, logging degrades silently — a service that cannot log
must still update.

### Configuration

`appsettings.json` beside the service executable:

| Key | Default |
| --- | --- |
| `StorePath` | `DomainDiscoveryPipeline.DefaultDumpDatabasePath` |
| `WorkingDirectory` | `<directory of StorePath>\incr` |
| `IntervalHours` | `24` |
| `StatusFilePath` | `<directory of StorePath>\update-status.json` |

`DefaultDumpDatabasePath` is currently a hardcoded const
([`DomainDiscoveryPipeline.cs:122`](../../../DomainDiscovery/DomainDiscoveryPipeline.cs)); it stays
as the fallback, and the service gains the ability to override it without a rebuild.

### Installation

`tools/install-updater-service.ps1` (elevated):

1. `dotnet publish WikipediaUpdateService -c Release`
2. Register the Event Log source.
3. `sc.exe create SmartCodeGenWikipediaUpdater binPath= "<published exe>" start= delayed-auto obj= LocalSystem`
4. `sc.exe description` and `sc.exe failure` (restart actions).
5. `sc.exe start`.

`tools/uninstall-updater-service.ps1` stops the service, deletes it, and removes the event source.

**Comment required in the script:** `LocalSystem` is correct only while the store is on local disk.
If the store ever moves to a network share, `LocalSystem` authenticates off-box as the machine
account and access will fail — the service would then need a domain account.

## CLI changes

`StartWikipediaStoreUpdate()` becomes a **read-only** report and is renamed
`ReportWikipediaStoreStatus()`, since it no longer starts anything.

- **Unchanged:** the `File.Exists` NOT FOUND warning block
  ([`CodeGeneratorStarter.cs:260-276`](../../../CodeGenerator/App/CodeGeneratorStarter.cs)). That is
  the PR #40 hardening, it is a local check, and a fresh clone still depends on the online-API
  fallback it describes.
- **Removed:** `_storeUpdateCts`, `_storeUpdateTask`, the `BackgroundStoreUpdate().Start(...)` call,
  and the cancellation on `exit` ([`CodeGeneratorStarter.cs:138`](../../../CodeGenerator/App/CodeGeneratorStarter.cs)).
- **Added:** a line reporting the store's dump date together with the service's state, from the
  status file plus `ServiceController`.
- **Added:** when the service is not installed, a **prominent** hint naming the install script.

That last point is not cosmetic. Sole ownership means a machine where nobody ran the installer stops
updating entirely, where today any CLI launch would catch up. The hint is the only thing covering
that gap, so it must be as loud as the NOT FOUND warning — not a quiet line.

A lightweight title-bar poller reflecting the status file's percent while the CLI is open is
retained. This preserves today's UX and is the reason a status file was chosen over log-only
reporting. It obeys the existing rule: in interactive mode nothing is written to the scrollback,
because `CommandLineEditor` drives the console with absolute cursor positioning and any background
write corrupts the rendered line.

## Concurrency

The second writer is now a **different process**: the service writes while a foreground `build app:`
run's `ArticleRefresher` may also write (`ReplaceArticle`).

This is already covered, and no code change is needed — but the reason is now load-bearing in a way
it was not before. Microsoft.Data.Sqlite retries a busy write until the connection's
`Default Timeout`, which is pinned to 30 s on the store's connection string precisely so a future
edit or a library default change cannot silently remove it. WAL permits one writer plus many
readers. Do **not** add a native `busy_timeout` pragma — it would *shorten* the ceiling from ~30 s
to whatever it sets, which is the wrong direction.

## Error handling

| Situation | Behaviour |
| --- | --- |
| Offline / Wikimedia unreachable | Normal case. `DumpStaleness.Unknown`, nothing runs, no false "up to date". Next tick retries. |
| Update fails mid-run | Applied days kept. Status records `lastError`; Event Log records the failure. Next tick retries. |
| Service stopped / machine shut down mid-day | Day in progress discarded, WAL rolls back the uncommitted transaction. Next start resumes at that day. |
| Service crashes | `sc.exe failure` restart actions bring it back. |
| Event source missing | Logging degrades silently; updating continues. |
| Status file unwritable | Swallowed. Updating continues; the CLI reports "unknown". |
| Store missing | Service logs and idles until the next tick. It does **not** attempt a full import — that is `WikipediaDumpImporter`'s job. |

## Testing (TDD)

Unit-level against fakes, following the existing `BackgroundStoreUpdateTests` /
`DumpFreshnessTests` patterns:

- **`StoreUpdateStatus`** — serialise/deserialise round-trip; the reader returns "unknown" for
  missing, empty, truncated, and malformed files, and never throws.
- **`StatusFileProgress`** — reports the expected percentages through the existing aggregator, and
  stays silent when the underlying write fails (the `CountingStream` no-throw contract).
- **Worker loop** — with a fake apply and an injected delay: applies then waits; cancellation stops
  promptly and reports `Skipped`; a failed run still schedules the next tick; an up-to-date check
  runs no apply; an unanswered check produces no false "up to date".
- **CLI status rendering** — fresh / stale / service-missing / errored status each produce the
  expected lines, including the prominent install hint.

Install and uninstall scripts get manual verification, not tests.

### CI note

The new project must be added to `SmartCodeGenerator/SmartCodeGenerator.slnx` and be warning-clean.
CI builds that solution in **Release** with `-warnaserror`; the local `CodeGenerator` Debug build
does not surface
those analyzer errors and runs only a subset of the test projects. A green local build is not
evidence of a green CI build.

### Verification note

`dotnet test` leaves the CLI's `bin` stale. Rebuild `CodeGenerator` before verifying any CLI-side
change by hand, or a working fix will look dead.

## Out of scope

- Changing what a full or incremental dump contains, or how articles are stripped.
- The full-import path (`WikipediaDumpImporter`) and its foreground `ConsoleProgressBar`.
- `RunOnce()` single-shot automation, which does not and will not run the store update.
- Showing service status in the VS Code panel. The status file makes this possible later; it is not
  part of this work.
- Any installer beyond the two PowerShell scripts.
