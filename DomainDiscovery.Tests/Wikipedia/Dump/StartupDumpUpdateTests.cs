using DomainDiscovery.Wikipedia.Dump;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

/// <summary>
/// The update the app runs for itself at startup. Every test here is really the same test: whatever
/// the update does, the user still gets their application.
/// </summary>
public sealed class StartupDumpUpdateTests
{
    private static DumpStaleness Stale(params int[] days) => new(20260701, days, Answered: true);

    [Fact]
    public void Run_AppliesEveryPendingDay_OldestFirst()
    {
        var applied = new List<int>();

        var result = new StartupDumpUpdate().Run(
            Stale(20260702, 20260703, 20260704),
            (day, _) => applied.Add(day),
            CancellationToken.None);

        Assert.Equal([20260702, 20260703, 20260704], applied);
        Assert.Equal(3, result.DaysApplied);
        Assert.Equal(StartupUpdateOutcome.Completed, result.Outcome);
    }

    [Fact]
    public void Run_AppliesNothing_WhenTheStoreIsAlreadyCurrent()
    {
        var applied = new List<int>();

        var result = new StartupDumpUpdate().Run(
            new DumpStaleness(20260701, [], Answered: true),
            (day, _) => applied.Add(day),
            CancellationToken.None);

        Assert.Empty(applied);
        Assert.Equal(StartupUpdateOutcome.NothingToDo, result.Outcome);
    }

    /// <summary>
    /// Being offline is the normal case for an app whose whole point is a local dump. A check that
    /// went unanswered knows nothing, and must not be read as "there is work to do".
    /// </summary>
    [Fact]
    public void Run_AppliesNothing_WhenWikimediaDidNotAnswer()
    {
        var applied = new List<int>();

        var result = new StartupDumpUpdate().Run(
            DumpStaleness.Unknown,
            (day, _) => applied.Add(day),
            CancellationToken.None);

        Assert.Empty(applied);
        Assert.Equal(StartupUpdateOutcome.NothingToDo, result.Outcome);
    }

    /// <summary>
    /// Ctrl-C. The user wants their prompt, not another two hours of downloading — so the update
    /// stops, the days it finished are kept, and NOTHING is thrown at the app that is trying to start.
    /// </summary>
    [Fact]
    public void Run_StopsWhereItIs_WhenTheUserInterruptsIt()
    {
        using var interrupt = new CancellationTokenSource();
        var applied = new List<int>();

        var result = new StartupDumpUpdate().Run(
            Stale(20260702, 20260703, 20260704),
            (day, cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested();
                applied.Add(day);
                interrupt.Cancel();
            },
            interrupt.Token);

        Assert.Equal([20260702], applied);
        Assert.Equal(1, result.DaysApplied);
        Assert.Equal(StartupUpdateOutcome.Skipped, result.Outcome);
    }

    /// <summary>
    /// The network dies on day two of twelve. An app that cannot start because Wikimedia hung up is a
    /// worse app than a stale one.
    /// </summary>
    [Fact]
    public void Run_KeepsTheDaysItApplied_AndStillReturns_WhenTheDownloadFails()
    {
        var applied = new List<int>();

        var result = new StartupDumpUpdate().Run(
            Stale(20260702, 20260703, 20260704),
            (day, _) =>
            {
                if (day == 20260703)
                    throw new HttpRequestException("the connection was lost");

                applied.Add(day);
            },
            CancellationToken.None);

        Assert.Equal([20260702], applied);
        Assert.Equal(1, result.DaysApplied);
        Assert.Equal(StartupUpdateOutcome.Failed, result.Outcome);
        Assert.Contains("connection was lost", result.Failure);
    }
}
