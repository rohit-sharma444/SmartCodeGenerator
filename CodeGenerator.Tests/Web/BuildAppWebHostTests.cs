using AppScaffolding;
using CodeGenerator.Web;
using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The host, without a browser. What matters here is not what the page looks like but that it
/// binds loopback, refuses a visitor with no token, and always stops — a CLI left blocked on an
/// abandoned workflow is the worst failure this feature can have.
/// </summary>
/// <remarks>
/// <see cref="DoNotParallelizeAttribute"/> because every test here starts a real Kestrel: run at
/// method level, eight web hosts come up at once and compete with the tests that compile whole
/// generated projects. On a two-core CI runner that is enough contention to push a start-and-stop
/// past twenty seconds — which is how these two waits went red on CI while passing every local
/// run. Same reasoning as <c>CommandColourTests</c>, one resource further out: there is one
/// machine, and starting web servers on it is not something to do eight ways at once.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class BuildAppWebHostTests
{
    /// <summary>
    /// How long <see cref="BuildAppWebHost.Run"/> is given to notice and return.
    /// </summary>
    /// <remarks>
    /// Generous on purpose. The assertion is "it returns rather than blocking forever", and it
    /// pays for a web host starting and stopping on whatever CPU is left over — a starved runner
    /// took CodeGenerator.Tests from five minutes to thirteen. A tighter number tests the runner's
    /// mood, not the code.
    /// </remarks>
    private static readonly TimeSpan ReturnsWithin = TimeSpan.FromSeconds(60);

    private static BuildSession NewSession()
        => new("a shop", Path.GetTempPath(), AppCustomization.Default);

    [TestMethod]
    public void Binds_loopback_only()
    {
        using var host = Start(out _);

        StringAssert.StartsWith(host.Url, "http://127.0.0.1:");
    }

    [TestMethod]
    public void Chooses_a_free_port_rather_than_a_fixed_one()
    {
        using var first = Start(out _);
        using var second = Start(out _);

        Assert.AreNotEqual(new Uri(first.Url).Port, new Uri(second.Url).Port);
    }

    [TestMethod]
    public async Task Serves_the_page_to_a_visitor_carrying_the_token()
    {
        using var host = Start(out _);
        using var client = new HttpClient();

        var response = await client.GetAsync(host.Url);

        Assert.IsTrue(response.IsSuccessStatusCode, $"expected 2xx, got {response.StatusCode}");
    }

    // A page whose script 404s has no interactivity at all, and the console would then block on
    // a workflow nobody can drive. Neither asset lives in the build output — both are served out
    // of the static-web-asset manifests — so this is exactly the wiring that breaks silently.
    [TestMethod]
    public async Task Serves_the_blazor_script_and_the_stylesheet()
    {
        using var host = Start(out _);
        using var client = new HttpClient();

        var root = new Uri(host.Url).GetLeftPart(UriPartial.Authority);

        foreach (var asset in new[] { "/_framework/blazor.web.js", "/workflow.css" })
        {
            var response = await client.GetAsync(root + asset);

            Assert.IsTrue(
                response.IsSuccessStatusCode, $"{asset} returned {response.StatusCode}");
        }
    }

    // Any process on this machine can reach a loopback port, and this one writes files and runs
    // `dotnet`. The token is what makes that safe.
    [TestMethod]
    public async Task Refuses_a_visitor_with_no_token()
    {
        using var host = Start(out _);
        using var client = new HttpClient();

        var bare = new Uri(host.Url).GetLeftPart(UriPartial.Path);
        var response = await client.GetAsync(bare);

        Assert.AreEqual(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Refuses_a_visitor_with_the_wrong_token()
    {
        using var host = Start(out _);
        using var client = new HttpClient();

        var bare = new Uri(host.Url).GetLeftPart(UriPartial.Path);
        var response = await client.GetAsync($"{bare}?t=not-the-token");

        Assert.AreEqual(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public void Run_returns_when_the_session_is_cancelled()
    {
        var session = NewSession();

        var task = Task.Run(() => BuildAppWebHost.Run(session, CancellationToken.None));

        session.Cancel();

        Assert.IsTrue(task.Wait(ReturnsWithin), "Run did not return on cancel");
        Assert.AreEqual(SessionPhase.Cancelled, task.Result.Phase);
    }

    [TestMethod]
    public void Run_returns_when_the_console_is_interrupted()
    {
        var session = NewSession();
        using var interrupt = new CancellationTokenSource();

        var task = Task.Run(() => BuildAppWebHost.Run(session, interrupt.Token));

        interrupt.Cancel();

        Assert.IsTrue(task.Wait(ReturnsWithin), "Run did not return on interrupt");
    }

    /// <summary>
    /// The failure this feature can least afford: a tab closed and a console blocked forever.
    /// </summary>
    /// <remarks>
    /// Drives the watch directly rather than through a browser. What a real circuit does to the
    /// watch is <c>WorkflowCircuitsTests</c>'s job; what the host does about it is this one's.
    /// </remarks>
    [TestMethod]
    public void Run_returns_when_the_last_tab_is_closed()
    {
        using var host = Start(out var session);

        host.Circuits.Opened(DateTimeOffset.UtcNow);
        host.Circuits.Closed(DateTimeOffset.UtcNow);

        var task = Task.Run(
            () => host.Run(session, CancellationToken.None, TimeSpan.Zero));

        Assert.IsTrue(task.Wait(ReturnsWithin), "Run did not return on an abandoned workflow");
        Assert.AreEqual(SessionPhase.Abandoned, task.Result.Phase);
    }

    /// <summary>
    /// The counterpart, and the one a too-eager watch would break: nobody has opened the page
    /// yet, and the URL is still worth pasting.
    /// </summary>
    [TestMethod]
    public void Run_keeps_waiting_when_the_page_has_not_been_opened_yet()
    {
        using var host = Start(out var session);

        var task = Task.Run(() => host.Run(session, CancellationToken.None, TimeSpan.Zero));

        Assert.IsFalse(
            task.Wait(TimeSpan.FromSeconds(2)), "Run gave up before the page was ever opened");

        session.Cancel();
        task.Wait(ReturnsWithin);
    }

    private static BuildAppWebHost Start(out BuildSession session)
    {
        session = NewSession();

        Assert.IsTrue(
            BuildAppWebHost.TryStart(session, out var host), "the host could not bind loopback");

        return host;
    }
}
