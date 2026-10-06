using CodeGenerator.Web.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Web.Chat;

/// <summary>
/// Serves the chat page for the length of one <c>chat</c> command.
/// </summary>
/// <remarks>
/// The mechanics belong to <see cref="LoopbackHost"/>; what is here is the waiting. It polls
/// rather than awaiting an event because the session is mutated from a Blazor circuit on another
/// thread, and a poll costs nothing next to a conversation someone is typing into.
/// <para>
/// Every exit is bounded, including the one where the person walked away: a tab closed and never
/// replaced ends the session, or the console would wait on a page nobody has open.
/// </para>
/// </remarks>
internal sealed class ChatWebHost : IDisposable
{
    /// <summary>The route the chat page is served on.</summary>
    private const string Route = "chat";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// How long every tab must have been gone before the session is treated as abandoned.
    /// </summary>
    /// <remarks>
    /// Long enough that a refresh (circuit down, new circuit up about a second later) is not
    /// mistaken for a closed tab, and short enough that someone who closed the tab is not left
    /// wondering why their prompt has not come back. Same number as the build workflow, for the
    /// same reasons.
    /// </remarks>
    private static readonly TimeSpan AbandonGrace = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long an ended session is given to draw its closing view before the host stops.
    /// </summary>
    /// <remarks>
    /// Stopping the host the moment <c>exit</c> or Ctrl-C ended the session took the circuit down
    /// before the page heard, so every tab sat on Blazor's "reconnecting" for two minutes instead
    /// of saying the session was over. Same number as the build workflow's.
    /// </remarks>
    private static readonly TimeSpan FinishGrace = TimeSpan.FromSeconds(1);

    private readonly LoopbackHost _host;

    private ChatWebHost(LoopbackHost host) => _host = host;

    public string Url => _host.Url;

    public string SessionToken => _host.SessionToken;

    public CircuitWatch Circuits => _host.Circuits;

    public IServiceProvider Services => _host.Services;

    public static bool TryStart(ChatSession session, out ChatWebHost host)
    {
        host = null!;

        // Singleton, so a second tab joins the conversation rather than starting a rival one
        // against the same workspace.
        if (!LoopbackHost.TryStart(Route, s => s.AddSingleton(session), out var loopback))
            return false;

        host = new ChatWebHost(loopback);

        return true;
    }

    public void Run(ChatSession session, CancellationToken ct, TimeSpan? grace = null, TimeSpan? linger = null)
    {
        var window = grace ?? AbandonGrace;

        while (!session.Ended)
        {
            if (ct.IsCancellationRequested)
            {
                session.End();
                break;
            }

            if (Circuits.ShouldAbandon(window, DateTimeOffset.UtcNow))
            {
                // No tab is left to draw a closing view to, so there is nothing to wait for.
                session.End();
                return;
            }

            Thread.Sleep(PollInterval);
        }

        // Ended by `exit` in a tab or by Ctrl-C here: the tabs are still open, and each has one
        // more render to receive - the closing view.
        Thread.Sleep(linger ?? FinishGrace);
    }

    public void Dispose() => _host.Dispose();
}
