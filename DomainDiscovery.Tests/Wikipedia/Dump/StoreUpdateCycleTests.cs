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
