using CodeGenerator.App;
using DomainDiscovery.Wikipedia.Dump;

namespace CodeGenerator.Tests.App;

[TestClass]
public sealed class StoreStatusReportTests
{
    private const string Hint = @".\tools\install-updater-service.ps1";

    private static string Joined(IReadOnlyList<string> lines) => string.Join("\n", lines);

    private static StoreUpdateStatus Idle => new(
        StoreUpdateState.Idle, 100, 0, null,
        new DateTimeOffset(2026, 7, 24, 4, 0, 0, TimeSpan.Zero), null, "1.0");

    [TestMethod]
    public void Running_And_UpToDate_ReportsTheDateAndTheService()
    {
        var text = Joined(StoreStatusReport.Lines(
            20260724, Idle, UpdaterServiceState.Running, Hint));

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
            StoreUpdateState.Applying, 43, 6, 20260718, null, null, "1.0");

        var text = Joined(StoreStatusReport.Lines(
            20260717, applying, UpdaterServiceState.Running, Hint));

        StringAssert.Contains(text, "43%");
    }

    [TestMethod]
    public void NotInstalled_ShowsTheInstallHintProminently()
    {
        // Sole ownership means a machine without the service stops updating entirely. This hint is
        // the only thing covering that gap, so it must be loud.
        var lines = StoreStatusReport.Lines(
            20260710, StoreUpdateStatus.Unknown, UpdaterServiceState.NotInstalled, Hint);

        var text = Joined(lines);
        StringAssert.Contains(text, Hint);
        StringAssert.Contains(text, "NOT INSTALLED");
        Assert.IsGreaterThanOrEqualTo(3, lines.Count, "the hint needs room to be noticed, not one quiet line");
    }

    [TestMethod]
    public void Stopped_SaysSoAndStillOffersTheHint()
    {
        var text = Joined(StoreStatusReport.Lines(
            20260710, StoreUpdateStatus.Unknown, UpdaterServiceState.Stopped, Hint));

        Assert.IsTrue(text.Contains("stopped", StringComparison.OrdinalIgnoreCase),
            "text should contain 'stopped' (case-insensitive)");
    }

    [TestMethod]
    public void Failed_SurfacesTheRecordedError()
    {
        var failed = new StoreUpdateStatus(
            StoreUpdateState.Failed, 12, 4, 20260718, null, "the server hung up", "1.0");

        var text = Joined(StoreStatusReport.Lines(
            20260717, failed, UpdaterServiceState.Running, Hint));

        StringAssert.Contains(text, "the server hung up");
    }

    [TestMethod]
    public void AnUnknownStoreDate_DoesNotPrintANullDate()
    {
        var text = Joined(StoreStatusReport.Lines(
            null, StoreUpdateStatus.Unknown, UpdaterServiceState.Running, Hint));

        Assert.IsFalse(text.Contains("null", StringComparison.OrdinalIgnoreCase),
            "text should not contain 'null' (case-insensitive)");
    }
}
