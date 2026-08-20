using System.Security.Cryptography;
using CodeGenerator.Web.Workflow;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodeGenerator.Web;

/// <summary>
/// Serves the `build app` workflow on a loopback port for the length of one command.
/// </summary>
/// <remarks>
/// In-process rather than a spawned app: the components call the same services the console path
/// calls, against the same objects, so there is nothing to serialize and nothing to marshal back
/// — the scaffold runs here and the console prints the result directly.
/// <para>
/// Three things this class exists to get right. It binds port 0 and reads the assignment back, so
/// a port in use is not a failure mode. It mints a token and 404s without it, because any process
/// on this machine can reach loopback and this page writes files and runs <c>dotnet</c>. And it
/// stops on every path — a terminal session, a console interrupt, or a tab closed and never
/// reconnected — because a CLI blocked forever on an abandoned workflow is worse than no feature.
/// </para>
/// </remarks>
internal sealed class BuildAppWebHost : IDisposable
{
    /// <summary>How long the query string's token parameter is called.</summary>
    private const string TokenParameter = "t";

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

    private readonly WebApplication _app;

    private BuildAppWebHost(WebApplication app, string url, string token, CircuitWatch circuits)
    {
        _app = app;
        Url = url;
        SessionToken = token;
        Circuits = circuits;
    }

    /// <summary>The tabs connected to this host. Written by <see cref="WorkflowCircuits"/> from
    /// the circuits Blazor raises, and read by <see cref="Run(BuildSession, CancellationToken,
    /// TimeSpan?)"/>.</summary>
    public CircuitWatch Circuits { get; }

    /// <summary>What the running host resolves its services from — the container Blazor takes
    /// the circuit handlers out of.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>The address to open, token and all.</summary>
    public string Url { get; }

    public string SessionToken { get; }

    /// <summary>
    /// Starts the host, or reports that it could not. False rather than an exception because a
    /// machine that cannot bind loopback is a situation with a remedy — the console wizard — not
    /// a fault to report.
    /// </summary>
    public static bool TryStart(BuildSession session, out BuildAppWebHost host)
    {
        host = null!;

        try
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

            // The application name and content root are pinned to this assembly and its output
            // folder rather than left to the defaults: the defaults are the entry assembly and
            // the current directory, and both are wrong here — the CLI is started from wherever
            // the user happens to be, and under a test host the entry assembly is the test host.
            // Both manifests below are named after this assembly, and are found next to it.
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(BuildAppWebHost).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory,
            });

            // What serves wwwroot and _framework/blazor.web.js from a build output that contains
            // neither: the runtime manifest maps them back to the source folders, and the
            // endpoints manifest publishes a route per asset. A published layout has no runtime
            // manifest and its own real wwwroot, and this call then does nothing.
            builder.WebHost.UseStaticWebAssets();

            // Nothing but warnings: this host shares a console with a REPL, and Kestrel's
            // startup banner in the middle of a command reads as output the tool produced.
            builder.Logging.ClearProviders();

            builder.WebHost.UseUrls("http://127.0.0.1:0");

            builder.Services.AddRazorComponents().AddInteractiveServerComponents();

            // Singleton, so a second tab attaches to the running workflow rather than starting a
            // rival one against the same workspace.
            builder.Services.AddSingleton(session);

            // The watch is one per host and the handler is one per circuit, which is the whole
            // point of the pair: the handlers come and go with the tabs and the count does not.
            var circuits = new CircuitWatch();

            builder.Services.AddSingleton(circuits);
            builder.Services.AddScoped<CircuitHandler, WorkflowCircuits>();

            var app = builder.Build();

            app.MapStaticAssets();

            // MapRazorComponents stamps anti-forgery metadata on the endpoint, and the endpoint
            // middleware refuses to run without the matching middleware in the pipeline.
            app.UseAntiforgery();

            // Before the component pipeline, so an untokened request never reaches a circuit.
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/build")
                    && context.Request.Query[TokenParameter] != token)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                await next(context);
            });

            app.MapRazorComponents<Components.App>().AddInteractiveServerRenderMode();

            app.Start();

            var bound = app.Urls.First();

            host = new BuildAppWebHost(
                app, $"{bound}/build?{TokenParameter}={token}", token, circuits);

            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                      or System.Net.Sockets.SocketException)
        {
            return false;
        }
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

        return session.Phase switch
        {
            SessionPhase.Abandoned => written is null
                ? BuildOutcome.Abandoned
                : BuildOutcome.AbandonedLeaving(written),
            SessionPhase.Generated => session.Outcome ?? BuildOutcome.Cancelled,
            _ => written is null
                ? BuildOutcome.Cancelled
                : BuildOutcome.CancelledLeaving(written),
        };
    }

    public void Dispose()
    {
        try
        {
            _app.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // Already going down. Nothing to add.
        }

        ((IDisposable)_app).Dispose();
    }
}
