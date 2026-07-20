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
