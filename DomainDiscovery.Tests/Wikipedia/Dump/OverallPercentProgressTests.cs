using DomainDiscovery.Wikipedia.Dump;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

public sealed class OverallPercentProgressTests
{
    private static (OverallPercentProgress Progress, List<int> Percents) New()
    {
        var percents = new List<int>();
        var progress = new OverallPercentProgress(percents.Add, (_, _) => { });
        return (progress, percents);
    }

    [Fact]
    public void SingleDay_DownloadFillsFirstHalf_ApplyFillsSecond()
    {
        var (progress, percents) = New();

        progress.BeginDay(0, 1);

        progress.BeginPhase(UpdatePhase.Download, day: 0, total: 100, initial: 0);
        progress.Report(50);   // 25%
        progress.EndPhase();   // 50%

        progress.BeginPhase(UpdatePhase.Apply, day: 0, total: 100, initial: 0);
        progress.Report(50);   // 75%
        progress.EndPhase();   // 100%

        Assert.Equal([0, 25, 50, 75, 100], percents);
    }

    [Fact]
    public void MultipleDays_ProgressIsMonotonic_AndReachesOneHundred()
    {
        var (progress, percents) = New();

        for (var day = 0; day < 3; day++)
        {
            progress.BeginDay(day, 3);
            progress.BeginPhase(UpdatePhase.Download, day, total: 10, initial: 0);
            progress.Report(5);
            progress.EndPhase();
            progress.BeginPhase(UpdatePhase.Apply, day, total: 10, initial: 0);
            progress.Report(5);
            progress.EndPhase();
        }

        for (var i = 1; i < percents.Count; i++)
            Assert.True(percents[i] >= percents[i - 1], "percentage must never go backwards");

        Assert.Equal(100, percents[^1]);
    }

    [Fact]
    public void ZeroTotal_DoesNotDivideByZero()
    {
        var (progress, percents) = New();

        progress.BeginDay(0, 1);
        progress.BeginPhase(UpdatePhase.Download, day: 0, total: 0, initial: 0);
        progress.Report(0);

        Assert.Equal([0], percents);
    }

    [Fact]
    public void Finish_ForwardsOutcomeAndCount()
    {
        StartupUpdateOutcome? seen = null;
        var seenDays = -1;
        var progress = new OverallPercentProgress(_ => { }, (o, d) => { seen = o; seenDays = d; });

        progress.Finish(StartupUpdateOutcome.Completed, 3);

        Assert.Equal(StartupUpdateOutcome.Completed, seen);
        Assert.Equal(3, seenDays);
    }
}
