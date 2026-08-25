using CodeGenerator.Web.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Web;

/// <summary>
/// Serves the `build app` workflow on a loopback port for the length of one command.
/// </summary>
/// <remarks>
/// In-process rather than a spawned app: the components call the same services the console path
/// calls, against the same objects, so there is nothing to serialize and nothing to marshal back
/// — the scaffold runs here and the console prints the result directly.
/// <para>
/// The host mechanics — binding port 0 and reading the assignment back, the token and the 404
/// without it, the static-asset manifests, anti-forgery, the circuit count and the stop on every
/// path — now live in <see cref="LoopbackHost"/>, because the chat page needs the same ones and
/// the token guard must exist exactly once. What is left here is the workflow's own half: the
/// wait, the abandon grace, and what the console prints when it ends.
/// </para>
/// </remarks>
internal sealed class BuildAppWebHost : IDisposable
{
    /// <summary>The route the workflow page is served on.</summary>
    private const string Route = "build";

    /// <summary>How often <see cref="Run"/> looks at the session.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// How long every tab must have been gone before the workflow is treated as abandoned.
    /// </summary>
    /// <remarks>
    /// Long enough that a refresh — circuit down, new circuit up about a second later — is not
    /// mistaken for a closed tab, and short enough that a user who closed the tab is not left
    /// wondering why their prompt has not come back.
    /// </remarks>
    private static readonly TimeSpan AbandonGrace = TimeSpan.FromSeconds(10);

    private readonly LoopbackHost _host;

    private BuildAppWebHost(LoopbackHost host) => _host = host;

    /// <summary>The tabs connected to this host. Written by <see cref="WorkflowCircuits"/> from
    /// the circuits Blazor raises, and read by <see cref="Run(BuildSession, CancellationToken,
    /// TimeSpan?)"/>.</summary>
    public CircuitWatch Circuits => _host.Circuits;

    /// <summary>What the running host resolves its services from — the container Blazor takes
    /// the circuit handlers out of.</summary>
    public IServiceProvider Services => _host.Services;

    /// <summary>The address to open, token and all.</summary>
    public string Url => _host.Url;

    public string SessionToken => _host.SessionToken;

    /// <summary>
    /// Starts the host, or reports that it could not. False rather than an exception because a
    /// machine that cannot bind loopback is a situation with a remedy — the console wizard — not
    /// a fault to report.
    /// </summary>
    public static bool TryStart(BuildSession session, out BuildAppWebHost host)
    {
        host = null!;

        // Singleton, so a second tab attaches to the running workflow rather than starting a
        // rival one against the same workspace.
        if (!LoopbackHost.TryStart(Route, s => s.AddSingleton(session), out var loopback))
            return false;

        host = new BuildAppWebHost(loopback);

        return true;
    }

    /// <summary>
    /// Starts a host of its own and runs the workflow to its end.
    /// </summary>
    /// <remarks>
    /// For a caller with no use for the URL. The interactive command uses
    /// <see cref="TryStart"/> and the instance <see cref="Run(BuildSession, CancellationToken,
    /// TimeSpan?)"/> instead, because it has to print the address before the waiting begins —
    /// and because the tabs must be counted on the host the browser is actually looking at.
    /// </remarks>
    public static BuildOutcome Run(BuildSession session, CancellationToken ct)
    {
        if (!TryStart(session, out var host))
            return new BuildOutcome(SessionPhase.Cancelled, ["Could not start the build workflow."]);

        using (host)
            return host.Run(session, ct);
    }

    /// <summary>
    /// Runs the workflow to its end and returns what the console should print.
    /// </summary>
    /// <remarks>
    /// Polls rather than awaiting an event, for one reason: the session is mutated from a Blazor
    /// circuit on another thread, and a poll costs nothing next to a workflow a person is typing
    /// into. Every exit is bounded — there is no path here that waits forever, including the one
    /// where the person walked away: a tab closed and never replaced ends the workflow, or the
    /// console would wait on a page nobody has open.
    /// </remarks>
    /// <param name="grace">
    /// How long the last tab must have been gone. Defaults to <see cref="AbandonGrace"/>; passed
    /// explicitly by the tests, which have no interest in waiting ten real seconds.
    /// </param>
    public BuildOutcome Run(BuildSession session, CancellationToken ct, TimeSpan? grace = null)
    {
        var window = grace ?? AbandonGrace;

        while (session.Phase == SessionPhase.Running)
        {
            if (ct.IsCancellationRequested)
            {
                session.Cancel();
                break;
            }

            if (Circuits.ShouldAbandon(window, DateTimeOffset.UtcNow))
            {
                session.Abandon();
                break;
            }

            Thread.Sleep(PollInterval);
        }

        return OutcomeFor(session);
    }

    /// <summary>
    /// What the console prints for a workflow that has ended.
    /// </summary>
    /// <remarks>
    /// Separated from the wait above so it can be asserted without binding a port, and because the
    /// interesting part is not the loop: a run that ends after the generate stage leaves a project
    /// on disk, and both terminal messages used to say "Nothing was generated" regardless. The
    /// cancel question promises that the project stays, so this is the line that has to agree.
    /// </remarks>
    internal static BuildOutcome OutcomeFor(BuildSession session)
    {
        var written = session.Generated?.Scaffold.ProjectPath;

        var outcome = session.Phase switch
        {
            SessionPhase.Abandoned => written is null
                ? BuildOutcome.Abandoned
                : BuildOutcome.AbandonedLeaving(written),
            SessionPhase.Generated => session.Outcome ?? BuildOutcome.Cancelled,
            _ => written is null
                ? BuildOutcome.Cancelled
                : BuildOutcome.CancelledLeaving(written),
        };

        return outcome with { Lines = [.. outcome.Lines, .. Repeatable(session)] };
    }

    /// <summary>
    /// The command that would do this again, on the end of every outcome.
    /// </summary>
    /// <remarks>
    /// The console is where the user ends up, and until now a workflow left nothing behind that
    /// could be run again or put in a script — a cancelled run in particular cost the whole
    /// interview to repeat. Worth most exactly where it was missing most.
    /// </remarks>
    private static IEnumerable<string> Repeatable(BuildSession session)
    {
        if (session.Description.Length == 0 && session.Source is null)
            return [];

        return ["", "To do the same again:", .. BuildCommandEcho.For(session)];
    }

    public void Dispose() => _host.Dispose();
}
