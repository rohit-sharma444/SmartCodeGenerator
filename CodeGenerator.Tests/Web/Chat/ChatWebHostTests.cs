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
