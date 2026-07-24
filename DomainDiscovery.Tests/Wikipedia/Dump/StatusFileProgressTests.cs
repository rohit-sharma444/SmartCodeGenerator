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
    public void CurrentDay_ThreadsThroughTheApplyPhase_NowThatApplyKnowsTheRealDay()
    {
        // IncrementalUpdater.Apply now receives its day explicitly from UpdateDay, instead of
        // always publishing a literal zero, so BeginPhase(Apply, ...) carries the real day and
        // StatusFileProgress no longer needs to guess or preserve a value learned earlier.
        var (progress, published, clock) = Build(pendingDays: 1);

        progress.BeginDay(0, 1);
        progress.BeginPhase(UpdatePhase.Download, 20260723, 100, 0);
        progress.Report(50);
        progress.EndPhase();

        // Cross the publish throttle so the Apply-phase report below actually publishes.
        clock.Advance(StatusFileProgress.PublishInterval);
        progress.BeginPhase(UpdatePhase.Apply, 20260723, 100, 0);
        progress.Report(50);

        var status = published[^1];
        Assert.Equal(20260723, status.CurrentDay);
    }

    [Fact]
    public void CurrentDay_ReportsCorrectly_OnTheResumePath_WhenDownloadNeverPublishedAtAll()
    {
        // The resume path this guarded against: IncrementalUpdater.Download returns BEFORE calling
        // BeginPhase when the day's file is already on disk in full (restart between download-
        // complete and apply-complete). So the only BeginPhase call for this day is Apply's — it
        // must carry the real day on its own, not rely on a Download call that never happened.
        var (progress, published, _) = Build(pendingDays: 1);

        progress.BeginDay(0, 1);
        progress.BeginPhase(UpdatePhase.Apply, 20260724, 100, 0);
        progress.Report(50);

        var status = published[^1];
        Assert.Equal(20260724, status.CurrentDay);
    }

    [Fact]
    public void EveryPublish_StampsWhenItWasWritten()
    {
        // Finding 6: LastSuccessUtc records the last SUCCESS, not when a snapshot was written, so a
        // reader cannot otherwise tell a live status from one left behind by a killed process.
        var (progress, published, clock) = Build();

        progress.BeginDay(0, 1);
        progress.BeginPhase(UpdatePhase.Download, 20260718, 100, 0);
        progress.Report(50);

        Assert.Equal(clock.Now, published[^1].UpdatedUtc);
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
