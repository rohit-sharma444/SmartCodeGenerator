using CodeGenerator.App;
using DomainDiscovery.Wikipedia.Dump;
using Xunit;
// The project-wide global using of Microsoft.VisualStudio.TestTools.UnitTesting (MSTest) makes
// `Assert` ambiguous against Xunit.Assert in this xunit-based file; alias to disambiguate.
using Assert = Xunit.Assert;

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
