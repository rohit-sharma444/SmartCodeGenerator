using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace CodeGenerator.App;

/// <summary>
/// Waits for a generated web application to start listening, and opens it in a browser.
/// </summary>
/// <remarks>
/// A web application is the one target that puts nothing on screen of its own: a desktop app shows
/// a window and a console app shows a console, while this starts, prints to a window of its own and
/// waits to be visited. Printing the address instead of opening it was worse than it looks —
/// <c>dotnet run</c> restores and builds before Kestrel binds, so for the first several seconds
/// there is nothing on the other end of the address, and clicking it in that window produces a
/// browser that cannot connect. It reads exactly like an app that did not run.
/// <para>
/// So: wait until something is actually listening, and only then open it. The wait is bounded — a
/// generated app that never comes up must leave the user with a prompt and an explanation, not a
/// hung CLI.
/// </para>
/// </remarks>
internal static class WebAppStart
{
    /// <summary>How long to wait for the app before giving up on it.</summary>
    /// <remarks>
    /// Generous on purpose: this is a first <c>dotnet run</c>, which restores and builds before it
    /// binds anything, on a machine that has just finished a build of the same project.
    /// </remarks>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>How long between looks.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>How long to give a single connection attempt.</summary>
    /// <remarks>
    /// Loopback either answers immediately or refuses immediately. The only case this bounds is a
    /// hostname that resolves to something unreachable, where a connect would otherwise sit for the
    /// OS default of twenty-odd seconds and swallow the whole wait in one attempt.
    /// </remarks>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Waits for <paramref name="url"/> to start answering. True if it did.
    /// </summary>
    public static bool Wait(string url)
        => WaitUntilReady(
            () => IsListening(url),
            Attempts(Timeout, PollInterval),
            () => Thread.Sleep(PollInterval),
            () => Interrupt.Requested);

    /// <summary>
    /// Probes until <paramref name="probe"/> succeeds or <paramref name="attempts"/> run out.
    /// </summary>
    /// <remarks>
    /// The sleep arrives as a delegate so the giving-up case can be tested without a test that
    /// really waits a minute to prove it stops waiting.
    /// </remarks>
    /// <param name="cancelled">
    /// Asked before every probe. A minute is a long time to be unable to leave, and Ctrl-C is the
    /// established way out of everything else here; null means nothing can cancel.
    /// </param>
    internal static bool WaitUntilReady(
        Func<bool> probe, int attempts, Action wait, Func<bool>? cancelled = null)
    {
        // Always at least one look: "is it up?" has an answer whatever the caller asked for, and
        // reporting a failure nobody checked for would be a lie about the app.
        var count = Math.Max(1, attempts);

        for (var i = 0; i < count; i++)
        {
            if (cancelled?.Invoke() == true)
                return false;

            if (probe())
                return true;

            // Between probes only. A wait after the last one is time the user spends staring at a
            // spinner for an answer that has already been decided.
            if (i < count - 1)
                wait();
        }

        return false;
    }

    /// <summary>
    /// How many probes cover <paramref name="timeout"/> at <paramref name="interval"/>, never
    /// fewer than one.
    /// </summary>
    internal static int Attempts(TimeSpan timeout, TimeSpan interval)
    {
        if (timeout <= TimeSpan.Zero || interval <= TimeSpan.Zero)
            return 1;

        return Math.Max(1, (int)(timeout.Ticks / interval.Ticks));
    }

    /// <summary>
    /// Whether something is accepting connections at <paramref name="url"/>.
    /// </summary>
    /// <remarks>
    /// A TCP connect rather than a request: a bound port is exactly the condition for the browser
    /// to work, and it is reached before the app has rendered anything — which is the point, since
    /// the first request should be the user's own, seeding and all.
    /// </remarks>
    internal static bool IsListening(string url)
    {
        if (!Browsable(url) || !Uri.TryCreate(url, UriKind.Absolute, out var address))
            return false;

        IPAddress[] hosts;

        try
        {
            hosts = Dns.GetHostAddresses(address.Host);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            return false;
        }

        // Every address the name resolves to, not just the first. `localhost` is both ::1 and
        // 127.0.0.1 on Windows and the v6 one is tried first, so stopping there would report a
        // listening app as absent whenever it bound v4 only — which is a wait that gives up on an
        // app that is sitting there answering.
        return hosts.Any(host => CanConnect(host, address.Port));
    }

    private static bool CanConnect(IPAddress host, int port)
    {
        try
        {
            using var socket = new Socket(host.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            return socket.ConnectAsync(host, port).Wait(ConnectTimeout) && socket.Connected;
        }
        catch (Exception ex) when (ex is SocketException or AggregateException
                                       or ArgumentException or ObjectDisposedException
                                       or NotSupportedException)
        {
            // Refused, unreachable, or an address family this machine has no stack for.
            return false;
        }
    }

    /// <summary>
    /// Opens <paramref name="url"/> in the user's browser. True if it was handed over.
    /// </summary>
    public static bool OpenBrowser(string url)
    {
        if (!Browsable(url))
            return false;

        try
        {
            // UseShellExecute is what picks the default browser: this is a URL, not a program, and
            // there is nothing to redirect or wait for.
            using var opened = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException
                                       or System.IO.FileNotFoundException)
        {
            // No default browser, or the shell refused. The caller still prints the address.
            return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="url"/> is something we are willing to hand to the shell.
    /// </summary>
    /// <remarks>
    /// The address comes from <c>launchSettings.json</c> inside the generated project — a file on
    /// disk, which anyone may have edited. Shell-executing whatever it says would turn that file
    /// into a way to run a program, so only an absolute http(s) address is ever passed on.
    /// </remarks>
    internal static bool Browsable(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var address)
           && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps);
}
