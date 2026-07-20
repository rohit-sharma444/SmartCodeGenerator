# Background Wikipedia Store Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the startup Wikipedia-store freshness check and daily-increment apply off the main thread so the prompt appears immediately, showing live progress as `Wikipedia store updating xx%` in the console title (interactive) or a throttled log line (redirected/piped).

**Architecture:** Invert progress reporting in `IncrementalUpdater` — it reports normalized bytes through an injected `IStoreUpdateProgress` sink instead of drawing `ConsoleProgressBar` itself. A pure `OverallPercentProgress` sink turns those byte reports into one overall percentage; renderers push that to `Console.Title` or a log line. A `BackgroundStoreUpdate` orchestrator runs the existing `StartupDumpUpdate` day-loop on a `Task`, cancellable on exit. `CodeGeneratorStarter` keeps the instant missing-store warning in the foreground and launches the background task.

**Tech Stack:** C# / .NET (match existing csproj targets), Microsoft.Data.Sqlite, xUnit (`DomainDiscovery.Tests`), SharpZipLib (test fixtures only).

## Global Constraints

- Style, copied from the existing files being edited: file-scoped namespaces; nullable reference types enabled; collection expressions (`[]`); `using var`; primary constructors where the existing code uses them.
- The progress callback path (`IStoreUpdateProgress.Report`, and anything reached from `CountingStream`) **must never throw** — `BZip2InputStream` treats any exception from the stream it reads as end-of-input, which silently truncates the apply. Every sink's `Report`/`Emit` body is pure math or wrapped in try/catch.
- Nothing in the store-update path may stop the application from starting, or crash it: offline is the normal case, an interrupted update is not an error, a failed one leaves the store as stale as it was.
- The store advances its dump-date only after a full day lands (existing `IncrementalUpdater.UpdateDay` guarantee) — do not change this; it is what makes background cancellation safe.
- `RunOnce` (single-shot automation) must remain untouched — it does not run the store update.
- Interactive mode: the background task must never write to the scrollback buffer (it would corrupt `CommandLineEditor`'s absolute-cursor rendering). Title only.
- Verification note (from project memory): `dotnet test` leaves the CLI's `bin` stale — rebuild `CodeGenerator` before any CLI smoke test.

## File Structure

**Create (all in `DomainDiscovery/Wikipedia/Dump/`):**
- `UpdatePhase.cs` — the `Download`/`Apply` enum.
- `IStoreUpdateProgress.cs` — the progress sink interface.
- `ConsoleBarProgress.cs` — default sink reproducing today's `ConsoleProgressBar` bars (importer + tests).
- `OverallPercentProgress.cs` — pure sink: byte reports → overall percentage → renderer callbacks.
- `StoreUpdateText.cs` — pure user-facing strings.
- `BackgroundStoreUpdate.cs` — orchestrator: testable `Run` + thin fire-and-forget `Start`.

**Modify:**
- `DomainDiscovery/Wikipedia/Dump/IncrementalUpdater.cs` — inject sink; replace direct `ConsoleProgressBar` use in `Download`/`Apply`.
- `DomainDiscovery/Wikipedia/Dump/WikipediaStore.cs:36-41` — add `PRAGMA busy_timeout` in `Open()`.
- `CodeGenerator/App/CodeGeneratorStarter.cs` — foreground missing-store warning + launch background task; cancel on exit; remove synchronous `ReportWikipediaStore`/`ApplyPendingDays`.

**Test (all in `DomainDiscovery.Tests/Wikipedia/Dump/`):**
- `IncrementalUpdaterTests.cs` (modify) — add sink-callback test + `RecordingProgress` fake.
- `OverallPercentProgressTests.cs` (create).
- `StoreUpdateTextTests.cs` (create).
- `BackgroundStoreUpdateTests.cs` (create).
- `WikipediaStoreConcurrencyTests.cs` (create).

---

### Task 1: Progress abstraction + `IncrementalUpdater` refactor

Introduce the sink interface and the default console-bar adapter, and route `IncrementalUpdater`'s download/apply progress through it. Existing behavior (importer bars, existing tests) is preserved because the sink defaults to `ConsoleBarProgress`.

**Files:**
- Create: `DomainDiscovery/Wikipedia/Dump/UpdatePhase.cs`
- Create: `DomainDiscovery/Wikipedia/Dump/IStoreUpdateProgress.cs`
- Create: `DomainDiscovery/Wikipedia/Dump/ConsoleBarProgress.cs`
- Modify: `DomainDiscovery/Wikipedia/Dump/IncrementalUpdater.cs`
- Test: `DomainDiscovery.Tests/Wikipedia/Dump/IncrementalUpdaterTests.cs`

**Interfaces:**
- Produces: `enum UpdatePhase { Download, Apply }`; `interface IStoreUpdateProgress` with `void BeginDay(int dayIndex, int dayCount)`, `void BeginPhase(UpdatePhase phase, int day, long total, long initial)`, `void Report(long done)`, `void EndPhase()`, `void Finish(StartupUpdateOutcome outcome, int daysApplied)`; `sealed class ConsoleBarProgress : IStoreUpdateProgress`.
- Produces: `IncrementalUpdater(WikipediaStore store, HttpClient? http = null, Action<string>? say = null, IStoreUpdateProgress? progress = null)`.
- Consumes: `StartupUpdateOutcome` (existing, in `StartupDumpUpdate.cs`).

- [ ] **Step 1: Create the enum**

`DomainDiscovery/Wikipedia/Dump/UpdatePhase.cs`:

```csharp
namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// The two phases of bringing one day forward. Each is half of that day's slice of overall progress.
/// </summary>
public enum UpdatePhase { Download, Apply }
```

- [ ] **Step 2: Create the interface**

`DomainDiscovery/Wikipedia/Dump/IStoreUpdateProgress.cs`:

```csharp
namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// Receives progress while the store is brought forward. <see cref="IncrementalUpdater"/> drives the
/// phase callbacks (<see cref="BeginPhase"/>/<see cref="Report"/>/<see cref="EndPhase"/>); the
/// orchestrator drives <see cref="BeginDay"/> and <see cref="Finish"/>.
/// </summary>
/// <remarks>
/// <b><see cref="Report"/> must never throw.</b> It is reached from <c>CountingStream</c>, and
/// <c>BZip2InputStream</c> treats any exception from the stream it reads as end-of-input — a throwing
/// report would silently truncate the apply.
/// </remarks>
public interface IStoreUpdateProgress
{
    /// <summary>A new day's slice begins. <paramref name="dayIndex"/> is 0-based within the pending set.</summary>
    void BeginDay(int dayIndex, int dayCount);

    /// <summary>
    /// A phase of the current day begins, over <paramref name="total"/> bytes with
    /// <paramref name="initial"/> already done (a resumed download). <paramref name="day"/> is for
    /// labelling only.
    /// </summary>
    void BeginPhase(UpdatePhase phase, int day, long total, long initial);

    /// <summary>Absolute bytes done in the current phase.</summary>
    void Report(long done);

    /// <summary>The current phase finished cleanly (not on cancellation).</summary>
    void EndPhase();

    /// <summary>The whole update ended, however it ended.</summary>
    void Finish(StartupUpdateOutcome outcome, int daysApplied);
}
```

- [ ] **Step 3: Create the default console-bar adapter**

`DomainDiscovery/Wikipedia/Dump/ConsoleBarProgress.cs`:

```csharp
namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// The default sink: the per-phase <see cref="ConsoleProgressBar"/> the importer and the
/// pre-background startup path have always drawn. This keeps that behaviour when no other sink is
/// injected, so the importer and the existing tests are unchanged.
/// </summary>
public sealed class ConsoleBarProgress : IStoreUpdateProgress
{
    private ConsoleProgressBar? _bar;

    public void BeginDay(int dayIndex, int dayCount) { }

    public void BeginPhase(UpdatePhase phase, int day, long total, long initial)
    {
        _bar?.Dispose();
        var label = phase == UpdatePhase.Download ? $"  download {day}" : "  apply   ";
        _bar = new ConsoleProgressBar(label, total, initial);
    }

    public void Report(long done) => _bar?.Report(done);

    public void EndPhase()
    {
        _bar?.Dispose();
        _bar = null;
    }

    public void Finish(StartupUpdateOutcome outcome, int daysApplied)
    {
        _bar?.Dispose();
        _bar = null;
    }
}
```

- [ ] **Step 4: Write the failing sink-callback test**

Add to `DomainDiscovery.Tests/Wikipedia/Dump/IncrementalUpdaterTests.cs` — a recording fake nested class and a test. Place the fake alongside the existing `StubDumpServer` nested class:

```csharp
private sealed class RecordingProgress : IStoreUpdateProgress
{
    public List<UpdatePhase> PhasesBegun { get; } = [];
    public int Reports { get; private set; }
    public bool Ended { get; private set; }

    public void BeginDay(int dayIndex, int dayCount) { }
    public void BeginPhase(UpdatePhase phase, int day, long total, long initial)
        => PhasesBegun.Add(phase);
    public void Report(long done) => Reports++;
    public void EndPhase() => Ended = true;
    public void Finish(StartupUpdateOutcome outcome, int daysApplied) { }
}

[Fact]
public void Apply_ReportsProgress_ThroughTheInjectedSink()
{
    var store = NewStore();
    var file = Increment("""
          <page><title>Receipt</title><ns>0</ns><id>101</id>
            <revision><id>2</id><text xml:space="preserve">A receipt records a [[Sale]].</text></revision>
          </page>
        """);

    var progress = new RecordingProgress();
    new IncrementalUpdater(store, say: _ => { }, progress: progress).Apply(file);

    Assert.Contains(UpdatePhase.Apply, progress.PhasesBegun);
    Assert.True(progress.Reports > 0);
    Assert.True(progress.Ended);
}
```

- [ ] **Step 5: Run the test to verify it fails**

Run: `cd D:/SmartCodeGenerator-Repository && dotnet test DomainDiscovery.Tests --filter "FullyQualifiedName~Apply_ReportsProgress_ThroughTheInjectedSink"`
Expected: FAIL to **compile** — `IncrementalUpdater` has no `progress` parameter and `IStoreUpdateProgress` is not yet referenced by the updater.

- [ ] **Step 6: Refactor `IncrementalUpdater` — constructor**

In `DomainDiscovery/Wikipedia/Dump/IncrementalUpdater.cs`, add the field and constructor parameter. Replace lines 31-43 (the fields + constructor) with:

```csharp
    private readonly WikipediaStore _store;
    private readonly HttpClient _http;
    private readonly Action<string> _say;
    private readonly IStoreUpdateProgress _progress;

    public IncrementalUpdater(
        WikipediaStore store, HttpClient? http = null, Action<string>? say = null,
        IStoreUpdateProgress? progress = null)
    {
        _store = store;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        _say = say ?? Console.WriteLine;
        _progress = progress ?? new ConsoleBarProgress();

        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
            _http.DefaultRequestHeaders.Add("User-Agent", "SmartCodeGenerator-DomainDiscovery/1.0");
    }
```

- [ ] **Step 7: Refactor `Download` to report through the sink**

In `Download`, replace the progress-bar block (currently lines 90-108, from `using var progress = ...` through the `while` loop) with:

```csharp
            _progress.BeginPhase(UpdatePhase.Download, day, total, already);

            using var source = response.Content.ReadAsStream();
            using var file = new FileStream(
                destination, resuming ? FileMode.Append : FileMode.Create, FileAccess.Write);

            var buffer = new byte[81920];
            var written = already;

            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                // Interrupted. What is on disk stays on disk — the next run resumes from exactly
                // here, and 780 MB is far too much to throw away because the user changed their mind.
                cancellation.ThrowIfCancellationRequested();

                file.Write(buffer, 0, read);
                written += read;
                _progress.Report(written);
            }

            // Only on a clean finish. A cancelled phase must not snap the bar/percentage to 100%.
            _progress.EndPhase();
```

- [ ] **Step 8: Refactor `Apply` to report through the sink**

In `Apply`, replace the two lines that create the bar and counting stream (currently lines 145-147):

```csharp
        using var progress = new ConsoleProgressBar("  apply   ", total);
        using var file = File.OpenRead(path);
        using var counted = new CountingStream(file, progress.Report);
```

with:

```csharp
        _progress.BeginPhase(UpdatePhase.Apply, day: 0, total, initial: 0);
        using var file = File.OpenRead(path);
        using var counted = new CountingStream(file, _progress.Report);
```

Then, after the final `Flush(pending, pendingRedirects);` and before `return new IncrementStats(...)` (currently line 195-197), add the phase end:

```csharp
        Flush(pending, pendingRedirects);

        _progress.EndPhase();

        return new IncrementStats(articles, redirects, skipped);
```

- [ ] **Step 9: Run the new test and the full updater suite**

Run: `cd D:/SmartCodeGenerator-Repository && dotnet test DomainDiscovery.Tests --filter "FullyQualifiedName~IncrementalUpdaterTests"`
Expected: PASS — the new test plus all existing `IncrementalUpdaterTests` (they pass `say: _ => {}`, default to `ConsoleBarProgress`, and assert on store contents, not the console).

- [ ] **Step 10: Commit**

```bash
cd D:/SmartCodeGenerator-Repository && git add DomainDiscovery/Wikipedia/Dump/UpdatePhase.cs DomainDiscovery/Wikipedia/Dump/IStoreUpdateProgress.cs DomainDiscovery/Wikipedia/Dump/ConsoleBarProgress.cs DomainDiscovery/Wikipedia/Dump/IncrementalUpdater.cs DomainDiscovery.Tests/Wikipedia/Dump/IncrementalUpdaterTests.cs && git commit -m "Route IncrementalUpdater progress through an injectable sink

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: `OverallPercentProgress` and `StoreUpdateText`

The pure core: turn per-phase byte reports into one overall percentage, and produce the user-facing strings. No console side effects — fully unit-tested.

**Files:**
- Create: `DomainDiscovery/Wikipedia/Dump/OverallPercentProgress.cs`
- Create: `DomainDiscovery/Wikipedia/Dump/StoreUpdateText.cs`
- Test: `DomainDiscovery.Tests/Wikipedia/Dump/OverallPercentProgressTests.cs`
- Test: `DomainDiscovery.Tests/Wikipedia/Dump/StoreUpdateTextTests.cs`

**Interfaces:**
- Consumes: `IStoreUpdateProgress`, `UpdatePhase`, `StartupUpdateOutcome`.
- Produces: `OverallPercentProgress(Action<int> onPercent, Action<StartupUpdateOutcome, int> onFinish)`.
- Produces: `static class StoreUpdateText` with `string Updating(int percent)` and `string Final(StartupUpdateOutcome outcome, int daysApplied)`.

- [ ] **Step 1: Write the failing percentage tests**

`DomainDiscovery.Tests/Wikipedia/Dump/OverallPercentProgressTests.cs`:

```csharp
using DomainDiscovery.Wikipedia.Dump;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

public sealed class OverallPercentProgressTests
{
    private static (OverallPercentProgress Progress, List<int> Percents) New()
    {
        var percents = new List<int>();
        var progress = new OverallPercentProgress(percents.Add, (_, _) => { });
        return (progress, percents);
    }

    [Fact]
    public void SingleDay_DownloadFillsFirstHalf_ApplyFillsSecond()
    {
        var (progress, percents) = New();

        progress.BeginDay(0, 1);

        progress.BeginPhase(UpdatePhase.Download, day: 0, total: 100, initial: 0);
        progress.Report(50);   // 25%
        progress.EndPhase();   // 50%

        progress.BeginPhase(UpdatePhase.Apply, day: 0, total: 100, initial: 0);
        progress.Report(50);   // 75%
        progress.EndPhase();   // 100%

        Assert.Equal([0, 25, 50, 75, 100], percents);
    }

    [Fact]
    public void MultipleDays_ProgressIsMonotonic_AndReachesOneHundred()
    {
        var (progress, percents) = New();

        for (var day = 0; day < 3; day++)
        {
            progress.BeginDay(day, 3);
            progress.BeginPhase(UpdatePhase.Download, day, total: 10, initial: 0);
            progress.Report(5);
            progress.EndPhase();
            progress.BeginPhase(UpdatePhase.Apply, day, total: 10, initial: 0);
            progress.Report(5);
            progress.EndPhase();
        }

        for (var i = 1; i < percents.Count; i++)
            Assert.True(percents[i] >= percents[i - 1], "percentage must never go backwards");

        Assert.Equal(100, percents[^1]);
    }

    [Fact]
    public void ZeroTotal_DoesNotDivideByZero()
    {
        var (progress, percents) = New();

        progress.BeginDay(0, 1);
        progress.BeginPhase(UpdatePhase.Download, day: 0, total: 0, initial: 0);
        progress.Report(0);

        Assert.Equal([0], percents);
    }

    [Fact]
    public void Finish_ForwardsOutcomeAndCount()
    {
        StartupUpdateOutcome? seen = null;
        var seenDays = -1;
        var progress = new OverallPercentProgress(_ => { }, (o, d) => { seen = o; seenDays = d; });

        progress.Finish(StartupUpdateOutcome.Completed, 3);

        Assert.Equal(StartupUpdateOutcome.Completed, seen);
        Assert.Equal(3, seenDays);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd D:/SmartCodeGenerator-Repository && dotnet test DomainDiscovery.Tests --filter "FullyQualifiedName~OverallPercentProgressTests"`
Expected: FAIL to compile — `OverallPercentProgress` does not exist.

- [ ] **Step 3: Implement `OverallPercentProgress`**

`DomainDiscovery/Wikipedia/Dump/OverallPercentProgress.cs`:

```csharp
namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// Turns the updater's per-phase byte reports into one overall percentage across every pending day,
/// and hands each new whole-percent value to a renderer. Each day is an equal <c>1/dayCount</c>
/// slice; within a day the download fills the first half of that slice and the apply the second.
/// </summary>
/// <remarks>
/// De-dupes on the integer percent, so a renderer is called at most 101 times over a whole update —
/// which is the throttle. Every method is pure math and cannot throw into <c>CountingStream</c>.
/// </remarks>
public sealed class OverallPercentProgress : IStoreUpdateProgress
{
    private readonly Action<int> _onPercent;
    private readonly Action<StartupUpdateOutcome, int> _onFinish;

    private int _dayIndex;
    private int _dayCount = 1;
    private UpdatePhase _phase;
    private long _total;
    private int _lastPercent = -1;

    public OverallPercentProgress(Action<int> onPercent, Action<StartupUpdateOutcome, int> onFinish)
    {
        _onPercent = onPercent;
        _onFinish = onFinish;
    }

    public void BeginDay(int dayIndex, int dayCount)
    {
        _dayIndex = dayIndex;
        _dayCount = dayCount < 1 ? 1 : dayCount;
    }

    public void BeginPhase(UpdatePhase phase, int day, long total, long initial)
    {
        _phase = phase;
        _total = total;
        Emit(initial);
    }

    public void Report(long done) => Emit(done);

    public void EndPhase() => Emit(_total);

    public void Finish(StartupUpdateOutcome outcome, int daysApplied)
        => _onFinish(outcome, daysApplied);

    private void Emit(long done)
    {
        var fraction = _total > 0 ? Math.Clamp((double)done / _total, 0, 1) : 0;

        var phaseFraction = _phase == UpdatePhase.Download
            ? 0.5 * fraction
            : 0.5 + 0.5 * fraction;

        var overall = (_dayIndex + phaseFraction) / _dayCount;
        var percent = Math.Clamp((int)Math.Round(overall * 100), 0, 100);

        if (percent == _lastPercent)
            return;

        _lastPercent = percent;
        _onPercent(percent);
    }
}
```

- [ ] **Step 4: Run to verify percentage tests pass**

Run: `cd D:/SmartCodeGenerator-Repository && dotnet test DomainDiscovery.Tests --filter "FullyQualifiedName~OverallPercentProgressTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Write the failing message tests**

`DomainDiscovery.Tests/Wikipedia/Dump/StoreUpdateTextTests.cs`:

```csharp
using DomainDiscovery.Wikipedia.Dump;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

public sealed class StoreUpdateTextTests
{
    [Fact]
    public void Updating_ReadsAsAPercentageLine()
        => Assert.Equal("Wikipedia store updating 42%", StoreUpdateText.Updating(42));

    [Fact]
    public void Final_Completed_WithDays_SaysHowManyLanded()
        => Assert.Equal(
            "Wikipedia store: up to date (3 day(s) applied).",
            StoreUpdateText.Final(StartupUpdateOutcome.Completed, 3));

    [Fact]
    public void Final_Completed_WithNoDays_JustSaysUpToDate()
        => Assert.Equal(
            "Wikipedia store: up to date.",
            StoreUpdateText.Final(StartupUpdateOutcome.Completed, 0));

    [Fact]
    public void Final_Skipped_AndFailed_SayTheDaysAreKept()
    {
        Assert.Contains("stopped", StoreUpdateText.Final(StartupUpdateOutcome.Skipped, 2));
        Assert.Contains("kept", StoreUpdateText.Final(StartupUpdateOutcome.Skipped, 2));
        Assert.Contains("failed", StoreUpdateText.Final(StartupUpdateOutcome.Failed, 1));
        Assert.Contains("kept", StoreUpdateText.Final(StartupUpdateOutcome.Failed, 1));
    }
}
```

- [ ] **Step 6: Run to verify it fails**

Run: `cd D:/SmartCodeGenerator-Repository && dotnet test DomainDiscovery.Tests --filter "FullyQualifiedName~StoreUpdateTextTests"`
Expected: FAIL to compile — `StoreUpdateText` does not exist.

- [ ] **Step 7: Implement `StoreUpdateText`**

`DomainDiscovery/Wikipedia/Dump/StoreUpdateText.cs`:

```csharp
namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// The user-facing strings for a background store update — kept pure and separate from the console
/// so they can be tested without a console. Renderers add their own prefix (title vs. log line).
/// </summary>
public static class StoreUpdateText
{
    public static string Updating(int percent) => $"Wikipedia store updating {percent}%";

    public static string Final(StartupUpdateOutcome outcome, int daysApplied) => outcome switch
    {
        StartupUpdateOutcome.Completed when daysApplied == 0
            => "Wikipedia store: up to date.",
        StartupUpdateOutcome.Completed
            => $"Wikipedia store: up to date ({daysApplied} day(s) applied).",
        StartupUpdateOutcome.Skipped
            => $"Wikipedia store: update stopped; {daysApplied} day(s) applied and kept.",
        StartupUpdateOutcome.Failed
            => $"Wikipedia store: update failed; {daysApplied} day(s) applied and kept.",
        _ => "Wikipedia store: up to date.",
    };
}
```

- [ ] **Step 8: Run both new suites**

Run: `cd D:/SmartCodeGenerator-Repository && dotnet test DomainDiscovery.Tests --filter "FullyQualifiedName~OverallPercentProgressTests|FullyQualifiedName~StoreUpdateTextTests"`
Expected: PASS (8 tests).

- [ ] **Step 9: Commit**

```bash
cd D:/SmartCodeGenerator-Repository && git add DomainDiscovery/Wikipedia/Dump/OverallPercentProgress.cs DomainDiscovery/Wikipedia/Dump/StoreUpdateText.cs DomainDiscovery.Tests/Wikipedia/Dump/OverallPercentProgressTests.cs DomainDiscovery.Tests/Wikipedia/Dump/StoreUpdateTextTests.cs && git commit -m "Add overall-percentage progress sink and update message strings

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Make concurrent-writer safety explicit on the store connection

**Revised 2026-07-20 after investigation.** The original task added `PRAGMA busy_timeout = 5000`. Investigation during execution disproved its premise: **Microsoft.Data.Sqlite 10.0.9 already retries a locked write** until the connection's `Default Timeout` (default **30 s**), independent of the native `busy_timeout` pragma. A concurrent writer already *waits and succeeds* against the unmodified `Open()` (verified empirically — the proposed RED test passed with no change). Adding `busy_timeout = 5000` would only *shorten* the effective wait ceiling from ~30 s to 5 s — the opposite of the goal.

So the store's concurrent-writer safety is real but **implicit**: it rides on the library's default `Default Timeout`. This task makes it **explicit and defensive** — pin `Default Timeout = 30` on the connection string with a comment, so a future edit (or a library default change) cannot silently remove the safety net. No native `busy_timeout`. No flaky timing test — a documented rationale in the code instead, per the decision recorded in the progress ledger.

**Files:**
- Modify: `DomainDiscovery/Wikipedia/Dump/WikipediaStore.cs:29-34` (the constructor's `SqliteConnectionStringBuilder`)

**Interfaces:**
- No public API change; `Open()` is untouched. Only the connection string gains an explicit `DefaultTimeout`.

- [ ] **Step 1: Pin `Default Timeout` explicitly, with rationale**

In `DomainDiscovery/Wikipedia/Dump/WikipediaStore.cs`, replace the constructor (currently lines 29-34):

```csharp
    public WikipediaStore(string databasePath)
        => _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
```

with:

```csharp
    public WikipediaStore(string databasePath)
        => _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,

            // Concurrent-writer safety, made explicit. The background startup update and a foreground
            // article refresh can both write to this store at once; under WAL the second writer meets
            // SQLITE_BUSY. Microsoft.Data.Sqlite retries a busy write until this timeout rather than
            // failing at once — so a blocked writer waits (up to 30 s) and then succeeds. 30 s IS the
            // library's current default, but it is pinned here so a future edit or a library default
            // change cannot silently remove the safety net. A shorter value would only make blocked
            // writers fail sooner; a native PRAGMA busy_timeout was deliberately NOT used, as it would
            // cap this managed retry below 30 s. Batches commit sub-second, so real waits are brief.
            DefaultTimeout = 30
        }.ToString();
```

- [ ] **Step 2: Build**

Run: `cd D:/SmartCodeGenerator-Repository && dotnet build DomainDiscovery`
Expected: Build succeeded. (`DefaultTimeout` is a real `SqliteConnectionStringBuilder` property in Microsoft.Data.Sqlite 10.0.9; it maps to the "Default Timeout" connection-string keyword.)

- [ ] **Step 3: Run the whole store suite to confirm no regression**

Run: `cd D:/SmartCodeGenerator-Repository && dotnet test DomainDiscovery.Tests --filter "FullyQualifiedName~WikipediaStore"`
Expected: PASS — the store still opens, reads, and writes exactly as before (the value pinned equals the previous implicit default, so behavior is unchanged; this guards intent, not a behavior change).

> **No new test.** A behavioral concurrency test cannot distinguish "explicit 30 s" from "implicit 30 s" (both pass), and a timing-based one is flaky. The rationale lives in the code comment and this task; the existing store suite covers no-regression. This is the documented-note decision from the ledger.

- [ ] **Step 4: Commit**

```bash
cd D:/SmartCodeGenerator-Repository && git add DomainDiscovery/Wikipedia/Dump/WikipediaStore.cs && git commit -m "Pin the store connection's Default Timeout so concurrent writers wait

Microsoft.Data.Sqlite already retries a locked write until the connection's
Default Timeout (30s default), so a background update and a foreground refresh
already coexist. Pin that 30s explicitly with a rationale so a future edit
can't silently drop the safety net. A native busy_timeout was avoided — it
would cap the managed retry below 30s.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: `BackgroundStoreUpdate` orchestrator

Wrap the existing `StartupDumpUpdate` day-loop so each day drives `BeginDay`, and expose a fire-and-forget `Start` that checks freshness and runs the apply on a background task. The `Run` core is tested with fakes; `Start` is thin console/network glue verified at Task 5.

**Files:**
- Create: `DomainDiscovery/Wikipedia/Dump/BackgroundStoreUpdate.cs`
- Test: `DomainDiscovery.Tests/Wikipedia/Dump/BackgroundStoreUpdateTests.cs`

**Interfaces:**
- Consumes: `DumpStaleness`, `IStoreUpdateProgress`, `StartupDumpUpdate` + its `ApplyDay` delegate, `StartupUpdateResult`, `StartupUpdateOutcome`, `IncrementalUpdater`, `WikipediaStore`, `DumpFreshness`, `StoreUpdateText`, `OverallPercentProgress`.
- Produces: `StartupUpdateResult Run(DumpStaleness staleness, IStoreUpdateProgress progress, StartupDumpUpdate.ApplyDay apply, CancellationToken cancellation)`.
- Produces: `Task Start(string databasePath, bool redirected, CancellationToken cancellation)`.

- [ ] **Step 1: Write the failing `Run` tests**

`DomainDiscovery.Tests/Wikipedia/Dump/BackgroundStoreUpdateTests.cs`:

```csharp
using DomainDiscovery.Wikipedia.Dump;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

public sealed class BackgroundStoreUpdateTests
{
    private sealed class RecordingProgress : IStoreUpdateProgress
    {
        public List<(int Index, int Count)> Days { get; } = [];
        public StartupUpdateOutcome? FinishedWith { get; private set; }
        public int FinishedDays { get; private set; }

        public void BeginDay(int dayIndex, int dayCount) => Days.Add((dayIndex, dayCount));
        public void BeginPhase(UpdatePhase phase, int day, long total, long initial) { }
        public void Report(long done) { }
        public void EndPhase() { }
        public void Finish(StartupUpdateOutcome outcome, int daysApplied)
        {
            FinishedWith = outcome;
            FinishedDays = daysApplied;
        }
    }

    private static DumpStaleness Stale(params int[] days) => new(20260701, days, Answered: true);

    [Fact]
    public void Run_BeginsEachDayInOrder_AndFinishesCompleted()
    {
        var progress = new RecordingProgress();
        var applied = new List<int>();

        var result = new BackgroundStoreUpdate().Run(
            Stale(20260702, 20260703, 20260704),
            progress,
            (day, _) => applied.Add(day),
            CancellationToken.None);

        Assert.Equal([20260702, 20260703, 20260704], applied);
        Assert.Equal([(0, 3), (1, 3), (2, 3)], progress.Days);
        Assert.Equal(StartupUpdateOutcome.Completed, result.Outcome);
        Assert.Equal(StartupUpdateOutcome.Completed, progress.FinishedWith);
        Assert.Equal(3, progress.FinishedDays);
    }

    [Fact]
    public void Run_DoesNothingButFinish_WhenNotStale()
    {
        var progress = new RecordingProgress();
        var applied = new List<int>();

        var result = new BackgroundStoreUpdate().Run(
            DumpStaleness.Unknown,
            progress,
            (day, _) => applied.Add(day),
            CancellationToken.None);

        Assert.Empty(applied);
        Assert.Empty(progress.Days);
        Assert.Equal(StartupUpdateOutcome.NothingToDo, result.Outcome);
        Assert.Equal(StartupUpdateOutcome.NothingToDo, progress.FinishedWith);
    }

    [Fact]
    public void Run_ReportsSkipped_AndKeepsAppliedCount_WhenCancelledMidway()
    {
        using var interrupt = new CancellationTokenSource();
        var progress = new RecordingProgress();
        var applied = new List<int>();

        var result = new BackgroundStoreUpdate().Run(
            Stale(20260702, 20260703, 20260704),
            progress,
            (day, cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested();
                applied.Add(day);
                interrupt.Cancel();
            },
            interrupt.Token);

        Assert.Equal([20260702], applied);
        Assert.Equal(StartupUpdateOutcome.Skipped, result.Outcome);
        Assert.Equal(StartupUpdateOutcome.Skipped, progress.FinishedWith);
        Assert.Equal(1, progress.FinishedDays);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd D:/SmartCodeGenerator-Repository && dotnet test DomainDiscovery.Tests --filter "FullyQualifiedName~BackgroundStoreUpdateTests"`
Expected: FAIL to compile — `BackgroundStoreUpdate` does not exist.

- [ ] **Step 3: Implement `BackgroundStoreUpdate`**

`DomainDiscovery/Wikipedia/Dump/BackgroundStoreUpdate.cs`:

```csharp
namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// Runs the daily-increment update in the background while the app is used, reporting progress
/// through an <see cref="IStoreUpdateProgress"/> instead of blocking the prompt. Whatever happens —
/// offline, interrupted, failed — the app keeps running.
/// </summary>
public sealed class BackgroundStoreUpdate
{
    /// <summary>
    /// Applies the pending days, driving <paramref name="progress"/> as it goes, and returns how it
    /// ended. The day loop is <see cref="StartupDumpUpdate"/>'s, so the resume guarantees hold: only
    /// a day that fully lands moves the store's date.
    /// </summary>
    public StartupUpdateResult Run(
        DumpStaleness staleness, IStoreUpdateProgress progress,
        StartupDumpUpdate.ApplyDay apply, CancellationToken cancellation)
    {
        var count = staleness.PendingDays.Count;
        var index = 0;

        var result = new StartupDumpUpdate().Run(
            staleness,
            (day, token) =>
            {
                progress.BeginDay(index, count);
                apply(day, token);
                index++;
            },
            cancellation);

        progress.Finish(result.Outcome, result.DaysApplied);
        return result;
    }

    /// <summary>
    /// Fire-and-forget: checks freshness and, if the store is stale, applies the pending days on a
    /// background task. Never throws into the caller and never blocks it. Returns the task so the app
    /// can cancel it on exit.
    /// </summary>
    /// <param name="redirected">
    /// Whether output is redirected. Interactive sessions get the percentage in the console title;
    /// piped/scripted ones get a throttled log line, since there is no title or cursor to use.
    /// </param>
    public Task Start(string databasePath, bool redirected, CancellationToken cancellation)
        => Task.Run(() =>
        {
            try
            {
                var store = new WikipediaStore(databasePath);
                var staleness = new DumpFreshness().Check(store);
                var progress = BuildProgress(redirected);

                if (!staleness.IsStale)
                {
                    // Up to date, or Wikimedia never answered. Say "up to date" ONLY when we actually
                    // know it is — a check that went unanswered knows nothing.
                    if (staleness.IsCurrent)
                        progress.Finish(StartupUpdateOutcome.Completed, 0);
                    return;
                }

                var working = Path.Combine(Path.GetDirectoryName(databasePath) ?? ".", "incr");
                var say = redirected ? (Action<string>)Console.WriteLine : _ => { };
                var updater = new IncrementalUpdater(store, say: say, progress: progress);

                Run(staleness, progress,
                    (day, token) => updater.UpdateDay(day, working, token), cancellation);
            }
            catch
            {
                // Being unable to update is never fatal: the app runs on the store as it stands.
            }
        }, cancellation);

    private static IStoreUpdateProgress BuildProgress(bool redirected)
    {
        if (redirected)
            return new OverallPercentProgress(
                percent => Console.WriteLine($"  {StoreUpdateText.Updating(percent)}"),
                (outcome, days) => Console.WriteLine($"  {StoreUpdateText.Final(outcome, days)}"));

        return new OverallPercentProgress(
            percent => SetTitle(StoreUpdateText.Updating(percent)),
            (outcome, days) => SetTitle(StoreUpdateText.Final(outcome, days)));
    }

    private static void SetTitle(string text)
    {
        // Setting the title can throw when no console is attached; progress is cosmetic, so swallow.
        try { Console.Title = $"CodeGenerator — {text}"; }
        catch { /* no console; nothing to show */ }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd D:/SmartCodeGenerator-Repository && dotnet test DomainDiscovery.Tests --filter "FullyQualifiedName~BackgroundStoreUpdateTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
cd D:/SmartCodeGenerator-Repository && git add DomainDiscovery/Wikipedia/Dump/BackgroundStoreUpdate.cs DomainDiscovery.Tests/Wikipedia/Dump/BackgroundStoreUpdateTests.cs && git commit -m "Add BackgroundStoreUpdate orchestrator

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: Wire into `CodeGeneratorStarter`

Replace the synchronous freshness-check-and-apply with an instant foreground missing-store warning plus a background launch, and cancel the background task on exit.

**Files:**
- Modify: `CodeGenerator/App/CodeGeneratorStarter.cs`

**Interfaces:**
- Consumes: `BackgroundStoreUpdate`, `DomainDiscoveryPipeline.DefaultDumpDatabasePath` (existing), `Console.IsOutputRedirected`.

- [ ] **Step 1: Add background-task fields**

In `CodeGenerator/App/CodeGeneratorStarter.cs`, add two fields alongside the existing private fields (near line 19-25):

```csharp
    private CancellationTokenSource? _storeUpdateCts;
    private Task? _storeUpdateTask;
```

- [ ] **Step 2: Point the header at the new launcher**

In `PrintHeader()` (line 192), replace the call:

```csharp
        ReportWikipediaStore();
```

with:

```csharp
        StartWikipediaStoreUpdate();
```

- [ ] **Step 3: Replace `ReportWikipediaStore` and `ApplyPendingDays` with the launcher**

Delete the entire `ReportWikipediaStore()` method (lines 195-289) and the entire `ApplyPendingDays(...)` method (lines 291-328), and in their place add:

```csharp
    /// <summary>
    /// Reports a missing store loudly (and stops), or launches the daily-increment update in the
    /// BACKGROUND and returns at once. The prompt no longer waits on the network or on gigabytes of
    /// downloads: progress shows in the console title (interactive) or as a throttled log line
    /// (piped). A missing store is a warning, not an error — the app starts on the online-API fallback.
    /// </summary>
    private void StartWikipediaStoreUpdate()
    {
        var databasePath = DomainDiscoveryPipeline.DefaultDumpDatabasePath;

        if (!File.Exists(databasePath))
        {
            // The store lives in a git-ignored directory, so an external clean, a disk-cleanup tool or
            // a manual delete removes it, and the app would otherwise slip to the online API without a
            // word. Say so, loudly — but do not stop. A fresh clone that never imported the store
            // depends on exactly that fallback to run 'build app:' at all.
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

        var redirected = Console.IsOutputRedirected;

        // The announcement is for a human at a terminal; a piped/scripted run gets the throttled
        // percent lines instead, so its transcript is not padded with a line a script must skip.
        if (!redirected)
        {
            Console.WriteLine(
                "Wikipedia store: checking for updates in the background (progress shown in the title bar).");
            Console.WriteLine();
        }

        _storeUpdateCts = new CancellationTokenSource();
        _storeUpdateTask =
            new BackgroundStoreUpdate().Start(databasePath, redirected, _storeUpdateCts.Token);
    }
```

- [ ] **Step 4: Cancel the background task on exit**

In `Run()`, replace the exit check (lines 124-125):

```csharp
            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;
```

with:

```csharp
            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                // A half-done background update is safe to abandon: the store's date moved only for
                // days that fully landed, so the next start resumes at the one in progress.
                _storeUpdateCts?.Cancel();
                break;
            }
```

- [ ] **Step 5: Build the whole solution**

Run: `cd D:/SmartCodeGenerator-Repository && dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx`
Expected: Build succeeded. (If `WikipediaStore`, `DumpFreshness`, or `IncrementalUpdater` symbols are unresolved in `CodeGeneratorStarter`, confirm the existing `using DomainDiscovery.Wikipedia.Dump;` at the top of the file is still present — it is used by the deleted methods and must remain.)

- [ ] **Step 6: Run the full test suite**

Run: `cd D:/SmartCodeGenerator-Repository && dotnet test`
Expected: PASS — all suites, including the four new/modified test files.

- [ ] **Step 7: Rebuild the CLI, then smoke-test non-interactively**

The prior `dotnet test` leaves the CLI's `bin` stale (project memory). Rebuild the CLI first, then confirm a piped session returns to the shell **immediately** instead of blocking on the update:

Run:
```bash
cd D:/SmartCodeGenerator-Repository && dotnet build CodeGenerator -c Debug && printf 'exit\n' | dotnet run --project CodeGenerator -c Debug --no-build
```
Expected: the banner and help print, the prompt is reached, and the process exits promptly. If the store is stale and reachable, one or more `  Wikipedia store updating xx%` lines appear (they may interleave with the exit); if it is current or offline, none do. The command must NOT hang for minutes downloading before returning.

- [ ] **Step 8: Commit**

```bash
cd D:/SmartCodeGenerator-Repository && git add CodeGenerator/App/CodeGeneratorStarter.cs && git commit -m "Run the Wikipedia store update in the background at startup

The prompt now appears immediately; the freshness check and daily-increment
apply run on a background task, with progress shown as 'Wikipedia store
updating xx%' in the console title (interactive) or a log line (piped).
Cancelled cleanly on exit.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-Review

**1. Spec coverage:**
- Prompt appears immediately / update runs in background → Task 5 (`StartWikipediaStoreUpdate` launches a `Task`, returns at once) + Task 4 (`Start`).
- `Wikipedia store updating xx%` in title (interactive) → Task 4 `BuildProgress`/`SetTitle` + Task 2 `StoreUpdateText.Updating`.
- Throttled log line when redirected → Task 4 `BuildProgress` (redirected branch) + Task 2 (integer-percent de-dupe is the throttle).
- After-header announcement line → Task 5 Step 3 (interactive only).
- Entire freshness check moved to background → Task 4 `Start` (calls `DumpFreshness().Check` inside the task).
- Missing store stays foreground, loud, non-fatal → Task 5 Step 3.
- Cancel on exit; applied days kept; resume → Task 5 Step 4 + reuse of `StartupDumpUpdate`/`UpdateDay` (unchanged).
- Progress plumbing via injected sink; `ConsoleProgressBar` retained for importer → Task 1.
- Overall-% formula (day slices, download=first half, apply=second) → Task 2 `OverallPercentProgress.Emit`.
- `Report` must not throw → Task 1 interface remark + Task 2 pure math.
- `busy_timeout` concurrency hardening → Task 3.
- `RunOnce` untouched → not modified in any task (only `Run`/`PrintHeader` change).
- Testing per spec → Tasks 1-4 each ship unit tests; console side effects isolated behind lambdas/adapters.

**2. Placeholder scan:** No TBD/TODO/"handle edge cases"/"similar to Task N". Every code step shows complete code; every run step shows the command and expected result.

**3. Type consistency:** `IStoreUpdateProgress` members (`BeginDay`, `BeginPhase`, `Report`, `EndPhase`, `Finish`) are identical across Tasks 1, 2, 4 and both `RecordingProgress` fakes. `BeginPhase(UpdatePhase, int, long, long)` matches every call site (`IncrementalUpdater.Download`/`Apply`, `OverallPercentProgress`, the fakes). `OverallPercentProgress(Action<int>, Action<StartupUpdateOutcome,int>)` matches its uses in Task 2 tests and Task 4 `BuildProgress`. `BackgroundStoreUpdate.Run(DumpStaleness, IStoreUpdateProgress, StartupDumpUpdate.ApplyDay, CancellationToken)` matches the Task 4 tests. `IncrementalUpdater(..., IStoreUpdateProgress? progress = null)` matches Task 1 test and Task 4 `Start`. `StoreUpdateText.Updating(int)`/`Final(StartupUpdateOutcome,int)` consistent across Tasks 2 and 4.

No gaps found.
