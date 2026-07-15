# Warn When the Local Wikipedia Store Is Missing

**Date:** 2026-07-16
**Status:** Approved — ready for implementation planning
**Branch:** `feat/warn-missing-store`

## Problem

The local Wikipedia store (`Dump\enwiki-full.db`, ~18 GB, 7.2M articles) can vanish —
it lives in a git-ignored directory (`.gitignore` excludes `/Dump/` and `*.db`), so an
external `git clean -x`, a disk-cleanup tool, or a manual delete removes it with nothing
in the application able to protect it. When that happens, **the app says nothing**:

- `CodeGeneratorStarter.ReportDumpStaleness()` (`:215`) opens with
  `if (!File.Exists(databasePath)) return;` — it prints a "Wikipedia store:" status line
  on every launch when the store exists, but returns in silence when it is gone.
- `DomainDiscoveryPipeline.CreateDefault()` (`:150`) then silently swaps the dump client
  for the HTTP client, and `build app:` runs against the online API — slower, rate-limited,
  connection-dependent — with no indication that the local store it was built to use is
  no longer there.

On 15 July the store was deleted and this silence let it pass unnoticed. The app should
make the absence **loud**, not fatal.

## Decision (settled)

**Warn and continue — do not refuse.** A hard stop is off the table: the pipeline docs
(`DomainDiscoveryPipeline.cs:129-133`) state the HTTP fallback exists precisely so a fresh
clone with no imported store can still run `build app:`. Failing outright would break that.
So the fix surfaces the missing store as a prominent warning and lets startup proceed.

**Scope: the startup reporter only** (`CodeGeneratorStarter`). It owns the per-launch
"Wikipedia store:" status line and is the exact place that swallowed the loss. The library
factory `CreateDefault` stays silent: it is a pure factory, console I/O does not belong at
that layer, and its two tests deliberately assert a silent fallback. The startup banner
prints on every launch, so the warning is visible before any `build app:` is attempted.

## Design

Modify `CodeGeneratorStarter.cs`:

1. **Rename** `ReportDumpStaleness()` → `ReportWikipediaStore()`. The method now reports a
   *missing* store as well as a *stale* one; the old name no longer tells the truth. Update
   the single call site (`:192`).

2. **Replace the silent early return** for a missing store with a warning block. Where the
   method currently has:

   ```csharp
   if (!File.Exists(databasePath)) return;
   ```

   print instead (then `return`, so the app still starts):

   ```
   Wikipedia store: NOT FOUND at <databasePath>.
     'build app:' will fall back to the online Wikipedia API — slower, rate-limited, and it
     needs a connection. If you had already imported the store, it has been moved or deleted.
     To (re)build it locally (a one-time import), run:

         dotnet run --project WikipediaDumpImporter -c Release
   ```

   Followed by a blank line, matching the spacing of the method's other status lines.

   - `<databasePath>` is the resolved `DomainDiscoveryPipeline.DefaultDumpDatabasePath`
     the method already reads into `databasePath`.
   - The wording serves both audiences at once: a fresh-clone user reads it as "here is how
     to get the local store," while a user who *had* imported it reads "it has been moved or
     deleted" as the alarm it should have raised.

Everything after this point in the method (opening the store, the `DumpFreshness` check, the
staleness/up-to-date/could-not-check branches, the startup update) is unchanged — it is
already guarded by the store existing.

## Testability

`CodeGeneratorStarter` lives in the `CodeGenerator` **CLI** project, which is outside the
unit-test boundary: `CodeGenerator.Tests` references the libraries but **not** the
`CodeGenerator` executable, and there is no `InternalsVisibleTo` for it. The REPL, the
startup banner, and `ReportDumpStaleness`/`ReportWikipediaStore` are unit-tested by nothing
today — the same situation documented for `BuildAppCommand.PrintModel` in the
[CRUD-in-review-loop spec](2026-07-15-crud-in-review-loop-display-design.md).

Making one startup warning line unit-testable would require crossing that deliberate
boundary (a new `CodeGenerator.Tests → CodeGenerator` reference plus `InternalsVisibleTo`)
for a static, approved message. That is disproportionate. So this change is **not**
unit-tested; it is verified end-to-end, consistent with the rest of the startup path.

## Testing

- **Manual CLI smoke (the verification of record):** with the store present, launch the app
  and confirm the existing "Wikipedia store: cut … " status line still prints (no
  regression). Then, with the store path pointing at a non-existent file (temporarily rename
  `Dump\enwiki-full.db`, or run a build from a checkout that never imported it), launch again
  and confirm the **NOT FOUND** warning prints, names the path, mentions the API fallback and
  the importer command, and that the app still reaches its prompt.

## Out of scope

- No change to `DomainDiscoveryPipeline.CreateDefault` — the library factory stays silent and
  its two fallback tests are untouched.
- No hard-stop / refuse-to-start behavior — warn-and-continue only.
- No new `CodeGenerator.Tests → CodeGenerator` project reference or `InternalsVisibleTo`.
- No change to what `build app:` generates, to the review loop, or to the importer.
- No attempt to distinguish "never imported" from "was here and got deleted" — the two are
  not reliably distinguishable, and one neutral-but-alarming message covers both.
