using System.Security.Cryptography;
using CodeGenerator.Web.Workflow;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodeGenerator.Web;

/// <summary>
/// Serves one Blazor page on a loopback port, for the length of one command.
/// </summary>
/// <remarks>
/// Extracted from <see cref="BuildAppWebHost"/> when the chat page needed the same machinery. The
/// reason it is shared rather than copied is one line — the token guard. Any process on this
/// machine can reach loopback, and these pages write files and run <c>dotnet</c>, so a route
/// served without a token is served to anything running here. Two copies of that check is two
/// places for it to drift out of step, and the second one would be the one nobody remembers.
/// <para>
/// Three things this class exists to get right, unchanged from where they came from. It binds port
/// 0 and reads the assignment back, so a port in use is not a failure mode. It mints a token and
/// 404s without it. And it stops on every path, because a CLI blocked forever on an abandoned page
/// is worse than no feature.
/// </para>
/// </remarks>
internal sealed class LoopbackHost : IDisposable
{
    /// <summary>How the query string's token parameter is spelled.</summary>
    internal const string TokenParameter = "t";

    private readonly WebApplication _app;

    private LoopbackHost(WebApplication app, string url, string token, CircuitWatch circuits)
    {
        _app = app;
        Url = url;
        SessionToken = token;
        Circuits = circuits;
    }

    /// <summary>The tabs connected to this host. Written by <see cref="WorkflowCircuits"/> from
    /// the circuits Blazor raises, and read by whatever is waiting on the page.</summary>
    public CircuitWatch Circuits { get; }

    /// <summary>What the running host resolves its services from — the container Blazor takes
    /// the circuit handlers out of.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>The address to open, route and token and all.</summary>
    public string Url { get; }

    public string SessionToken { get; }

    /// <summary>
    /// Starts the host, or reports that it could not.
    /// </summary>
    /// <remarks>
    /// False rather than an exception because a machine that cannot bind loopback has a remedy —
    /// the console — not a fault to report.
    /// </remarks>
    /// <param name="route">
    /// The path segment the page is served on, without a leading slash. The token guard is keyed on
    /// it, so a caller that passes the wrong one gets an unreachable page rather than an
    /// unguarded one.
    /// </param>
    public static bool TryStart(
        string route, Action<IServiceCollection> register, out LoopbackHost host)
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
                ApplicationName = typeof(LoopbackHost).Assembly.GetName().Name,
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

            // The watch is one per host and the handler is one per circuit, which is the whole
            // point of the pair: the handlers come and go with the tabs and the count does not.
            var circuits = new CircuitWatch();

            builder.Services.AddSingleton(circuits);
            builder.Services.AddScoped<CircuitHandler, WorkflowCircuits>();

            register(builder.Services);

            var app = builder.Build();

            app.MapStaticAssets();

            // MapRazorComponents stamps anti-forgery metadata on the endpoint, and the endpoint
            // middleware refuses to run without the matching middleware in the pipeline.
            app.UseAntiforgery();

            var guarded = "/" + route.Trim('/');

            // Before the component pipeline, so an untokened request never reaches a circuit.
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments(guarded)
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

            host = new LoopbackHost(
                app, $"{bound}{guarded}?{TokenParameter}={token}", token, circuits);

            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                      or System.Net.Sockets.SocketException)
        {
            return false;
        }
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
