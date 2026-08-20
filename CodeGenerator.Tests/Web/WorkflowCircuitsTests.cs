using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The adapter between Blazor's circuit callbacks and <see cref="CircuitWatch"/>.
/// </summary>
/// <remarks>
/// What is proved here is that each callback moves the watch the right way. What is NOT proved
/// here — and cannot be, without a browser — is that Blazor calls them: that seam is the
/// framework's, and the reason the connection pair is used rather than the circuit pair is
/// written on the handler itself.
/// </remarks>
[TestClass]
public class WorkflowCircuitsTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task A_connection_coming_up_is_an_open_tab()
    {
        var watch = new CircuitWatch();
        var handler = new WorkflowCircuits(watch);

        await handler.OnConnectionUpAsync(null!, CancellationToken.None);

        Assert.IsFalse(watch.ShouldAbandon(Grace, Start.AddHours(1)));
    }

    [TestMethod]
    public async Task A_connection_going_down_starts_the_grace_period()
    {
        var watch = new CircuitWatch();
        var handler = new WorkflowCircuits(watch);

        await handler.OnConnectionUpAsync(null!, CancellationToken.None);
        await handler.OnConnectionDownAsync(null!, CancellationToken.None);

        Assert.IsTrue(watch.ShouldAbandon(TimeSpan.Zero, DateTimeOffset.UtcNow.AddHours(1)));
    }
}
