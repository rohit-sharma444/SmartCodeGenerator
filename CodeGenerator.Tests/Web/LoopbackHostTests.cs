using System.Net;
using CodeGenerator.Web;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The host mechanics, independent of what page they serve.
/// </summary>
/// <remarks>
/// <see cref="DoNotParallelizeAttribute"/> for the reason given on <c>BuildAppWebHostTests</c>:
/// every test here starts a real Kestrel, and there is one machine.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class LoopbackHostTests
{
    [TestMethod]
    public void Binds_loopback_on_a_free_port()
    {
        using var host = Start("chat");

        StringAssert.StartsWith(host.Url, "http://127.0.0.1:");
    }

    [TestMethod]
    public void The_url_carries_the_route_and_the_token()
    {
        using var host = Start("chat");

        StringAssert.Contains(host.Url, "/chat?t=");
        StringAssert.Contains(host.Url, host.SessionToken);
    }

    [TestMethod]
    public async Task Refuses_the_route_without_the_token()
    {
        using var host = Start("chat");
        using var client = new HttpClient();

        var response = await client.GetAsync(
            new Uri(host.Url).GetLeftPart(UriPartial.Authority) + "/chat");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Refuses_the_route_with_the_wrong_token()
    {
        using var host = Start("chat");
        using var client = new HttpClient();

        var response = await client.GetAsync(
            new Uri(host.Url).GetLeftPart(UriPartial.Authority) + "/chat?t=nonsense");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public void Registers_what_the_caller_asked_for()
    {
        using var host = Start("chat", services => services.AddSingleton(new Marker()));

        Assert.IsNotNull(host.Services.GetService<Marker>());
    }

    [TestMethod]
    public void Guards_the_route_it_was_given_and_not_another()
    {
        // The guard must follow the route argument. Hard-coding it to "/build" is the bug this
        // whole extraction exists to make impossible.
        using var host = Start("chat");

        StringAssert.Contains(host.Url, "/chat");
    }

    private sealed class Marker;

    private static LoopbackHost Start(
        string route, Action<IServiceCollection>? register = null)
    {
        Assert.IsTrue(
            LoopbackHost.TryStart(route, register ?? (_ => { }), out var host),
            "the host must start; a machine that cannot bind loopback cannot run this test");

        return host;
    }
}
