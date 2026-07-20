using DomainDiscovery.Wikipedia.Dump;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

public sealed class StoreUpdateTextTests
{
    [Fact]
    public void Updating_ReadsAsAPercentageLine()
        => Assert.Equal("Wikipedia store updating 42%", StoreUpdateText.Updating(42));

    [Fact]
    public void Final_Completed_WithDays_SaysHowManyLanded()
        => Assert.Equal(
            "Wikipedia store: up to date (3 day(s) applied).",
            StoreUpdateText.Final(StartupUpdateOutcome.Completed, 3));

    [Fact]
    public void Final_Completed_WithNoDays_JustSaysUpToDate()
        => Assert.Equal(
            "Wikipedia store: up to date.",
            StoreUpdateText.Final(StartupUpdateOutcome.Completed, 0));

    [Fact]
    public void Final_Skipped_AndFailed_SayTheDaysAreKept()
    {
        Assert.Contains("stopped", StoreUpdateText.Final(StartupUpdateOutcome.Skipped, 2));
        Assert.Contains("kept", StoreUpdateText.Final(StartupUpdateOutcome.Skipped, 2));
        Assert.Contains("failed", StoreUpdateText.Final(StartupUpdateOutcome.Failed, 1));
        Assert.Contains("kept", StoreUpdateText.Final(StartupUpdateOutcome.Failed, 1));
    }
}
