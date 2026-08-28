using System.Net;
using CodeGenerator.App;
using CodeGenerator.Web.Chat;
using CodeIntelligence;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// The chat host, without a browser.
/// </summary>
/// <remarks>
/// The two properties that matter are the same two the build host has: the route is not served to
/// a visitor without the token, and the wait always ends. A CLI blocked forever on a page nobody
/// has open is the worst failure this feature can have.
/// <para>
/// <see cref="DoNotParallelizeAttribute"/>: every test here starts a real Kestrel.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public class ChatWebHostTests
{
    private static readonly TimeSpan ReturnsWithin = TimeSpan.FromSeconds(60);

    [TestMethod]
    public async Task Refuses_a_visitor_with_no_token()
    {
        using var host = Start(out _);
        using var client = new HttpClient();

        var response = await client.GetAsync(
            new Uri(host.Url).GetLeftPart(UriPartial.Authority) + "/chat");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Serves_the_page_to_a_visitor_carrying_the_token()
    {
        using var host = Start(out _);
        using var client = new HttpClient();

        var response = await client.GetAsync(host.Url);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The page must be served as an interactive server component, not statically rendered.
    /// </summary>
    /// <remarks>
    /// A page with no <c>@rendermode</c> draws its whole DOM and wires none of it: the strip, the
    /// picker and the composer all look right, every button is inert, and nothing fails. No unit
    /// test can see it either, because bUnit renders a component interactively by construction.
    /// This was found by opening the page and clicking Load.
    /// <para>
    /// Asserted on the body rather than the whole document, because App.razor renders its
    /// HeadOutlet interactively whatever the page does, and its marker sits in the head.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task Serves_the_page_as_an_interactive_component()
    {
        using var host = Start(out _);
        using var client = new HttpClient();

        var html = await client.GetStringAsync(host.Url);
        var body = html[html.IndexOf("<body>", StringComparison.Ordinal)..];

        StringAssert.Contains(
            body,
            "\"type\":\"server\"",
            "the chat page is statically rendered: it draws, and none of it responds");
    }

    [TestMethod]
    public void The_session_is_a_singleton_so_a_second_tab_joins_it()
    {
        using var host = Start(out var session);

        Assert.AreSame(session, host.Services.GetRequiredService<ChatSession>());
    }

    [TestMethod]
    public void Returns_when_the_session_ends()
    {
        using var host = Start(out var session);

        var wait = Task.Run(() => host.Run(session, CancellationToken.None));

        session.End();

        Assert.IsTrue(wait.Wait(ReturnsWithin), "the wait must end when the session does");
    }

    [TestMethod]
    public void Returns_when_cancelled()
    {
        using var host = Start(out var session);
        using var cancellation = new CancellationTokenSource();

        var wait = Task.Run(() => host.Run(session, cancellation.Token));

        cancellation.Cancel();

        Assert.IsTrue(wait.Wait(ReturnsWithin), "Ctrl-C must end the wait");
        Assert.IsTrue(session.Ended, "and it must end the session, not just the wait");
    }

    [TestMethod]
    public void Returns_when_every_tab_has_been_gone_for_the_grace_period()
    {
        using var host = Start(out var session);

        // A tab arrived and left. With a zero grace the next poll treats it as abandoned.
        host.Circuits.Opened(DateTimeOffset.UtcNow);
        host.Circuits.Closed(DateTimeOffset.UtcNow);

        var wait = Task.Run(() => host.Run(session, CancellationToken.None, TimeSpan.Zero));

        Assert.IsTrue(wait.Wait(ReturnsWithin), "an abandoned page must end the command");
        Assert.IsTrue(session.Ended);
    }

    /// <summary>
    /// The validation page writes source files, and loopback is reachable by every process on this
    /// machine. It has to be behind the same token as the chat page - which is why it is served
    /// under /chat rather than at /validation.
    /// </summary>
    [TestMethod]
    public async Task Refuses_the_validation_page_to_a_visitor_with_no_token()
    {
        using var host = Start(out _);
        using var client = new HttpClient();

        var response = await client.GetAsync(
            new Uri(host.Url).GetLeftPart(UriPartial.Authority) + "/chat/validation");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Serves_the_validation_page_to_a_visitor_carrying_the_token()
    {
        using var host = Start(out _);
        using var client = new HttpClient();

        var url = new Uri(host.Url);
        var response = await client.GetAsync(
            url.GetLeftPart(UriPartial.Authority) + "/chat/validation" + url.Query);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Serves_the_validation_page_as_an_interactive_component()
    {
        using var host = Start(out _);
        using var client = new HttpClient();

        var url = new Uri(host.Url);
        var html = await client.GetStringAsync(
            url.GetLeftPart(UriPartial.Authority) + "/chat/validation" + url.Query);

        var body = html[html.IndexOf("<body>", StringComparison.Ordinal)..];

        StringAssert.Contains(
            body,
            "\"type\":\"server\"",
            "the validation page is statically rendered: the box draws and the button does nothing");
    }

    private static ChatWebHost Start(out ChatSession session)
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        session = new ChatSession(
            new Workspace(root, projectPathSet: false), AiLayer.Create(), []);

        Assert.IsTrue(ChatWebHost.TryStart(session, out var host));

        return host;
    }
}
