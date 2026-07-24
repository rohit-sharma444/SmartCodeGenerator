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
    public void CurrentDay_SurvivesTheApplyPhase_WhichAlwaysReportsDayZero()
    {
        // IncrementalUpdater.cs:153 calls BeginPhase(Apply, day: 0, ...) with a literal zero — the
        // apply path works from a file path and does not know the day. The day learned during
        // Download must survive into Apply's publishes; a naive BeginPhase overwrites _currentDay
        // with that 0, and Apply's publishes report CurrentDay == null for the rest of the day.
        var (progress, published, clock) = Build(pendingDays: 1);

        progress.BeginDay(0, 1);
        progress.BeginPhase(UpdatePhase.Download, 20260723, 100, 0);
        progress.Report(50);
        progress.EndPhase();

        // Cross the publish throttle so the Apply-phase report below actually publishes.
        clock.Advance(StatusFileProgress.PublishInterval);
        progress.BeginPhase(UpdatePhase.Apply, 0, 100, 0);
        progress.Report(50);

        var status = published[^1];
        Assert.Equal(20260723, status.CurrentDay);
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
