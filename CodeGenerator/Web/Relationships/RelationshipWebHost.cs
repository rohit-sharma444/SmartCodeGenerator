using CodeGenerator.Web.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Web.Relationships;

/// <summary>
/// Serves the relationships page for the length of one <c>show relationships</c> command.
/// </summary>
/// <remarks>
/// The mechanics belong to <see cref="LoopbackHost"/>. What is here is the waiting, and it is
/// simpler than the chat host's: there is no session to end, because nothing on the page changes
/// anything. It ends when the tab goes away or the console is interrupted.
/// </remarks>
internal sealed class RelationshipWebHost : IDisposable
{
    /// <summary>The route the page is served on.</summary>
    private const string Route = "relationships";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// How long every tab must have been gone before the command gives the prompt back.
    /// </summary>
    /// <remarks>
    /// The same ten seconds the chat and the build workflow use, for the same reason: a refresh
    /// takes the circuit down and brings a new one up about a second later, and a shorter window
    /// would read that as the tab being closed.
    /// </remarks>
    private static readonly TimeSpan AbandonGrace = TimeSpan.FromSeconds(10);

    private readonly LoopbackHost _host;

    private RelationshipWebHost(LoopbackHost host) => _host = host;

    public string Url => _host.Url;

    public CircuitWatch Circuits => _host.Circuits;

    public IServiceProvider Services => _host.Services;

    public static bool TryStart(RelationshipView view, out RelationshipWebHost host)
    {
        host = null!;

        if (!LoopbackHost.TryStart(Route, s => s.AddSingleton(view), out var loopback))
            return false;

        host = new RelationshipWebHost(loopback);

        return true;
    }

    /// <summary>
    /// Waits until the tab is closed or the console is interrupted.
    /// </summary>
    /// <remarks>
    /// A poll, like the chat host, and bounded the same way: a page nobody has open must not hold
    /// the prompt. Waiting for the FIRST tab is not special-cased — <c>ShouldAbandon</c> already
    /// treats a host that has never had a circuit as not yet abandoned, so the browser has as long
    /// as it needs to start.
    /// </remarks>
    public void Run(CancellationToken ct, TimeSpan? grace = null)
    {
        var window = grace ?? AbandonGrace;

        while (!ct.IsCancellationRequested)
        {
            if (Circuits.ShouldAbandon(window, DateTimeOffset.UtcNow))
                break;

            Thread.Sleep(PollInterval);
        }
    }

    public void Dispose() => _host.Dispose();
}
