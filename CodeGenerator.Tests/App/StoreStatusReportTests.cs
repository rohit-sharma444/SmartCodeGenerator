using CodeGenerator.App;
using DomainDiscovery.Wikipedia.Dump;

namespace CodeGenerator.Tests.App;

[TestClass]
public sealed class StoreStatusReportTests
{
    private const string Hint = @".\tools\install-updater-service.ps1";

    private static readonly DateTimeOffset Now = new(2026, 7, 24, 10, 0, 0, TimeSpan.Zero);

    private static string Joined(IReadOnlyList<string> lines) => string.Join("\n", lines);

    private static StoreUpdateStatus Idle => new(
        StoreUpdateState.Idle, 100, 0, null,
        new DateTimeOffset(2026, 7, 24, 4, 0, 0, TimeSpan.Zero), null, "1.0", Now);

    [TestMethod]
    public void Running_And_UpToDate_ReportsTheDateAndTheService()
    {
        var text = Joined(StoreStatusReport.Lines(
            20260724, Idle, UpdaterServiceState.Running, Hint, Now));

        StringAssert.Contains(text, "20260724");
        Assert.IsTrue(text.Contains("updater service", StringComparison.OrdinalIgnoreCase),
            "text should contain 'updater service' (case-insensitive)");
        Assert.IsFalse(text.Contains(Hint, StringComparison.Ordinal),
            $"text should not contain the hint '{Hint}'");
    }

    [TestMethod]
    public void Applying_ShowsThePercentage()
    {
        var applying = new StoreUpdateStatus(
            StoreUpdateState.Applying, 43, 6, 20260718, null, null, "1.0", Now);

        var text = Joined(StoreStatusReport.Lines(
            20260717, applying, UpdaterServiceState.Running, Hint, Now));

        StringAssert.Contains(text, "43%");
    }

    [TestMethod]
    public void Applying_AlsoNamesTheDayBeingApplied()
    {
        // Finding 5: the percentage alone does not say which day is in flight, and a multi-day
        // catch-up can sit at, say, 40% for several different days in a row.
        var applying = new StoreUpdateStatus(
            StoreUpdateState.Applying, 43, 6, 20260718, null, null, "1.0", Now);

        var text = Joined(StoreStatusReport.Lines(
            20260717, applying, UpdaterServiceState.Running, Hint, Now));

        StringAssert.Contains(text, "20260718");
    }

    [TestMethod]
    public void NotInstalled_ShowsTheInstallHintProminently()
    {
        // Sole ownership means a machine without the service stops updating entirely. This hint is
        // the only thing covering that gap, so it must be loud.
        var lines = StoreStatusReport.Lines(
            20260710, StoreUpdateStatus.Unknown, UpdaterServiceState.NotInstalled, Hint, Now);

        var text = Joined(lines);
        StringAssert.Contains(text, Hint);
        StringAssert.Contains(text, "NOT INSTALLED");
        Assert.IsGreaterThanOrEqualTo(3, lines.Count, "the hint needs room to be noticed, not one quiet line");
    }

    [TestMethod]
    public void Stopped_SaysSoAndStillOffersTheHint()
    {
        var text = Joined(StoreStatusReport.Lines(
            20260710, StoreUpdateStatus.Unknown, UpdaterServiceState.Stopped, Hint, Now));

        Assert.IsTrue(text.Contains("stopped", StringComparison.OrdinalIgnoreCase),
            "text should contain 'stopped' (case-insensitive)");
    }

    [TestMethod]
    public void Failed_SurfacesTheRecordedError()
    {
        var failed = new StoreUpdateStatus(
            StoreUpdateState.Failed, 12, 4, 20260718, null, "the server hung up", "1.0", Now);

        var text = Joined(StoreStatusReport.Lines(
            20260717, failed, UpdaterServiceState.Running, Hint, Now));

        StringAssert.Contains(text, "the server hung up");
    }

    [TestMethod]
    public void AnUnknownStoreDate_DoesNotPrintANullDate()
    {
        var text = Joined(StoreStatusReport.Lines(
            null, StoreUpdateStatus.Unknown, UpdaterServiceState.Running, Hint, Now));

        Assert.IsFalse(text.Contains("null", StringComparison.OrdinalIgnoreCase),
            "text should not contain 'null' (case-insensitive)");
    }

    [TestMethod]
    public void Running_WithAnUnansweredCheck_SaysWikimediaCouldNotBeReached()
    {
        // Finding 2: StoreUpdateCycle writes StoreUpdateState.Unknown deliberately to mean "we
        // asked and got no answer; claim nothing". The old default arm printed "no status published
        // yet" for this — reading as "hasn't got round to it yet" for the exact case (an unanswered
        // check silently letting a stale store report itself healthy) this branch exists to guard
        // against. It must say the check could not reach Wikimedia, not that nothing has run.
        var text = Joined(StoreStatusReport.Lines(
            20260710, StoreUpdateStatus.Unknown, UpdaterServiceState.Running, Hint, Now));

        Assert.IsFalse(text.Contains("no status published yet", StringComparison.OrdinalIgnoreCase),
            "an unanswered check must not read as merely 'nothing has run yet'");
        Assert.IsTrue(text.Contains("wikimedia", StringComparison.OrdinalIgnoreCase),
            "text should explain that Wikimedia could not be reached");
    }

    [TestMethod]
    public void Applying_OlderThanAnHour_ReadsAsStaleRatherThanALiveConfidentPercentage()
    {
        // Finding 6: LastSuccessUtc records the last SUCCESS, not when this snapshot was written,
        // so a status left behind by a killed process (e.g. Applying at 52%, from a run that ended
        // hours ago) reads as live. Anything older than an hour must say so instead of printing a
        // confident percentage.
        var stale = new StoreUpdateStatus(
            StoreUpdateState.Applying, 52, 3, 20260720, null, null, "1.0",
            Now - TimeSpan.FromHours(2));

        var text = Joined(StoreStatusReport.Lines(
            20260717, stale, UpdaterServiceState.Running, Hint, Now));

        Assert.IsFalse(text.Contains("52%", StringComparison.Ordinal),
            "a stale snapshot must not be printed as a confident live percentage");
        Assert.IsTrue(text.Contains("stale", StringComparison.OrdinalIgnoreCase)
            || text.Contains("hour", StringComparison.OrdinalIgnoreCase),
            "text should say the status is stale / too old to trust");
    }

    [TestMethod]
    public void Applying_WithinAnHour_StillShowsTheLivePercentage()
    {
        // The counterpart to the stale case: a snapshot known to be recent must still print
        // normally, so the stale check does not swallow genuinely live progress.
        var fresh = new StoreUpdateStatus(
            StoreUpdateState.Applying, 52, 3, 20260720, null, null, "1.0",
            Now - TimeSpan.FromMinutes(1));

        var text = Joined(StoreStatusReport.Lines(
            20260717, fresh, UpdaterServiceState.Running, Hint, Now));

        StringAssert.Contains(text, "52%");
    }
}
