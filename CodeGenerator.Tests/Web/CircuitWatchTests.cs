using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// When a closed tab means the workflow is over, and — just as important — when it does not.
/// </summary>
/// <remarks>
/// Two mistakes are possible here and they are not symmetrical. Abandoning too eagerly kills a
/// workflow someone is still using, over a page refresh; abandoning never leaves the CLI blocked
/// forever, which is the failure the design called the worst this feature can have. Every instant
/// is passed in rather than read from the clock, so both are assertable without waiting.
/// </remarks>
[TestClass]
public class CircuitWatchTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The URL is printed whether or not a browser opened, and a user who pastes it a minute
    /// later must find the workflow still there.
    /// </summary>
    [TestMethod]
    public void Waits_forever_when_no_tab_has_ever_connected()
    {
        var watch = new CircuitWatch();

        Assert.IsFalse(watch.ShouldAbandon(Grace, Start.AddHours(1)));
    }

    [TestMethod]
    public void Does_not_abandon_while_a_tab_is_connected()
    {
        var watch = new CircuitWatch();

        watch.Opened(Start);

        Assert.IsFalse(watch.ShouldAbandon(Grace, Start.AddHours(1)));
    }

    [TestMethod]
    public void Abandons_once_the_last_tab_has_been_gone_longer_than_the_grace_period()
    {
        var watch = new CircuitWatch();

        watch.Opened(Start);
        watch.Closed(Start);

        Assert.IsTrue(watch.ShouldAbandon(Grace, Start + Grace + TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public void Does_not_abandon_before_the_grace_period_is_up()
    {
        var watch = new CircuitWatch();

        watch.Opened(Start);
        watch.Closed(Start);

        Assert.IsFalse(watch.ShouldAbandon(Grace, Start + Grace - TimeSpan.FromSeconds(1)));
    }

    /// <summary>
    /// The reason the grace period exists at all: a refresh drops the circuit and raises a new
    /// one a moment later, and that is not a closed tab.
    /// </summary>
    [TestMethod]
    public void A_refresh_inside_the_grace_period_does_not_abandon()
    {
        var watch = new CircuitWatch();

        watch.Opened(Start);
        watch.Closed(Start);
        watch.Opened(Start.AddSeconds(1));

        Assert.IsFalse(watch.ShouldAbandon(Grace, Start.AddHours(1)));
    }

    [TestMethod]
    public void Closing_one_of_two_tabs_leaves_the_workflow_running()
    {
        var watch = new CircuitWatch();

        watch.Opened(Start);
        watch.Opened(Start);
        watch.Closed(Start);

        Assert.IsFalse(watch.ShouldAbandon(Grace, Start.AddHours(1)));
    }

    [TestMethod]
    public void Closing_both_tabs_abandons()
    {
        var watch = new CircuitWatch();

        watch.Opened(Start);
        watch.Opened(Start);
        watch.Closed(Start);
        watch.Closed(Start.AddSeconds(2));

        Assert.IsTrue(watch.ShouldAbandon(Grace, Start.AddSeconds(2) + Grace + TimeSpan.FromSeconds(1)));
    }

    /// <summary>
    /// The clock that matters is the last tab's, not the first's. Otherwise a workflow driven for
    /// an hour in a second tab would abandon the instant that tab closed.
    /// </summary>
    [TestMethod]
    public void The_grace_period_runs_from_the_last_tab_leaving()
    {
        var watch = new CircuitWatch();

        watch.Opened(Start);
        watch.Opened(Start);
        watch.Closed(Start);

        var lastLeft = Start.AddHours(1);
        watch.Closed(lastLeft);

        Assert.IsFalse(watch.ShouldAbandon(Grace, lastLeft + Grace - TimeSpan.FromSeconds(1)));
        Assert.IsTrue(watch.ShouldAbandon(Grace, lastLeft + Grace + TimeSpan.FromSeconds(1)));
    }
}
