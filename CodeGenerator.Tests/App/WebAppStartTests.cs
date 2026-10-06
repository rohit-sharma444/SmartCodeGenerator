using System.Net;
using System.Net.Sockets;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Waiting for a generated web application to come up, and opening it.
/// </summary>
/// <remarks>
/// The gap this closes was reported as "the web application is not running": it was running, but
/// <c>dotnet run</c> rebuilds before Kestrel binds, so the address was printed seconds before
/// anything answered on it — and a web app, alone among the targets, puts nothing on screen of its
/// own. Waiting is therefore the whole feature, and the wait is what these tests pin down.
/// </remarks>
/// <remarks>
/// <para>
/// <c>DoNotParallelize</c> for one test in here, and it is not a timing tolerance:
/// <c>IsListening_IsTrueOnlyWhileSomethingHoldsThePort</c> asks the OS for an ephemeral port,
/// releases it, and then asserts nothing answers on it. This assembly also starts Kestrels — the
/// build-workflow host tests — and they ask for ephemeral ports too, so a neighbour can be handed
/// the very port this test just gave back and the second assertion fails on a port that is
/// genuinely listening, for someone else. Reproduced in a full Release run and green on its own
/// nine times over; the precedent is the SpinnerTests fix, which was the same lesson about a test
/// that cannot tolerate neighbours.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public class WebAppStartTests
{
    [TestMethod]
    public void WaitUntilReady_StopsAtTheFirstSuccessfulProbe()
    {
        var probes = 0;
        var waits = 0;

        var ready = WebAppStart.WaitUntilReady(
            probe: () => ++probes == 3,
            attempts: 10,
            wait: () => waits++);

        Assert.IsTrue(ready);
        Assert.AreEqual(3, probes);

        // Waited only between attempts, never after the one that succeeded: an app that is already
        // listening must hand the prompt back without a pause the user can see.
        Assert.AreEqual(2, waits);
    }

    [TestMethod]
    public void WaitUntilReady_GivesUpOnceTheAttemptsRunOut()
    {
        var probes = 0;
        var waits = 0;

        var ready = WebAppStart.WaitUntilReady(
            probe: () => { probes++; return false; },
            attempts: 4,
            wait: () => waits++);

        // Bounded, and it says so rather than blocking the CLI on an app that will never answer.
        Assert.IsFalse(ready);
        Assert.AreEqual(4, probes);

        // Three gaps between four looks. The fourth wait would be spent on an answer already
        // decided.
        Assert.AreEqual(3, waits);
    }

    [TestMethod]
    public void WaitUntilReady_ProbesOnce_WhenAskedForNoAttemptsAtAll()
    {
        var probes = 0;

        // A zero or negative timeout is still one look: "is it up?" always has an answer, and the
        // alternative is reporting a failure nobody checked for.
        Assert.IsFalse(WebAppStart.WaitUntilReady(() => { probes++; return false; }, 0, () => { }));
        Assert.AreEqual(1, probes);
    }

    [TestMethod]
    public void WaitUntilReady_StopsWhenTheUserCancels()
    {
        var probes = 0;
        var cancelAfter = 2;

        var ready = WebAppStart.WaitUntilReady(
            probe: () => { probes++; return false; },
            attempts: 100,
            wait: () => { },
            cancelled: () => probes >= cancelAfter);

        // Ctrl-C is the way out of everything else the CLI does, and a minute of waiting is the
        // last place to make it the exception.
        Assert.IsFalse(ready);
        Assert.AreEqual(2, probes);
    }

    [TestMethod]
    public void Attempts_CoversTheTimeoutAtThePollInterval()
    {
        Assert.AreEqual(120, WebAppStart.Attempts(TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(500)));
        Assert.AreEqual(4, WebAppStart.Attempts(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(500)));

        // Never zero: see WaitUntilReady_ProbesOnce_WhenAskedForNoAttemptsAtAll.
        Assert.AreEqual(1, WebAppStart.Attempts(TimeSpan.Zero, TimeSpan.FromMilliseconds(500)));
        Assert.AreEqual(1, WebAppStart.Attempts(TimeSpan.FromSeconds(-5), TimeSpan.FromMilliseconds(500)));
    }

    [TestMethod]
    public void IsListening_IsTrueOnlyWhileSomethingHoldsThePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var url = $"http://localhost:{port}";

        try
        {
            Assert.IsTrue(WebAppStart.IsListening(url), "a bound port reads as listening");
        }
        finally
        {
            listener.Stop();
        }

        // And the same address stops being ready the moment nothing is behind it, which is the
        // state every wait starts in.
        Assert.IsFalse(WebAppStart.IsListening(url));
    }

    [TestMethod]
    public void IsListening_IsFalseForAnythingThatIsNotAnAddressWeCanReach()
    {
        Assert.IsFalse(WebAppStart.IsListening(""));
        Assert.IsFalse(WebAppStart.IsListening("not a url"));
        Assert.IsFalse(WebAppStart.IsListening(@"C:\Windows\System32\calc.exe"));
    }

    [TestMethod]
    public void Browsable_AcceptsOnlyAnAbsoluteHttpAddress()
    {
        Assert.IsTrue(WebAppStart.Browsable("http://localhost:5056"));
        Assert.IsTrue(WebAppStart.Browsable("https://localhost:7069/"));

        // The address is read out of launchSettings.json inside the generated project, so it is a
        // file on disk that decides what the CLI hands to the shell. Anything but http(s) is
        // refused rather than shell-executed.
        Assert.IsFalse(WebAppStart.Browsable(@"C:\Windows\System32\calc.exe"));
        Assert.IsFalse(WebAppStart.Browsable("file:///C:/Windows/System32/calc.exe"));
        Assert.IsFalse(WebAppStart.Browsable("javascript:alert(1)"));
        Assert.IsFalse(WebAppStart.Browsable("/book"));
        Assert.IsFalse(WebAppStart.Browsable(""));
    }

    [TestMethod]
    public void OpenBrowser_RefusesWhatBrowsableRejects_WithoutStartingAnything()
    {
        // No process is started, so this is safe to run in a suite: the guard is the assertion.
        Assert.IsFalse(WebAppStart.OpenBrowser(@"C:\Windows\System32\calc.exe"));
        Assert.IsFalse(WebAppStart.OpenBrowser("file:///C:/Windows/System32/calc.exe"));
        Assert.IsFalse(WebAppStart.OpenBrowser(""));
    }

    [TestMethod]
    public void BrowserOptedOut_ReadsCgNoBrowserLikeCgNoWeb()
    {
        // CG_NO_BROWSER=1 keeps the page but opens no tab: the URL is printed instead.
        Assert.IsTrue(WebAppStart.BrowserOptedOut("1"));
        Assert.IsFalse(WebAppStart.BrowserOptedOut(null));
        Assert.IsFalse(WebAppStart.BrowserOptedOut(""));
        Assert.IsFalse(WebAppStart.BrowserOptedOut("0"));
    }

    [TestMethod]
    public void NotOpenedLine_SaysWhyTheTabDidNotOpen()
    {
        StringAssert.Contains(WebAppStart.NotOpenedLine(optedOut: true), "CG_NO_BROWSER");
        StringAssert.Contains(WebAppStart.NotOpenedLine(optedOut: true), "open that address yourself");
        StringAssert.Contains(WebAppStart.NotOpenedLine(optedOut: false), "No default browser");
    }
}
