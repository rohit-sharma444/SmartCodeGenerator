# Wikipedia Update Windows Service Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the daily Wikipedia-increment update out of the CodeGenerator CLI process into a Windows Service that checks at machine start and every 24 hours thereafter, leaving the CLI as a read-only reporter of store and service status.

**Architecture:** The orchestration already exists and is console-free — `BackgroundStoreUpdate.Run` takes an injected `IStoreUpdateProgress`. Tasks 1-3 add the pieces a headless host needs (a JSON status file, a progress sink that writes it, and a check-apply-wait cycle), all in `DomainDiscovery` so they are unit-testable without a service host. Task 4 wraps them in a thin `BackgroundService`. Task 5 strips the update out of the CLI. Task 6 installs it.

**Tech Stack:** .NET 10, `Microsoft.Extensions.Hosting.WindowsServices`, `System.Diagnostics.EventLog`, `System.ServiceProcess.ServiceController`, xunit, `sc.exe` via PowerShell.

## Global Constraints

- Spec: [`docs/superpowers/specs/2026-07-24-wikipedia-update-service-design.md`](../specs/2026-07-24-wikipedia-update-service-design.md)
- Target framework is `net10.0` from `Directory.Build.props`; `ImplicitUsings` and `Nullable` are enabled. The service project overrides the TFM to `net10.0-windows` (see Task 4).
- **CI builds `SmartCodeGenerator/SmartCodeGenerator.slnx` in Release with `-warnaserror`.** The local `CodeGenerator` Debug build does not surface analyzer errors and runs only a subset of the test projects. A green local build is not evidence of a green CI build — run `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror` before every commit.
- Every new project must be added to `SmartCodeGenerator/SmartCodeGenerator.slnx`.
- **Nothing in the update path may throw into the caller.** Being unable to update is never fatal.
- **`IStoreUpdateProgress.Report` must never throw.** It is reached from `CountingStream`, and `BZip2InputStream` treats any exception from the stream it reads as end-of-input — a throwing report silently truncates the apply.
- Service name: `SmartCodeGenWikipediaUpdater`. Event Log source: `SmartCodeGen Wikipedia Updater`. Status file name: `update-status.json`, written beside the store.
- Do **not** add a native `busy_timeout` pragma to `WikipediaStore`. Microsoft.Data.Sqlite already retries a busy write until the connection's `Default Timeout`, pinned at 30 s; a native pragma would only shorten that ceiling.
- `dotnet test` leaves the CLI's `bin` stale. Rebuild `CodeGenerator` before verifying any CLI-side change by hand.

---

### Task 1: Status record and status file

**Files:**
- Create: `DomainDiscovery/Wikipedia/Dump/StoreUpdateStatus.cs`
- Create: `DomainDiscovery/Wikipedia/Dump/StoreUpdateStatusFile.cs`
- Test: `DomainDiscovery.Tests/Wikipedia/Dump/StoreUpdateStatusFileTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum StoreUpdateState { Unknown, Idle, Checking, Applying, Failed }`
  - `sealed record StoreUpdateStatus(StoreUpdateState State, int Percent, int PendingDays, int? CurrentDay, DateTimeOffset? LastSuccessUtc, string? LastError, string? ServiceVersion)` with `static readonly StoreUpdateStatus Unknown`
  - `sealed class StoreUpdateStatusFile` with `StoreUpdateStatusFile(string path)`, `static string BesideStore(string databasePath)`, `void Write(StoreUpdateStatus status)`, `StoreUpdateStatus Read()`

- [ ] **Step 1: Write the failing tests**

Create `DomainDiscovery.Tests/Wikipedia/Dump/StoreUpdateStatusFileTests.cs`:

```csharp
using DomainDiscovery.Wikipedia.Dump;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

public sealed class StoreUpdateStatusFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "scg-status-" + Guid.NewGuid().ToString("N"));

    private string Path_ => System.IO.Path.Combine(_directory, "update-status.json");

    public StoreUpdateStatusFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { /* best effort */ }
    }

    private static StoreUpdateStatus Sample() => new(
        StoreUpdateState.Applying, 43, 6, 20260718,
        new DateTimeOffset(2026, 7, 23, 4, 11, 2, TimeSpan.Zero), null, "1.0");

    [Fact]
    public void Write_ThenRead_RoundTripsEveryField()
    {
        var file = new StoreUpdateStatusFile(Path_);
        file.Write(Sample());

        Assert.Equal(Sample(), file.Read());
    }

    [Fact]
    public void Read_MissingFile_IsUnknown()
        => Assert.Equal(StoreUpdateStatus.Unknown, new StoreUpdateStatusFile(Path_).Read());

    [Fact]
    public void Read_EmptyFile_IsUnknown()
    {
        File.WriteAllText(Path_, string.Empty);
        Assert.Equal(StoreUpdateStatus.Unknown, new StoreUpdateStatusFile(Path_).Read());
    }

    [Fact]
    public void Read_MalformedJson_IsUnknown()
    {
        File.WriteAllText(Path_, "{ not json at all");
        Assert.Equal(StoreUpdateStatus.Unknown, new StoreUpdateStatusFile(Path_).Read());
    }

    [Fact]
    public void Read_TruncatedJson_IsUnknown()
    {
        // A reader must survive catching a writer mid-flight, even though Write is atomic.
        File.WriteAllText(Path_, "{ \"state\": \"Applying\", \"percent\": 4");
        Assert.Equal(StoreUpdateStatus.Unknown, new StoreUpdateStatusFile(Path_).Read());
    }

    [Fact]
    public void Write_ToAnUnwritableLocation_DoesNotThrow()
    {
        // Status is a convenience, never a reason to fail an update.
        var file = new StoreUpdateStatusFile(
            System.IO.Path.Combine(_directory, "no-such-dir", "update-status.json"));

        file.Write(Sample());
        Assert.Equal(StoreUpdateStatus.Unknown, file.Read());
    }

    [Fact]
    public void Write_OverwritesAPreviousStatus()
    {
        var file = new StoreUpdateStatusFile(Path_);
        file.Write(Sample());
        var later = Sample() with { Percent = 91, State = StoreUpdateState.Idle };
        file.Write(later);

        Assert.Equal(later, file.Read());
    }

    [Fact]
    public void BesideStore_PutsTheStatusNextToTheDatabase()
        => Assert.Equal(
            System.IO.Path.Combine(@"D:\Dump", "update-status.json"),
            StoreUpdateStatusFile.BesideStore(@"D:\Dump\enwiki-full.db"));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DomainDiscovery.Tests --filter StoreUpdateStatusFileTests`

Expected: FAIL to compile — `StoreUpdateStatus` and `StoreUpdateStatusFile` do not exist.

- [ ] **Step 3: Write the status record**

Create `DomainDiscovery/Wikipedia/Dump/StoreUpdateStatus.cs`:

```csharp
namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>What the updater is doing, as reported to anyone who cares to look.</summary>
public enum StoreUpdateState
{
    /// <summary>Nothing has said otherwise — no status file, or one that could not be read.</summary>
    Unknown,

    /// <summary>Not updating. Either up to date, or waiting for the next tick.</summary>
    Idle,

    /// <summary>Asking Wikimedia what has been published.</summary>
    Checking,

    /// <summary>Downloading and applying pending days.</summary>
    Applying,

    /// <summary>The last cycle broke. <see cref="StoreUpdateStatus.LastError"/> says how.</summary>
    Failed,
}

/// <summary>
/// A snapshot of the updater's progress, published for other processes to read.
/// </summary>
/// <remarks>
/// This is a <b>convenience, never a source of truth</b>. The store's own dump date is the truth,
/// and a reader that cannot get a status must carry on regardless — hence
/// <see cref="Unknown"/> rather than an exception.
/// </remarks>
public sealed record StoreUpdateStatus(
    StoreUpdateState State,
    int Percent,
    int PendingDays,
    int? CurrentDay,
    DateTimeOffset? LastSuccessUtc,
    string? LastError,
    string? ServiceVersion)
{
    /// <summary>Nothing is known: no file, an unreadable one, or one written by a newer version.</summary>
    public static readonly StoreUpdateStatus Unknown =
        new(StoreUpdateState.Unknown, 0, 0, null, null, null, null);
}
```

- [ ] **Step 4: Write the status file**

Create `DomainDiscovery/Wikipedia/Dump/StoreUpdateStatusFile.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// Publishes and reads <see cref="StoreUpdateStatus"/> as a small JSON file beside the store.
/// </summary>
/// <remarks>
/// <b>Neither direction may throw.</b> A write happens on the update's own thread, ultimately from
/// a progress callback, so a failing write must never disturb the apply. A read happens in the CLI
/// at startup, where an unreadable status must not stop the prompt appearing.
/// <para>
/// Writes are atomic — a temporary file in the same directory, then an overwriting move — so a
/// reader can never observe a half-written document. The tolerant read is belt-and-braces: it also
/// covers a file left behind by an older or newer version of this record.
/// </para>
/// </remarks>
public sealed class StoreUpdateStatusFile
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public StoreUpdateStatusFile(string path) => _path = path;

    /// <summary>The conventional status location for a given store: alongside the database.</summary>
    public static string BesideStore(string databasePath)
        => Path.Combine(Path.GetDirectoryName(databasePath) ?? ".", "update-status.json");

    public void Write(StoreUpdateStatus status)
    {
        try
        {
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(status, Json));
            File.Move(temporary, _path, overwrite: true);
        }
        catch
        {
            // Status is a convenience; the store's own dump date is the truth.
        }
    }

    public StoreUpdateStatus Read()
    {
        try
        {
            if (!File.Exists(_path))
                return StoreUpdateStatus.Unknown;

            return JsonSerializer.Deserialize<StoreUpdateStatus>(File.ReadAllText(_path), Json)
                ?? StoreUpdateStatus.Unknown;
        }
        catch
        {
            return StoreUpdateStatus.Unknown;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test DomainDiscovery.Tests --filter StoreUpdateStatusFileTests`

Expected: PASS — 8 tests.

- [ ] **Step 6: Verify the Release build is clean**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror`

Expected: Build succeeded, 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add DomainDiscovery/Wikipedia/Dump/StoreUpdateStatus.cs DomainDiscovery/Wikipedia/Dump/StoreUpdateStatusFile.cs DomainDiscovery.Tests/Wikipedia/Dump/StoreUpdateStatusFileTests.cs
git commit -m "feat(store-update): add a tolerant, atomically-written status file"
```

---

### Task 2: Status-publishing progress sink

**Files:**
- Create: `DomainDiscovery/Wikipedia/Dump/StatusFileProgress.cs`
- Test: `DomainDiscovery.Tests/Wikipedia/Dump/StatusFileProgressTests.cs`

**Interfaces:**
- Consumes: `StoreUpdateStatus`, `StoreUpdateState` (Task 1); the existing `IStoreUpdateProgress`, `OverallPercentProgress`, `UpdatePhase`, `StartupUpdateOutcome`, `StoreUpdateText`.
- Produces: `sealed class StatusFileProgress : IStoreUpdateProgress` with constructor
  `StatusFileProgress(Action<StoreUpdateStatus> publish, int pendingDays, DateTimeOffset? lastSuccessUtc, string? serviceVersion, Func<DateTimeOffset>? now = null)`
  and `static readonly TimeSpan PublishInterval`.

Existing signatures this task relies on (do not change them):

```csharp
public interface IStoreUpdateProgress
{
    void BeginDay(int dayIndex, int dayCount);
    void BeginPhase(UpdatePhase phase, int day, long total, long initial);
    void Report(long done);
    void EndPhase();
    void Finish(StartupUpdateOutcome outcome, int daysApplied);
}

public OverallPercentProgress(Action<int> onPercent, Action<StartupUpdateOutcome, int> onFinish);
```

- [ ] **Step 1: Write the failing tests**

Create `DomainDiscovery.Tests/Wikipedia/Dump/StatusFileProgressTests.cs`:

```csharp
using DomainDiscovery.Wikipedia.Dump;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

public sealed class StatusFileProgressTests
{
    private sealed class Clock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 7, 24, 9, 0, 0, TimeSpan.Zero);
        public DateTimeOffset Read() => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    private static (StatusFileProgress Progress, List<StoreUpdateStatus> Published, Clock Clock)
        Build(int pendingDays = 2, DateTimeOffset? lastSuccess = null)
    {
        var published = new List<StoreUpdateStatus>();
        var clock = new Clock();
        var progress = new StatusFileProgress(
            published.Add, pendingDays, lastSuccess, "1.0", clock.Read);
        return (progress, published, clock);
    }

    /// <summary>Drives one full day's worth of events so the aggregator reaches 100%.</summary>
    private static void RunOneDay(StatusFileProgress progress, int day)
    {
        progress.BeginDay(0, 1);
        progress.BeginPhase(UpdatePhase.Download, day, 100, 0);
        progress.Report(100);
        progress.EndPhase();
        progress.BeginPhase(UpdatePhase.Apply, day, 100, 0);
        progress.Report(100);
        progress.EndPhase();
    }

    [Fact]
    public void Reporting_PublishesApplyingWithTheCurrentDay()
    {
        var (progress, published, _) = Build();

        progress.BeginDay(0, 2);
        progress.BeginPhase(UpdatePhase.Download, 20260718, 100, 0);
        progress.Report(50);

        var status = Assert.Single(published);
        Assert.Equal(StoreUpdateState.Applying, status.State);
        Assert.Equal(20260718, status.CurrentDay);
        Assert.Equal(2, status.PendingDays);
        Assert.Equal("1.0", status.ServiceVersion);
    }

    [Fact]
    public void Reports_WithinTheInterval_PublishOnlyOnce()
    {
        var (progress, published, clock) = Build();

        progress.BeginDay(0, 1);
        progress.BeginPhase(UpdatePhase.Download, 20260718, 1000, 0);
        progress.Report(100);
        clock.Advance(StatusFileProgress.PublishInterval / 2);
        progress.Report(200);
        clock.Advance(StatusFileProgress.PublishInterval / 2);
        progress.Report(300);

        // First report publishes; the second is inside the window; the third crosses it.
        Assert.Equal(2, published.Count);
    }

    [Fact]
    public void Finish_Completed_PublishesIdleAndStampsLastSuccess()
    {
        var (progress, published, clock) = Build(pendingDays: 1);
        RunOneDay(progress, 20260718);

        progress.Finish(StartupUpdateOutcome.Completed, 1);

        var status = published[^1];
        Assert.Equal(StoreUpdateState.Idle, status.State);
        Assert.Equal(clock.Now, status.LastSuccessUtc);
        Assert.Null(status.LastError);
    }

    [Fact]
    public void Finish_Failed_PublishesFailedAndKeepsTheEarlierSuccess()
    {
        var earlier = new DateTimeOffset(2026, 7, 20, 3, 0, 0, TimeSpan.Zero);
        var (progress, published, _) = Build(pendingDays: 1, lastSuccess: earlier);
        RunOneDay(progress, 20260718);

        progress.Finish(StartupUpdateOutcome.Failed, 0);

        var status = published[^1];
        Assert.Equal(StoreUpdateState.Failed, status.State);
        Assert.Equal(earlier, status.LastSuccessUtc);
        Assert.False(string.IsNullOrWhiteSpace(status.LastError));
    }

    [Fact]
    public void Finish_Skipped_PublishesIdleWithoutAnError()
    {
        var (progress, published, _) = Build(pendingDays: 1);
        RunOneDay(progress, 20260718);

        progress.Finish(StartupUpdateOutcome.Skipped, 0);

        Assert.Equal(StoreUpdateState.Idle, published[^1].State);
        Assert.Null(published[^1].LastError);
    }

    [Fact]
    public void APublishThatThrows_NeverEscapes()
    {
        // Report is reached from CountingStream; BZip2InputStream treats any exception from the
        // stream it reads as end-of-input, so a throwing publish would silently truncate the apply.
        var progress = new StatusFileProgress(
            _ => throw new IOException("disk full"), 1, null, "1.0");

        progress.BeginDay(0, 1);
        progress.BeginPhase(UpdatePhase.Download, 20260718, 100, 0);

        var report = Record.Exception(() => progress.Report(50));
        var finish = Record.Exception(() => progress.Finish(StartupUpdateOutcome.Completed, 1));

        Assert.Null(report);
        Assert.Null(finish);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DomainDiscovery.Tests --filter StatusFileProgressTests`

Expected: FAIL to compile — `StatusFileProgress` does not exist.

- [ ] **Step 3: Write the sink**

Create `DomainDiscovery/Wikipedia/Dump/StatusFileProgress.cs`:

```csharp
namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// An <see cref="IStoreUpdateProgress"/> that publishes a <see cref="StoreUpdateStatus"/> snapshot
/// instead of drawing to a console — the sink a headless host uses.
/// </summary>
/// <remarks>
/// Percentage aggregation is delegated to the existing <see cref="OverallPercentProgress"/>; this
/// class only decides <i>when</i> to publish and <i>what</i> the snapshot says.
/// <para>
/// <b>Nothing here may throw.</b> Every entry point is reachable from <c>CountingStream</c>, and
/// <c>BZip2InputStream</c> treats any exception from the stream it reads as end-of-input — so a
/// throwing publish would silently truncate the apply rather than merely losing a status update.
/// </para>
/// </remarks>
public sealed class StatusFileProgress : IStoreUpdateProgress
{
    /// <summary>
    /// How often a snapshot is published while applying. Progress callbacks arrive per read of the
    /// download/apply stream, which is far too often to touch the disk on every one.
    /// </summary>
    public static readonly TimeSpan PublishInterval = TimeSpan.FromSeconds(1);

    private readonly IStoreUpdateProgress _percentages;
    private readonly Action<StoreUpdateStatus> _publish;
    private readonly Func<DateTimeOffset> _now;
    private readonly int _pendingDays;
    private readonly string? _version;

    private DateTimeOffset? _lastSuccessUtc;
    private DateTimeOffset _lastPublish = DateTimeOffset.MinValue;
    private int _currentDay;
    private int _percent;

    public StatusFileProgress(
        Action<StoreUpdateStatus> publish, int pendingDays,
        DateTimeOffset? lastSuccessUtc, string? serviceVersion, Func<DateTimeOffset>? now = null)
    {
        _publish = publish;
        _pendingDays = pendingDays;
        _lastSuccessUtc = lastSuccessUtc;
        _version = serviceVersion;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _percentages = new OverallPercentProgress(OnPercent, OnFinish);
    }

    public void BeginDay(int dayIndex, int dayCount) => Safely(
        () => _percentages.BeginDay(dayIndex, dayCount));

    public void BeginPhase(UpdatePhase phase, int day, long total, long initial) => Safely(() =>
    {
        _currentDay = day;
        _percentages.BeginPhase(phase, day, total, initial);
    });

    public void Report(long done) => Safely(() => _percentages.Report(done));

    public void EndPhase() => Safely(() => _percentages.EndPhase());

    public void Finish(StartupUpdateOutcome outcome, int daysApplied) => Safely(
        () => _percentages.Finish(outcome, daysApplied));

    private void OnPercent(int percent)
    {
        _percent = percent;

        var now = _now();
        if (now - _lastPublish < PublishInterval)
            return;

        _lastPublish = now;
        Publish(StoreUpdateState.Applying, error: null);
    }

    private void OnFinish(StartupUpdateOutcome outcome, int daysApplied)
    {
        // Only a completed run proves the store advanced. An interrupted or failed one keeps
        // whatever success was last recorded — the days that landed are still there.
        if (outcome is StartupUpdateOutcome.Completed)
            _lastSuccessUtc = _now();

        var failed = outcome is StartupUpdateOutcome.Failed;

        Publish(
            failed ? StoreUpdateState.Failed : StoreUpdateState.Idle,
            failed ? StoreUpdateText.Final(outcome, daysApplied) : null);
    }

    private void Publish(StoreUpdateState state, string? error) => _publish(new StoreUpdateStatus(
        state, _percent, _pendingDays, _currentDay == 0 ? null : _currentDay,
        _lastSuccessUtc, error, _version));

    private static void Safely(Action work)
    {
        try { work(); }
        catch { /* see the no-throw note on this class */ }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DomainDiscovery.Tests --filter StatusFileProgressTests`

Expected: PASS — 6 tests.

If `Reports_WithinTheInterval_PublishOnlyOnce` fails with 3 published instead of 2, the throttle is
comparing against the wrong timestamp — `_lastPublish` must be updated only when a publish actually
happens, not on every report.

- [ ] **Step 5: Verify the Release build is clean**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror`

Expected: Build succeeded, 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add DomainDiscovery/Wikipedia/Dump/StatusFileProgress.cs DomainDiscovery.Tests/Wikipedia/Dump/StatusFileProgressTests.cs
git commit -m "feat(store-update): add a status-publishing progress sink for headless hosts"
```

---

### Task 3: The update cycle

**Files:**
- Create: `DomainDiscovery/Wikipedia/Dump/IUpdateLog.cs`
- Create: `DomainDiscovery/Wikipedia/Dump/StoreUpdateCycle.cs`
- Test: `DomainDiscovery.Tests/Wikipedia/Dump/StoreUpdateCycleTests.cs`

**Interfaces:**
- Consumes: `StoreUpdateStatusFile`, `StoreUpdateStatus`, `StoreUpdateState` (Task 1); `StatusFileProgress` (Task 2); the existing `DumpStaleness`, `BackgroundStoreUpdate.Run`, `StartupDumpUpdate.ApplyDay`, `StartupUpdateResult`, `StartupUpdateOutcome`, `StoreUpdateText`.
- Produces:
  - `interface IUpdateLog { void Info(string message); void Error(string message, Exception? failure = null); }`
  - `sealed class StoreUpdateCycle` with:
    - `delegate DumpStaleness Check()`
    - `delegate StartupDumpUpdate.ApplyDay ApplyFactory(IStoreUpdateProgress progress)`
    - `StoreUpdateCycle(Check check, ApplyFactory apply, IUpdateLog log, StoreUpdateStatusFile status, string? serviceVersion = null, Func<DateTimeOffset>? now = null)`
    - `StartupUpdateResult RunOnce(CancellationToken cancellation)`
    - `Task RunLoop(TimeSpan interval, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellation)`

The `ApplyFactory` seam exists because the real `IncrementalUpdater` takes its progress sink at
construction, not per day. The cycle builds the sink, hands it to the factory, and gets back the
per-day delegate `BackgroundStoreUpdate.Run` expects.

- [ ] **Step 1: Write the failing tests**

Create `DomainDiscovery.Tests/Wikipedia/Dump/StoreUpdateCycleTests.cs`:

```csharp
using DomainDiscovery.Wikipedia.Dump;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

public sealed class StoreUpdateCycleTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "scg-cycle-" + Guid.NewGuid().ToString("N"));

    private readonly StoreUpdateStatusFile _status;

    public StoreUpdateCycleTests()
    {
        Directory.CreateDirectory(_directory);
        _status = new StoreUpdateStatusFile(Path.Combine(_directory, "update-status.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { /* best effort */ }
    }

    private sealed class RecordingLog : IUpdateLog
    {
        public List<string> Infos { get; } = [];
        public List<string> Errors { get; } = [];
        public void Info(string message) => Infos.Add(message);
        public void Error(string message, Exception? failure = null) => Errors.Add(message);
    }

    private static DumpStaleness Stale(params int[] days) => new(20260701, days, Answered: true);
    private static DumpStaleness Current => new(20260701, [], Answered: true);
    private static DumpStaleness Unanswered => new(20260701, [], Answered: false);

    private StoreUpdateCycle Cycle(
        DumpStaleness staleness, List<int> applied, RecordingLog log, Exception? applyThrows = null)
        => new(
            () => staleness,
            _ => (day, _) =>
            {
                if (applyThrows is not null) throw applyThrows;
                applied.Add(day);
            },
            log, _status, "1.0");

    [Fact]
    public void RunOnce_Stale_AppliesEveryPendingDayInOrder()
    {
        var applied = new List<int>();
        var log = new RecordingLog();

        var result = Cycle(Stale(20260702, 20260703), applied, log).RunOnce(CancellationToken.None);

        Assert.Equal([20260702, 20260703], applied);
        Assert.Equal(StartupUpdateOutcome.Completed, result.Outcome);
        Assert.Equal(2, result.DaysApplied);
        Assert.Empty(log.Errors);
    }

    [Fact]
    public void RunOnce_UpToDate_AppliesNothingAndRecordsIdle()
    {
        var applied = new List<int>();

        var result = Cycle(Current, applied, new RecordingLog()).RunOnce(CancellationToken.None);

        Assert.Empty(applied);
        Assert.Equal(StartupUpdateOutcome.NothingToDo, result.Outcome);
        Assert.Equal(StoreUpdateState.Idle, _status.Read().State);
    }

    [Fact]
    public void RunOnce_Unanswered_ClaimsNothing()
    {
        // A check Wikimedia never answered knows nothing, and must not read as a clean bill of health.
        var applied = new List<int>();

        var result = Cycle(Unanswered, applied, new RecordingLog()).RunOnce(CancellationToken.None);

        Assert.Empty(applied);
        Assert.Equal(StartupUpdateOutcome.NothingToDo, result.Outcome);
        Assert.Equal(StoreUpdateState.Unknown, _status.Read().State);
    }

    [Fact]
    public void RunOnce_ApplyThrows_ReportsFailedAndDoesNotEscape()
    {
        var log = new RecordingLog();

        var result = Cycle(Stale(20260702), [], log, new IOException("network gone"))
            .RunOnce(CancellationToken.None);

        Assert.Equal(StartupUpdateOutcome.Failed, result.Outcome);
        Assert.NotEmpty(log.Errors);
        Assert.Equal(StoreUpdateState.Failed, _status.Read().State);
    }

    [Fact]
    public void RunOnce_CheckThrows_ReportsFailedAndDoesNotEscape()
    {
        var log = new RecordingLog();
        var cycle = new StoreUpdateCycle(
            () => throw new InvalidOperationException("store is locked"),
            _ => (_, _) => { }, log, _status, "1.0");

        var result = cycle.RunOnce(CancellationToken.None);

        Assert.Equal(StartupUpdateOutcome.Failed, result.Outcome);
        Assert.NotEmpty(log.Errors);
    }

    [Fact]
    public void RunOnce_Cancelled_ReportsSkippedAndKeepsAppliedDays()
    {
        using var cancellation = new CancellationTokenSource();
        var applied = new List<int>();
        var cycle = new StoreUpdateCycle(
            () => Stale(20260702, 20260703, 20260704),
            _ => (day, token) =>
            {
                applied.Add(day);
                if (applied.Count == 2) cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            },
            new RecordingLog(), _status, "1.0");

        var result = cycle.RunOnce(cancellation.Token);

        Assert.Equal(StartupUpdateOutcome.Skipped, result.Outcome);
        Assert.Equal(1, result.DaysApplied);
    }

    [Fact]
    public async Task RunLoop_RunsACycleThenWaits_AndStopsOnCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var applied = new List<int>();
        var waits = new List<TimeSpan>();

        var cycle = new StoreUpdateCycle(
            () => Stale(20260702), _ => (day, _) => applied.Add(day),
            new RecordingLog(), _status, "1.0");

        await cycle.RunLoop(
            TimeSpan.FromHours(24),
            (interval, _) =>
            {
                waits.Add(interval);
                if (waits.Count == 3) cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellation.Token);

        Assert.Equal(3, applied.Count);
        Assert.Equal(3, waits.Count);
        Assert.All(waits, wait => Assert.Equal(TimeSpan.FromHours(24), wait));
    }

    [Fact]
    public async Task RunLoop_ACycleThatFails_StillSchedulesTheNextTick()
    {
        using var cancellation = new CancellationTokenSource();
        var waits = 0;

        var cycle = new StoreUpdateCycle(
            () => throw new IOException("offline"), _ => (_, _) => { },
            new RecordingLog(), _status, "1.0");

        await cycle.RunLoop(
            TimeSpan.FromHours(24),
            (_, _) =>
            {
                if (++waits == 2) cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellation.Token);

        Assert.Equal(2, waits);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DomainDiscovery.Tests --filter StoreUpdateCycleTests`

Expected: FAIL to compile — `IUpdateLog` and `StoreUpdateCycle` do not exist.

- [ ] **Step 3: Write the log abstraction**

Create `DomainDiscovery/Wikipedia/Dump/IUpdateLog.cs`:

```csharp
namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// Where the updater records what it did. Deliberately tiny: the updater has milestones to report,
/// not a logging framework to configure.
/// </summary>
/// <remarks>
/// Implementations <b>must not throw</b>. A service that cannot write to its log must still update
/// the store — being unable to say what you are doing is not a reason to stop doing it.
/// </remarks>
public interface IUpdateLog
{
    void Info(string message);

    void Error(string message, Exception? failure = null);
}
```

- [ ] **Step 4: Write the cycle**

Create `DomainDiscovery/Wikipedia/Dump/StoreUpdateCycle.cs`:

```csharp
namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// One check-and-apply pass over the store, and the loop that repeats it. This is what a headless
/// host (the Windows Service) runs; it owns no console, no timer, and no service plumbing.
/// </summary>
/// <remarks>
/// <b>Nothing here may throw into the host.</b> Being unable to update is never fatal: the next
/// tick tries again and the store is merely as stale as it already was.
/// <para>
/// Resumability comes from <see cref="IncrementalUpdater"/>, which commits a day at a time and moves
/// the store's dump date only once a day has FULLY landed. So a service stop, a shutdown, or a crash
/// mid-day costs the day in progress and nothing else.
/// </para>
/// </remarks>
public sealed class StoreUpdateCycle
{
    /// <summary>Asks how far behind the store is.</summary>
    public delegate DumpStaleness Check();

    /// <summary>
    /// Builds the per-day apply for a given progress sink. This is a factory rather than a plain
    /// delegate because <see cref="IncrementalUpdater"/> takes its sink at construction, not per day.
    /// </summary>
    public delegate StartupDumpUpdate.ApplyDay ApplyFactory(IStoreUpdateProgress progress);

    private readonly Check _check;
    private readonly ApplyFactory _apply;
    private readonly IUpdateLog _log;
    private readonly StoreUpdateStatusFile _status;
    private readonly string? _version;
    private readonly Func<DateTimeOffset> _now;

    public StoreUpdateCycle(
        Check check, ApplyFactory apply, IUpdateLog log, StoreUpdateStatusFile status,
        string? serviceVersion = null, Func<DateTimeOffset>? now = null)
    {
        _check = check;
        _apply = apply;
        _log = log;
        _status = status;
        _version = serviceVersion;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public StartupUpdateResult RunOnce(CancellationToken cancellation)
    {
        var lastSuccess = _status.Read().LastSuccessUtc;

        try
        {
            _status.Write(new StoreUpdateStatus(
                StoreUpdateState.Checking, 0, 0, null, lastSuccess, null, _version));

            var staleness = _check();

            if (!staleness.IsStale)
                return NothingToDo(staleness, lastSuccess);

            _log.Info(
                $"{staleness.PendingDays.Count} day(s) pending, about " +
                $"{staleness.ApproximateGigabytes:F1} GB.");

            var progress = new StatusFileProgress(
                _status.Write, staleness.PendingDays.Count, lastSuccess, _version, _now);

            var result = new BackgroundStoreUpdate().Run(
                staleness, progress, _apply(progress), cancellation);

            _log.Info(StoreUpdateText.Final(result.Outcome, result.DaysApplied));

            if (result.Outcome is StartupUpdateOutcome.Failed)
                _log.Error(result.Failure ?? "The update failed.");

            return result;
        }
        catch (Exception failure)
        {
            // The check itself can break (a locked store, a broken HttpClient). Record it and carry
            // on; the next tick tries again.
            _log.Error("The update cycle broke.", failure);

            _status.Write(new StoreUpdateStatus(
                StoreUpdateState.Failed, 0, 0, null, lastSuccess, failure.Message, _version));

            return new StartupUpdateResult(0, StartupUpdateOutcome.Failed, failure.Message);
        }
    }

    /// <summary>
    /// Runs a cycle, waits, repeats — until cancelled. <paramref name="delay"/> is injected so the
    /// loop can be tested without waiting a day.
    /// </summary>
    public async Task RunLoop(
        TimeSpan interval, Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            RunOnce(cancellation);

            try
            {
                await delay(interval, cancellation);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private StartupUpdateResult NothingToDo(DumpStaleness staleness, DateTimeOffset? lastSuccess)
    {
        // IsCurrent, not "no pending days": a check Wikimedia never answered knows nothing, and must
        // not be recorded as a clean bill of health.
        if (staleness.IsCurrent)
        {
            _log.Info("The store is up to date.");
            _status.Write(new StoreUpdateStatus(
                StoreUpdateState.Idle, 100, 0, null, lastSuccess, null, _version));
        }
        else
        {
            _log.Info("Wikimedia did not answer. Nothing claimed, nothing downloaded.");
            _status.Write(new StoreUpdateStatus(
                StoreUpdateState.Unknown, 0, 0, null, lastSuccess, null, _version));
        }

        return new StartupUpdateResult(0, StartupUpdateOutcome.NothingToDo);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test DomainDiscovery.Tests --filter StoreUpdateCycleTests`

Expected: PASS — 8 tests.

- [ ] **Step 6: Run the whole DomainDiscovery suite and the Release build**

Run:

```bash
dotnet test DomainDiscovery.Tests
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror
```

Expected: all tests PASS (no regression in `BackgroundStoreUpdateTests`, `DumpFreshnessTests`,
`IncrementalUpdaterTests`); build succeeds with 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add DomainDiscovery/Wikipedia/Dump/IUpdateLog.cs DomainDiscovery/Wikipedia/Dump/StoreUpdateCycle.cs DomainDiscovery.Tests/Wikipedia/Dump/StoreUpdateCycleTests.cs
git commit -m "feat(store-update): add the headless check-apply-wait update cycle"
```

---

### Task 4: The Windows Service host

**Files:**
- Create: `WikipediaUpdateService/WikipediaUpdateService.csproj`
- Create: `WikipediaUpdateService/ServiceOptions.cs`
- Create: `WikipediaUpdateService/EventLogUpdateLog.cs`
- Create: `WikipediaUpdateService/UpdateWorker.cs`
- Create: `WikipediaUpdateService/Program.cs`
- Create: `WikipediaUpdateService/appsettings.json`
- Modify: `SmartCodeGenerator/SmartCodeGenerator.slnx`

**Interfaces:**
- Consumes: `StoreUpdateCycle`, `IUpdateLog` (Task 3); `StoreUpdateStatusFile` (Task 1); the existing `WikipediaStore`, `DumpFreshness`, `IncrementalUpdater`, `DomainDiscoveryPipeline.DefaultDumpDatabasePath`.
- Produces: the executable `WikipediaUpdateService.exe`, and `ServiceOptions` with properties `StorePath`, `WorkingDirectory`, `IntervalHours`, `StatusFilePath` plus `void ApplyDefaults()`.

**Why `net10.0-windows`:** `EventLog` is Windows-only, and CI builds with `-warnaserror`, so the
platform-compatibility analyzer (CA1416) would fail the build against the repo-wide `net10.0`.
Targeting `net10.0-windows` states the constraint instead of suppressing it.
`EnableWindowsTargeting` lets the project still build on a non-Windows CI agent.

- [ ] **Step 1: Create the project file**

Create `WikipediaUpdateService/WikipediaUpdateService.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Worker">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <!-- EventLog is Windows-only; state that rather than suppress CA1416 under -warnaserror. -->
    <TargetFramework>net10.0-windows</TargetFramework>
    <!-- Lets a non-Windows CI agent still build this project. -->
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <UserSecretsId>smartcodegen-wikipedia-updater</UserSecretsId>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="10.0.0" />
    <PackageReference Include="System.Diagnostics.EventLog" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\DomainDiscovery\DomainDiscovery.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

If `10.0.0` is not the resolvable version for the `Microsoft.Extensions.*` packages, run
`dotnet add WikipediaUpdateService package Microsoft.Extensions.Hosting.WindowsServices` and let
NuGet pick, then match the others to the version it chose.

- [ ] **Step 2: Write the options**

Create `WikipediaUpdateService/ServiceOptions.cs`:

```csharp
using DomainDiscovery;
using DomainDiscovery.Wikipedia.Dump;

namespace WikipediaUpdateService;

/// <summary>
/// What the service reads from <c>appsettings.json</c>. Every value has a working default, so an
/// installation that never edits the file still updates the store in its usual place.
/// </summary>
public sealed class ServiceOptions
{
    public const string SectionName = "Updater";

    public string? StorePath { get; set; }

    public string? WorkingDirectory { get; set; }

    public double IntervalHours { get; set; } = 24;

    public string? StatusFilePath { get; set; }

    /// <summary>Fills anything left unset, deriving the paths from the store's own directory.</summary>
    public void ApplyDefaults()
    {
        StorePath = string.IsNullOrWhiteSpace(StorePath)
            ? DomainDiscoveryPipeline.DefaultDumpDatabasePath
            : StorePath;

        var storeDirectory = Path.GetDirectoryName(StorePath) ?? ".";

        WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectory)
            ? Path.Combine(storeDirectory, "incr")
            : WorkingDirectory;

        StatusFilePath = string.IsNullOrWhiteSpace(StatusFilePath)
            ? StoreUpdateStatusFile.BesideStore(StorePath)
            : StatusFilePath;

        if (IntervalHours <= 0)
            IntervalHours = 24;
    }
}
```

- [ ] **Step 3: Write the Event Log sink**

Create `WikipediaUpdateService/EventLogUpdateLog.cs`:

```csharp
using System.Diagnostics;
using DomainDiscovery.Wikipedia.Dump;

namespace WikipediaUpdateService;

/// <summary>
/// Writes milestones to the Windows Event Log — the standard place to look when a service will not
/// start, and the only record that survives the status file being overwritten.
/// </summary>
/// <remarks>
/// The source is registered by the install script, because creating one needs elevation and the
/// service runs long after that. If the source is missing, every write here fails and is swallowed:
/// a service that cannot log must still update the store.
/// </remarks>
public sealed class EventLogUpdateLog : IUpdateLog
{
    public const string SourceName = "SmartCodeGen Wikipedia Updater";

    public void Info(string message) => Write(message, EventLogEntryType.Information);

    public void Error(string message, Exception? failure = null) => Write(
        failure is null ? message : $"{message}{Environment.NewLine}{failure}",
        EventLogEntryType.Error);

    private static void Write(string message, EventLogEntryType type)
    {
        try { EventLog.WriteEntry(SourceName, message, type); }
        catch { /* see the remarks: logging is never a reason to stop updating */ }
    }
}
```

- [ ] **Step 4: Write the worker**

Create `WikipediaUpdateService/UpdateWorker.cs`:

```csharp
using DomainDiscovery.Wikipedia.Dump;
using Microsoft.Extensions.Options;

namespace WikipediaUpdateService;

/// <summary>
/// The service's only moving part: it builds a <see cref="StoreUpdateCycle"/> over the real store
/// and runs it until the host stops. All the decisions live in the cycle, which is unit-tested;
/// this class is wiring.
/// </summary>
public sealed class UpdateWorker : BackgroundService
{
    private readonly ServiceOptions _options;
    private readonly IUpdateLog _log;

    public UpdateWorker(IOptions<ServiceOptions> options, IUpdateLog log)
    {
        _options = options.Value;
        _options.ApplyDefaults();
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var storePath = _options.StorePath!;
        var workingDirectory = _options.WorkingDirectory!;

        _log.Info($"Started. Store: {storePath}. Interval: {_options.IntervalHours}h.");

        if (!File.Exists(storePath))
        {
            // A missing store is a job for WikipediaDumpImporter, not for this service. Say so once
            // and keep ticking — the store may appear while the service is running.
            _log.Error($"No store at {storePath}. Run WikipediaDumpImporter to build it.");
        }

        var status = new StoreUpdateStatusFile(_options.StatusFilePath!);
        var version = typeof(UpdateWorker).Assembly.GetName().Version?.ToString();

        var cycle = new StoreUpdateCycle(
            check: () => new DumpFreshness().Check(new WikipediaStore(storePath)),
            apply: progress => (day, token) => new IncrementalUpdater(
                    new WikipediaStore(storePath), say: _ => { }, progress: progress)
                .UpdateDay(day, workingDirectory, token),
            log: _log,
            status: status,
            serviceVersion: version);

        await cycle.RunLoop(
            TimeSpan.FromHours(_options.IntervalHours),
            Task.Delay,
            stoppingToken);

        _log.Info("Stopped.");
    }
}
```

Note `say: _ => { }` — `IncrementalUpdater` defaults its `say` to `Console.WriteLine`, and a service
has no console. Progress reaches the outside world through the status file instead.

- [ ] **Step 5: Write the host**

Create `WikipediaUpdateService/Program.cs`:

```csharp
using DomainDiscovery.Wikipedia.Dump;
using WikipediaUpdateService;

var builder = Host.CreateApplicationBuilder(args);

// Lets the same executable run under services.msc and, for diagnosis, straight from a console.
builder.Services.AddWindowsService(options =>
    options.ServiceName = "SmartCodeGenWikipediaUpdater");

builder.Services.Configure<ServiceOptions>(
    builder.Configuration.GetSection(ServiceOptions.SectionName));

builder.Services.AddSingleton<IUpdateLog, EventLogUpdateLog>();
builder.Services.AddHostedService<UpdateWorker>();

await builder.Build().RunAsync();
```

- [ ] **Step 6: Write the settings file**

Create `WikipediaUpdateService/appsettings.json`:

```json
{
  "Updater": {
    "StorePath": "D:\\SmartCodeGenerator-Repository\\Dump\\enwiki-full.db",
    "WorkingDirectory": null,
    "IntervalHours": 24,
    "StatusFilePath": null
  }
}
```

`null` means "derive it from `StorePath`" — see `ServiceOptions.ApplyDefaults`.

- [ ] **Step 7: Add the project to the solution**

In `SmartCodeGenerator/SmartCodeGenerator.slnx`, add the project inside `<Solution>`, keeping the
existing alphabetical ordering — after the `WikipediaDumpImporter` line:

```xml
  <Project Path="../WikipediaUpdateService/WikipediaUpdateService.csproj" />
```

- [ ] **Step 8: Build and run it as a console app**

Run:

```bash
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror
dotnet run --project WikipediaUpdateService -c Release
```

Expected: the build succeeds with 0 warnings. The run starts and stays running. Because the Event
Log source is not registered yet (Task 6 does that), no Event Log entries appear — that is the
designed silent degradation, not a failure.

Confirm the status file appears and moves through states:

```bash
cat Dump/update-status.json
```

Expected: valid JSON whose `state` is one of `Checking`, `Applying`, `Idle`, `Unknown`, or `Failed`.
If the machine is offline, `Unknown` is correct and the service must still be running.

Stop it with Ctrl-C and confirm the process exits promptly rather than hanging.

- [ ] **Step 9: Commit**

```bash
git add WikipediaUpdateService SmartCodeGenerator/SmartCodeGenerator.slnx
git commit -m "feat(service): add the WikipediaUpdateService worker host"
```

---

### Task 5: Make the CLI a read-only reporter

**Files:**
- Create: `CodeGenerator/App/StoreStatusReport.cs`
- Create: `CodeGenerator/App/UpdaterServicePresence.cs`
- Modify: `CodeGenerator/CodeGenerator.csproj` (add `System.ServiceProcess.ServiceController`)
- Modify: `CodeGenerator/App/CodeGeneratorStarter.cs` (`StartWikipediaStoreUpdate` at `:250-292`; the `exit` branch at `:134-140`; the `_storeUpdateCts` / `_storeUpdateTask` fields)
- Modify: `DomainDiscovery/Wikipedia/Dump/BackgroundStoreUpdate.cs` (delete `Start`, `BuildProgress`, `SetTitle`, `WriteLineSafe`)
- Test: `CodeGenerator.Tests/App/StoreStatusReportTests.cs`

**Interfaces:**
- Consumes: `StoreUpdateStatus`, `StoreUpdateState`, `StoreUpdateStatusFile` (Task 1).
- Produces:
  - `enum UpdaterServiceState { NotInstalled, Running, Stopped, Unknown }`
  - `static class UpdaterServicePresence` with `static UpdaterServiceState Detect()`
  - `static class StoreStatusReport` with
    `static IReadOnlyList<string> Lines(int? storeDate, StoreUpdateStatus status, UpdaterServiceState service, string installHintCommand)`

**Why `ServiceController` rather than inferring from the status file:** the file's age cannot tell
"not installed" from "installed and idle", and the prominent install hint depends on that
distinction being definite.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/App/StoreStatusReportTests.cs`:

```csharp
using CodeGenerator.App;
using DomainDiscovery.Wikipedia.Dump;
using Xunit;

namespace CodeGenerator.Tests.App;

public sealed class StoreStatusReportTests
{
    private const string Hint = @".\tools\install-updater-service.ps1";

    private static string Joined(IReadOnlyList<string> lines) => string.Join("\n", lines);

    private static StoreUpdateStatus Idle => new(
        StoreUpdateState.Idle, 100, 0, null,
        new DateTimeOffset(2026, 7, 24, 4, 0, 0, TimeSpan.Zero), null, "1.0");

    [Fact]
    public void Running_And_UpToDate_ReportsTheDateAndTheService()
    {
        var text = Joined(StoreStatusReport.Lines(
            20260724, Idle, UpdaterServiceState.Running, Hint));

        Assert.Contains("20260724", text);
        Assert.Contains("updater service", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Hint, text);
    }

    [Fact]
    public void Applying_ShowsThePercentage()
    {
        var applying = new StoreUpdateStatus(
            StoreUpdateState.Applying, 43, 6, 20260718, null, null, "1.0");

        var text = Joined(StoreStatusReport.Lines(
            20260717, applying, UpdaterServiceState.Running, Hint));

        Assert.Contains("43%", text);
    }

    [Fact]
    public void NotInstalled_ShowsTheInstallHintProminently()
    {
        // Sole ownership means a machine without the service stops updating entirely. This hint is
        // the only thing covering that gap, so it must be loud.
        var lines = StoreStatusReport.Lines(
            20260710, StoreUpdateStatus.Unknown, UpdaterServiceState.NotInstalled, Hint);

        var text = Joined(lines);
        Assert.Contains(Hint, text);
        Assert.Contains("NOT INSTALLED", text);
        Assert.True(lines.Count >= 3, "the hint needs room to be noticed, not one quiet line");
    }

    [Fact]
    public void Stopped_SaysSoAndStillOffersTheHint()
    {
        var text = Joined(StoreStatusReport.Lines(
            20260710, StoreUpdateStatus.Unknown, UpdaterServiceState.Stopped, Hint));

        Assert.Contains("stopped", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Failed_SurfacesTheRecordedError()
    {
        var failed = new StoreUpdateStatus(
            StoreUpdateState.Failed, 12, 4, 20260718, null, "the server hung up", "1.0");

        var text = Joined(StoreStatusReport.Lines(
            20260717, failed, UpdaterServiceState.Running, Hint));

        Assert.Contains("the server hung up", text);
    }

    [Fact]
    public void AnUnknownStoreDate_DoesNotPrintANullDate()
    {
        var text = Joined(StoreStatusReport.Lines(
            null, StoreUpdateStatus.Unknown, UpdaterServiceState.Running, Hint));

        Assert.DoesNotContain("null", text, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests --filter StoreStatusReportTests`

Expected: FAIL to compile — `StoreStatusReport` and `UpdaterServiceState` do not exist.

- [ ] **Step 3: Add the ServiceController package**

In `CodeGenerator/CodeGenerator.csproj`, add to the existing `<ItemGroup>` of package references
(or create one):

```xml
    <PackageReference Include="System.ServiceProcess.ServiceController" Version="10.0.0" />
```

If that version does not resolve, run
`dotnet add CodeGenerator package System.ServiceProcess.ServiceController` and use what NuGet picks.

- [ ] **Step 4: Write the service probe**

Create `CodeGenerator/App/UpdaterServicePresence.cs`:

```csharp
using System.Runtime.Versioning;
using System.ServiceProcess;

namespace CodeGenerator.App;

/// <summary>Whether the updater service is there, and whether it is running.</summary>
public enum UpdaterServiceState
{
    /// <summary>No such service. Nothing is updating the store — the install hint is warranted.</summary>
    NotInstalled,

    Running,

    Stopped,

    /// <summary>Not Windows, or the query was refused. Claim nothing.</summary>
    Unknown,
}

/// <summary>
/// Asks Windows whether the updater service exists. Isolated in its own file because it is the only
/// Windows-only API the CLI touches.
/// </summary>
public static class UpdaterServicePresence
{
    public const string ServiceName = "SmartCodeGenWikipediaUpdater";

    public static UpdaterServiceState Detect()
    {
        if (!OperatingSystem.IsWindows())
            return UpdaterServiceState.Unknown;

        return DetectOnWindows();
    }

    [SupportedOSPlatform("windows")]
    private static UpdaterServiceState DetectOnWindows()
    {
        try
        {
            using var controller = new ServiceController(ServiceName);

            return controller.Status == ServiceControllerStatus.Running
                ? UpdaterServiceState.Running
                : UpdaterServiceState.Stopped;
        }
        catch (InvalidOperationException)
        {
            // ServiceController throws this — not a "not found" exception — when the name is absent.
            return UpdaterServiceState.NotInstalled;
        }
        catch
        {
            // Refused, or a machine that will not answer. Claim nothing.
            return UpdaterServiceState.Unknown;
        }
    }
}
```

- [ ] **Step 5: Write the report**

Create `CodeGenerator/App/StoreStatusReport.cs`:

```csharp
using DomainDiscovery.Wikipedia.Dump;

namespace CodeGenerator.App;

/// <summary>
/// Turns the store's date, the updater's published status, and the service's presence into the
/// lines the CLI prints at startup. Pure — no console, no clock, no file system — so it is tested
/// directly.
/// </summary>
public static class StoreStatusReport
{
    public static IReadOnlyList<string> Lines(
        int? storeDate, StoreUpdateStatus status, UpdaterServiceState service,
        string installHintCommand)
    {
        var lines = new List<string>
        {
            storeDate is { } date
                ? $"Wikipedia store: dump {date}."
                : "Wikipedia store: present, but it cannot say which dump it was built from.",
        };

        lines.Add(ServiceLine(service, status));

        if (status.State is StoreUpdateState.Failed && !string.IsNullOrWhiteSpace(status.LastError))
            lines.Add($"  Last update error: {status.LastError}");

        if (service is UpdaterServiceState.NotInstalled or UpdaterServiceState.Stopped)
        {
            // The service is the ONLY thing that updates the store now — the CLI no longer does it.
            // So this gap is real, and the hint has to be noticed rather than skimmed past.
            lines.Add(string.Empty);
            lines.Add("  Nothing is updating the store. To install and start the updater service,");
            lines.Add("  run this from an elevated PowerShell prompt:");
            lines.Add(string.Empty);
            lines.Add($"      {installHintCommand}");
            lines.Add(string.Empty);
        }

        return lines;
    }

    private static string ServiceLine(UpdaterServiceState service, StoreUpdateStatus status)
        => service switch
        {
            UpdaterServiceState.NotInstalled => "  Updater service: NOT INSTALLED.",
            UpdaterServiceState.Stopped => "  Updater service: installed, but stopped.",
            UpdaterServiceState.Unknown => "  Updater service: cannot tell.",
            _ => $"  Updater service: running — {Activity(status)}",
        };

    private static string Activity(StoreUpdateStatus status) => status.State switch
    {
        StoreUpdateState.Applying => $"updating {status.Percent}% ({status.PendingDays} day(s) pending).",
        StoreUpdateState.Checking => "checking for new increments.",
        StoreUpdateState.Idle => "idle.",
        StoreUpdateState.Failed => "the last update failed.",
        _ => "no status published yet.",
    };
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests --filter StoreStatusReportTests`

Expected: PASS — 6 tests.

- [ ] **Step 7: Rewire the CLI startup**

In `CodeGenerator/App/CodeGeneratorStarter.cs`, replace the whole `StartWikipediaStoreUpdate` method
(and its XML doc comment) at lines 250-292 with:

```csharp
    /// <summary>
    /// Reports a missing store loudly, or reports what the store holds and what the updater service
    /// is doing. This method no longer <i>performs</i> any update: the Windows Service
    /// <c>SmartCodeGenWikipediaUpdater</c> is the sole owner of that work, so the CLI never competes
    /// with it for the store and never spends 780 MB a day of the user's connection unasked.
    /// </summary>
    private void ReportWikipediaStoreStatus()
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

        int? storeDate;
        try
        {
            storeDate = new WikipediaStore(databasePath).GetDumpDate();
        }
        catch
        {
            // A store that cannot answer is still usable for reads; do not let a status line stop the app.
            storeDate = null;
        }

        var status = new StoreUpdateStatusFile(
            StoreUpdateStatusFile.BesideStore(databasePath)).Read();

        foreach (var line in StoreStatusReport.Lines(
            storeDate, status, UpdaterServicePresence.Detect(),
            @".\tools\install-updater-service.ps1"))
        {
            Console.WriteLine(line);
        }

        Console.WriteLine();
    }
```

Update the call site in `Run()` to `ReportWikipediaStoreStatus();`.

Add the required usings at the top of the file if they are not already present:

```csharp
using DomainDiscovery.Wikipedia.Dump;
```

- [ ] **Step 8: Remove the in-process update machinery**

In `CodeGenerator/App/CodeGeneratorStarter.cs`:

Delete the `_storeUpdateCts` and `_storeUpdateTask` fields.

Replace the `exit` branch (lines 134-140) with:

```csharp
            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;
```

The comment about abandoning a half-done background update goes with it — there is no background
update in this process any more.

In `DomainDiscovery/Wikipedia/Dump/BackgroundStoreUpdate.cs`, delete the `Start` method and the
private `BuildProgress`, `SetTitle`, and `WriteLineSafe` helpers, leaving only `Run` and the class
doc comment. Update the class summary to:

```csharp
/// <summary>
/// Drives the daily-increment apply through an <see cref="IStoreUpdateProgress"/>, so the caller
/// decides how progress is shown and whether it blocks anything. Whatever happens — offline,
/// interrupted, failed — the caller keeps running.
/// </summary>
```

- [ ] **Step 9: Verify the build, the tests, and the running CLI**

Run:

```bash
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx
dotnet build CodeGenerator -c Debug
dotnet run --project CodeGenerator
```

Expected: 0 warnings, all three test projects green. `dotnet test` leaves the CLI's `bin` stale,
which is why the explicit `dotnet build CodeGenerator` precedes the run.

At the prompt, confirm:

1. The prompt appears immediately — no network wait.
2. The store's dump date is printed.
3. The updater service line reflects reality. Since Task 6 has not installed it yet, expect
   `Updater service: NOT INSTALLED.` followed by the install hint block.
4. `exit` returns to the shell at once.

- [ ] **Step 10: Commit**

```bash
git add CodeGenerator/App/StoreStatusReport.cs CodeGenerator/App/UpdaterServicePresence.cs CodeGenerator/App/CodeGeneratorStarter.cs CodeGenerator/CodeGenerator.csproj DomainDiscovery/Wikipedia/Dump/BackgroundStoreUpdate.cs CodeGenerator.Tests/App/StoreStatusReportTests.cs
git commit -m "refactor(cli): stop updating the store in-process; report service status instead"
```

---

### Task 6: Install, uninstall, and end-to-end verification

**Files:**
- Create: `tools/install-updater-service.ps1`
- Create: `tools/uninstall-updater-service.ps1`
- Modify: `DomainDiscovery/README.md`

**Interfaces:**
- Consumes: the published `WikipediaUpdateService.exe` (Task 4); `EventLogUpdateLog.SourceName`; `UpdaterServicePresence.ServiceName`.
- Produces: an installed, running Windows Service named `SmartCodeGenWikipediaUpdater`.

- [ ] **Step 1: Write the install script**

Create `tools/install-updater-service.ps1`:

```powershell
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Publishes and installs the SmartCodeGen Wikipedia updater as a Windows Service.

.DESCRIPTION
    The service is the SOLE owner of the daily Wikipedia increment update — the CodeGenerator CLI
    no longer does it. Without this service installed, the store stops advancing entirely.

    Runs as LocalSystem. That is correct only while the store is on LOCAL disk: LocalSystem
    authenticates off-box as the machine account, so a store moved to a network share would need a
    domain account with rights to that share instead.

    Start type is delayed-auto rather than auto: this still runs "when the computer starts", but it
    keeps an ~800 MB download from competing with the boot storm.
#>
[CmdletBinding()]
param(
    [string] $ServiceName = 'SmartCodeGenWikipediaUpdater',
    [string] $EventSource = 'SmartCodeGen Wikipedia Updater',
    [string] $PublishDirectory = (Join-Path $PSScriptRoot '..\artifacts\updater-service')
)

$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot '..\WikipediaUpdateService\WikipediaUpdateService.csproj'
$publishPath = [System.IO.Path]::GetFullPath($PublishDirectory)

Write-Host "Publishing $projectPath -> $publishPath"
dotnet publish $projectPath -c Release -o $publishPath
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$exePath = Join-Path $publishPath 'WikipediaUpdateService.exe'
if (-not (Test-Path $exePath)) { throw "Published executable not found at $exePath." }

if (-not [System.Diagnostics.EventLog]::SourceExists($EventSource)) {
    Write-Host "Registering Event Log source '$EventSource'"
    New-EventLog -LogName Application -Source $EventSource
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service '$ServiceName' already exists; stopping and removing it first."
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

Write-Host "Creating service '$ServiceName'"
sc.exe create $ServiceName binPath= "`"$exePath`"" start= delayed-auto obj= LocalSystem | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed with exit code $LASTEXITCODE." }

sc.exe description $ServiceName "Keeps the SmartCodeGen Wikipedia store up to date with daily increments." | Out-Null

# Restart on failure so a crash does not silently end updating: 1 min, 5 min, then 15 min,
# with the failure count resetting after a day.
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/300000/restart/900000 | Out-Null

Write-Host "Starting service"
Start-Service -Name $ServiceName

Get-Service -Name $ServiceName | Format-List Name, Status, StartType
Write-Host "Done. Progress is published to the store's update-status.json; milestones go to the Application event log."
```

- [ ] **Step 2: Write the uninstall script**

Create `tools/uninstall-updater-service.ps1`:

```powershell
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Stops and removes the SmartCodeGen Wikipedia updater service.

.DESCRIPTION
    Removing the service stops the store advancing — nothing else updates it. The store itself and
    its status file are left alone.
#>
[CmdletBinding()]
param(
    [string] $ServiceName = 'SmartCodeGenWikipediaUpdater',
    [string] $EventSource = 'SmartCodeGen Wikipedia Updater',
    [switch] $KeepEventSource
)

$ErrorActionPreference = 'Stop'

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Service '$ServiceName' is not installed. Nothing to do."
}
else {
    if ($existing.Status -ne 'Stopped') {
        Write-Host "Stopping '$ServiceName'"
        Stop-Service -Name $ServiceName -Force
    }

    Write-Host "Deleting '$ServiceName'"
    sc.exe delete $ServiceName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sc.exe delete failed with exit code $LASTEXITCODE." }
}

if (-not $KeepEventSource -and [System.Diagnostics.EventLog]::SourceExists($EventSource)) {
    Write-Host "Removing Event Log source '$EventSource'"
    Remove-EventLog -Source $EventSource
}

Write-Host "Done. The store and its update-status.json were left in place."
```

- [ ] **Step 3: Install and verify end to end**

From an **elevated** PowerShell prompt at the repository root:

```powershell
.\tools\install-updater-service.ps1
```

Confirm each of:

1. `Get-Service SmartCodeGenWikipediaUpdater` reports `Running`, and
   `(Get-Service SmartCodeGenWikipediaUpdater).StartType` reports `Automatic` (delayed start is
   reported as `Automatic`; confirm the delayed flag with
   `sc.exe qc SmartCodeGenWikipediaUpdater`, which shows `DELAYED`).
2. `Get-EventLog -LogName Application -Source 'SmartCodeGen Wikipedia Updater' -Newest 5` shows a
   `Started.` entry naming the store path and the interval.
3. `Get-Content Dump\update-status.json` shows valid JSON. Offline, `state` is `Unknown` and that is
   correct — an unanswered check claims nothing.
4. Rebuild and run the CLI (`dotnet build CodeGenerator -c Debug; dotnet run --project CodeGenerator`).
   The startup line now reports `Updater service: running — …` and **no** install hint appears.
5. `Restart-Service SmartCodeGenWikipediaUpdater` mid-update, then confirm from the Event Log that
   the next start resumes rather than restarting from the store's original date. The store's dump
   date must not have gone backwards: compare `GetDumpDate` before and after.
6. Run `.\tools\uninstall-updater-service.ps1`, then start the CLI again and confirm the
   `NOT INSTALLED` block reappears with the install command. Reinstall afterwards.

- [ ] **Step 4: Document it**

In `DomainDiscovery/README.md`, add a section after the existing store documentation:

```markdown
## Keeping the store up to date

The daily Wikipedia increments are applied by a Windows Service,
`SmartCodeGenWikipediaUpdater`, **not** by the CodeGenerator CLI. Install it once, from an
elevated PowerShell prompt:

    .\tools\install-updater-service.ps1

It checks at every machine start and then every 24 hours. Progress is published to
`update-status.json` beside the store; milestones and failures go to the Application event log
under the source `SmartCodeGen Wikipedia Updater`. The CLI reports both at startup.

Without this service installed, nothing updates the store — the CLI will say so and repeat the
command above.

To remove it: `.\tools\uninstall-updater-service.ps1`.
```

- [ ] **Step 5: Final full verification**

Run:

```bash
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx
```

Expected: 0 warnings; all three test projects green.

- [ ] **Step 6: Commit**

```bash
git add tools/install-updater-service.ps1 tools/uninstall-updater-service.ps1 DomainDiscovery/README.md
git commit -m "feat(service): add install/uninstall scripts and document the updater service"
```

---

## Self-Review

**Spec coverage.** New Worker Service project reusing `BackgroundStoreUpdate.Run` — Task 4, built on
Task 3's cycle. Worker loop with boot check plus 24h interval — Task 3 (`RunLoop`, tested) and
Task 4 (`Task.Delay` and delayed-auto start). Resumability and `sc.exe failure` restart actions —
Task 3's doc comment and Task 6 Step 1, verified at Task 6 Step 3.5. Deleting
`BackgroundStoreUpdate.Start`, `BuildProgress`, `SetTitle`, `WriteLineSafe` — Task 5 Step 8. Status
file: atomic write, ~1s throttle, non-throwing sink, tolerant reader — Tasks 1 and 2, each with
tests for the failure modes. Event Log source registered by the installer with silent runtime
degradation — Task 4 Step 3 and Task 6 Step 1. Configuration keys and their defaults — Task 4
Steps 2 and 6. Install script steps, `LocalSystem`, and the network-share comment — Task 6 Step 1.
CLI changes: `File.Exists` warning preserved verbatim, `_storeUpdateCts`/`_storeUpdateTask` and the
`exit` cancellation removed, dump date plus service state reported, prominent install hint — Task 5
Steps 7-8, with the hint's prominence asserted in a test. Concurrency: no `WikipediaStore` change
appears in any task, and the "no native `busy_timeout`" rule is in Global Constraints. Error-handling
table rows are covered by Task 3's tests (offline/unanswered, apply failure, check failure,
cancellation), Task 1's (unwritable status), Task 4's (missing store), and Task 4 Step 3 (missing
event source). CI and rebuild-before-verifying notes — Global Constraints, and repeated in the
verification steps that need them.

**Deviation from the spec, deliberate.** The spec's title-bar poller — a CLI thread polling the
status file to keep a live percentage in `Console.Title` — is **not** implemented. The startup line
reports the percentage at the moment the CLI starts (Task 5), which delivers the visibility the spec
asked for without adding a background thread to a process that no longer does background work. If
the live-updating title is wanted, it is a small follow-up: poll `StoreUpdateStatusFile.Read()` on a
timer and set `Console.Title` from `StoreUpdateText.Updating(status.Percent)`, never writing to the
scrollback.

**Placeholder scan.** No TBDs and no "add appropriate error handling". Two steps say "if that NuGet
version does not resolve, let `dotnet add package` pick" — that is a concrete fallback command, not a
placeholder.

**Type consistency.** `StoreUpdateStatus`'s seven positional members are constructed identically in
Tasks 1, 2, 3, and 5. `StoreUpdateState` members (`Unknown`, `Idle`, `Checking`, `Applying`,
`Failed`) are the same set everywhere. `StoreUpdateStatusFile.Write`/`Read`/`BesideStore` match
between definition and all call sites. `IUpdateLog.Info`/`Error(string, Exception?)` match between
Task 3's interface, Task 3's `RecordingLog`, and Task 4's `EventLogUpdateLog`.
`StoreUpdateCycle.Check` and `ApplyFactory` match Task 3's tests and Task 4's worker.
`UpdaterServiceState` members match between Task 5's enum, its tests, and `StoreStatusReport`.
The service name `SmartCodeGenWikipediaUpdater` and the source `SmartCodeGen Wikipedia Updater` are
identical in Task 4's `Program.cs`, Task 4's `EventLogUpdateLog`, Task 5's `UpdaterServicePresence`,
and both scripts in Task 6.
