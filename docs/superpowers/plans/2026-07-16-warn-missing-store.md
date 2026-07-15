# Warn When the Local Wikipedia Store Is Missing — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a missing local Wikipedia store print a loud, non-fatal warning at startup instead of being silently swallowed.

**Architecture:** One method in the CLI startup path, `CodeGeneratorStarter.ReportDumpStaleness()`, currently returns in silence when `Dump\enwiki-full.db` is absent. Rename it to `ReportWikipediaStore()` (it now reports *missing* as well as *stale*) and replace the silent early return with a warning block that names the path, the API fallback, and the importer command. The app still starts — warn-and-continue, never refuse — because a fresh clone with no store relies on the HTTP fallback.

**Tech Stack:** C# / .NET, single console project `CodeGenerator` (an `Exe`).

**Spec:** `docs/superpowers/specs/2026-07-16-warn-missing-store-design.md` (Approach A, approved).

## Global Constraints

- **Warn-and-continue only.** The method must still `return` and let startup proceed; it must never throw or stop the app when the store is missing.
- **Do not touch `DomainDiscoveryPipeline.CreateDefault`.** The library factory stays silent; its two fallback tests are untouched.
- **No new test-project reference or `InternalsVisibleTo`.** The `CodeGenerator` CLI stays outside the unit-test boundary; verification is a manual CLI smoke.
- **Exact store path** comes from `DomainDiscoveryPipeline.DefaultDumpDatabasePath` (already read into the local `databasePath`) — do not hardcode the path string a second time.

---

## Testability note (read before Task 1)

`CodeGeneratorStarter` is in the `CodeGenerator` **CLI** project, which no test project references and which has no `InternalsVisibleTo`. There is no unit-test seam for this method, so this plan does **not** follow a red-green unit-test cycle. The test of record is a manual CLI smoke (Task 1, Steps 4–5). This is consistent with how the rest of the startup path and the `build app:` review loop are verified in this repo (see the CRUD-in-review-loop spec).

Because the real store was deleted (the reason for this work), the missing-store branch is exercised *naturally* by the current environment — running the app today hits exactly the new code path. The "store present" branch is byte-for-byte unchanged by this task, so it carries no regression risk and needs no separate run.

---

### Task 1: Warn on a missing store in the startup reporter

**Files:**
- Modify: `CodeGenerator/App/CodeGeneratorStarter.cs` — the call site (`:192`), the method's XML `<summary>` (`:195-198`), the method name (`:212`), and the silent early return (`:214-216`).

**Interfaces:**
- Consumes: `DomainDiscoveryPipeline.DefaultDumpDatabasePath` (existing `const string`); `System.IO.File.Exists`; `System.Console`.
- Produces: renamed private method `ReportWikipediaStore()` (was `ReportDumpStaleness()`), same signature `private static void`, same single caller in `PrintHeader()`. No public surface changes.

- [ ] **Step 1: Rename the method and update its `<summary>`**

In `CodeGenerator/App/CodeGeneratorStarter.cs`, change the method's summary (lines 195–198) from:

```csharp
    /// <summary>
    /// Says how far behind the encyclopedia the local Wikipedia store has fallen, and brings it
    /// forward.
    /// </summary>
```

to:

```csharp
    /// <summary>
    /// Reports the local Wikipedia store's state at startup — missing, up to date, or how far behind
    /// the encyclopedia it has fallen — and, when it is merely behind, brings it forward. A missing
    /// store is a loud warning, not an error: the app still starts on the online-API fallback.
    /// </summary>
```

Then change the method declaration (line 212) from:

```csharp
    private static void ReportDumpStaleness()
```

to:

```csharp
    private static void ReportWikipediaStore()
```

- [ ] **Step 2: Update the single call site**

In the same file, in `PrintHeader()` (line 192), change:

```csharp
        ReportDumpStaleness();
```

to:

```csharp
        ReportWikipediaStore();
```

- [ ] **Step 3: Replace the silent early return with the warning block**

In the same file, replace the missing-store guard (lines 214–216):

```csharp
        var databasePath = DomainDiscoveryPipeline.DefaultDumpDatabasePath;
        if (!File.Exists(databasePath))
            return;
```

with:

```csharp
        var databasePath = DomainDiscoveryPipeline.DefaultDumpDatabasePath;
        if (!File.Exists(databasePath))
        {
            // The store lives in a git-ignored directory, so nothing here can protect it: an external
            // clean, a disk-cleanup tool or a manual delete removes it, and the app would otherwise
            // slip to the online API without a word. Say so, loudly — but do not stop. A fresh clone
            // that never imported the store depends on exactly that fallback to run `build app:` at all.
            Console.WriteLine($"Wikipedia store: NOT FOUND at {databasePath}.");
            Console.WriteLine(
                "  'build app:' will fall back to the online Wikipedia API — slower, rate-limited, and");
            Console.WriteLine(
                "  it needs a connection. If you had already imported the store, it has been moved or");
            Console.WriteLine("  deleted. To (re)build it locally (a one-time import), run:");
            Console.WriteLine();
            Console.WriteLine("      dotnet run --project WikipediaDumpImporter -c Release");
            Console.WriteLine();
            return;
        }
```

- [ ] **Step 4: Build the CLI project**

The CLI's `bin` can be left stale by other tooling, so build the project explicitly.

Run:
```bash
dotnet build CodeGenerator/CodeGenerator.csproj -c Debug
```
Expected: `Build succeeded.` with 0 errors. (A pre-existing `NETSDK` warning about referencing an `Exe` project is acceptable — it is documented in an earlier plan.)

- [ ] **Step 5: Manual smoke — the missing-store warning prints and the app still starts**

The store is currently absent, so the startup path hits the new branch directly. Feed `exit` to the interactive loop so it prints the banner and then quits:

Run:
```bash
echo exit | dotnet run --project CodeGenerator -c Debug
```
Expected: the startup banner is followed by, verbatim in shape:
```
Wikipedia store: NOT FOUND at D:\SmartCodeGenerator-Repository\Dump\enwiki-full.db.
  'build app:' will fall back to the online Wikipedia API — slower, rate-limited, and
  it needs a connection. If you had already imported the store, it has been moved or
  deleted. To (re)build it locally (a one-time import), run:

      dotnet run --project WikipediaDumpImporter -c Release
```
and the app then reads `exit` and terminates with exit code 0 — i.e. the warning did **not** stop startup. Confirm the path in the first line is the real store path, not an empty or placeholder string.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/App/CodeGeneratorStarter.cs
git commit -m "Warn, not fall silent, when the local Wikipedia store is missing"
```

---

## Self-Review

**1. Spec coverage.**
- Rename `ReportDumpStaleness`→`ReportWikipediaStore` + update summary — Task 1, Steps 1–2. ✓
- Replace silent `if (!File.Exists) return;` with the NOT FOUND warning naming path / API fallback / importer command, then `return` — Task 1, Step 3. ✓
- Warn-and-continue, not refuse — guaranteed by the trailing `return` inside the block; asserted in Step 5. ✓
- `CreateDefault` untouched, no test-project reference — nothing in the plan touches them; called out in Global Constraints. ✓
- Manual CLI smoke as verification of record — Task 1, Step 5. ✓
- "Store present" regression — argued nil (branch unchanged) in the testability note, matching the spec. ✓

**2. Placeholder scan.** No TBD/TODO/"handle edge cases"/"write tests for the above". Every code step shows complete code; the verification step shows exact expected output. ✓

**3. Type consistency.** The method is referenced by one name after the change — `ReportWikipediaStore()` — at both its declaration (Step 1) and its sole call site (Step 2). `databasePath` is the existing local; the warning reuses it rather than re-reading the const. No new types introduced. ✓
