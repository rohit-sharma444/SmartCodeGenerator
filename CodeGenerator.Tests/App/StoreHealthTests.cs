using CodeGenerator.App;
using DomainDiscovery.Wikipedia.Dump;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Whether startup may stay silent about the store.
/// </summary>
/// <remarks>
/// Load-bearing in one direction only: a false positive here hides a real problem — a store that
/// nothing is updating, or an update that failed — behind a clean-looking prompt. So every case
/// that is not plainly healthy has to report unhealthy, including the ones that mean "we do not
/// know".
/// </remarks>
[TestClass]
public class StoreHealthTests
{
    private const int Dump = 20260812;

    private static readonly DateTimeOffset Now =
        new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    private static StoreUpdateStatus Status(
        StoreUpdateState state, string? error = null, DateTimeOffset? updated = null)
        => new(state, 0, 0, null, null, error, null, updated ?? Now);

    [TestMethod]
    public void AStoreWithARunningIdleUpdaterIsHealthy()
    {
        Assert.IsTrue(StoreStatusReport.IsHealthy(
            Dump, Status(StoreUpdateState.Idle), UpdaterServiceState.Running, Now));
    }

    [TestMethod]
    public void WorkInProgressIsHealthy()
    {
        // Checking and a fresh Applying are the service doing its job; neither needs announcing.
        Assert.IsTrue(StoreStatusReport.IsHealthy(
            Dump, Status(StoreUpdateState.Checking), UpdaterServiceState.Running, Now));

        Assert.IsTrue(StoreStatusReport.IsHealthy(
            Dump, Status(StoreUpdateState.Applying), UpdaterServiceState.Running, Now));
    }

    [TestMethod]
    public void AMissingStoreIsNotHealthy()
    {
        // Null date is a store that cannot say what it holds. `build app:` then silently falls
        // back to the online API, which is exactly what the user must be told.
        Assert.IsFalse(StoreStatusReport.IsHealthy(
            null, Status(StoreUpdateState.Idle), UpdaterServiceState.Running, Now));
    }

    [TestMethod]
    public void AnyServiceStateOtherThanRunningIsNotHealthy()
    {
        foreach (var service in new[]
                 {
                     UpdaterServiceState.NotInstalled,
                     UpdaterServiceState.Stopped,
                     UpdaterServiceState.Unknown,
                 })
        {
            Assert.IsFalse(
                StoreStatusReport.IsHealthy(Dump, Status(StoreUpdateState.Idle), service, Now),
                $"{service} was treated as healthy");
        }
    }

    [TestMethod]
    public void AFailedOrUnknownUpdateIsNotHealthy()
    {
        Assert.IsFalse(StoreStatusReport.IsHealthy(
            Dump, Status(StoreUpdateState.Failed, "boom"), UpdaterServiceState.Running, Now));

        // "Could not reach Wikimedia" must never read as a clean bill of health — the same
        // constraint the Unknown state exists for.
        Assert.IsFalse(StoreStatusReport.IsHealthy(
            Dump, Status(StoreUpdateState.Unknown), UpdaterServiceState.Running, Now));
    }

    [TestMethod]
    public void AStaleApplyingSnapshotIsNotHealthy()
    {
        // A process killed mid-apply leaves a frozen status. Reported as healthy, it would hide
        // that nothing has updated the store for hours.
        Assert.IsFalse(StoreStatusReport.IsHealthy(
            Dump,
            Status(StoreUpdateState.Applying, updated: Now.AddHours(-2)),
            UpdaterServiceState.Running,
            Now));
    }

    [TestMethod]
    public void EveryUnhealthyCaseStillHasSomethingToPrint()
    {
        // The startup path prints Lines() only when IsHealthy is false, so a case that reports
        // unhealthy and then renders nothing would be a silent gap.
        var unhealthy = new (int? Date, StoreUpdateStatus Status, UpdaterServiceState Service)[]
        {
            (null, Status(StoreUpdateState.Idle), UpdaterServiceState.Running),
            (Dump, Status(StoreUpdateState.Idle), UpdaterServiceState.NotInstalled),
            (Dump, Status(StoreUpdateState.Idle), UpdaterServiceState.Stopped),
            (Dump, Status(StoreUpdateState.Idle), UpdaterServiceState.Unknown),
            (Dump, Status(StoreUpdateState.Failed, "boom"), UpdaterServiceState.Running),
            (Dump, Status(StoreUpdateState.Unknown), UpdaterServiceState.Running),
        };

        foreach (var (date, status, service) in unhealthy)
        {
            Assert.IsFalse(StoreStatusReport.IsHealthy(date, status, service, Now));

            var text = string.Join(
                "\n", StoreStatusReport.Lines(date, status, service, "install.ps1", Now));

            Assert.IsNotEmpty(text.Trim(), $"{date}/{status.State}/{service} printed nothing");
        }
    }
}
