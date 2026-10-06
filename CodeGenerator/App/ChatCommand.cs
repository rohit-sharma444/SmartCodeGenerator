using CodeGenerationEngine.Helper;
using CodeGenerator.Web.Chat;
using CodeIntelligence;

namespace CodeGenerator.App;

/// <summary>
/// Opens the chat page and waits for it.
/// </summary>
/// <remarks>
/// The same shape as the interactive branch of <c>build app</c>: start a loopback host, print the
/// address, open a browser if there is one, and block until the session ends. Everything the page
/// does happens in this process against the same objects the console path uses, so there is
/// nothing to serialize and nothing to marshal back.
/// </remarks>
internal sealed class ChatCommand
{
    /// <summary>Set to 1 to keep the console. Same variable `build app` honours.</summary>
    internal const string NoWebVariable = "CG_NO_WEB";

    internal static bool IsChat(string? input)
    {
        var text = (input ?? string.Empty).Trim();

        return text.Equals("chat", StringComparison.OrdinalIgnoreCase)
            || text.Equals("ui", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether this run can open the page.
    /// </summary>
    /// <remarks>
    /// Pure, and taking its inputs as arguments, for the reason
    /// <c>BuildAppCommand.UseWebWorkflow</c> is: the branch it governs reads the console and
    /// cannot be driven from a test.
    /// <para>
    /// Redirected stdin is the boundary. A piped run has no browser to open and no person to type
    /// into it, and the console prompt it already has is the better answer.
    /// </para>
    /// </remarks>
    internal static bool UseWeb(bool inputRedirected, bool noWeb)
        => !inputRedirected && !noWeb;

    internal void Handle(Workspace workspace, AiLayer ai, List<string> history)
    {
        if (!UseWeb(
                Console.IsInputRedirected,
                Environment.GetEnvironmentVariable(NoWebVariable) == "1"))
        {
            Output.Warning(
                "`chat` opens a page in a browser, which this run has no way to reach. "
                + "The console prompt does everything the page does.");
            return;
        }

        var session = new ChatSession(workspace, ai, history);

        if (!ChatWebHost.TryStart(session, out var host))
        {
            // Could not bind loopback. Say so; the prompt the user already has still works.
            Output.Warning("Could not start the chat page. The console prompt is unaffected.");
            return;
        }

        using (host)
        {
            Console.WriteLine($"  Opening the chat at {host.Url}");

            if (!WebAppStart.OpenBrowser(host.Url))
                Console.WriteLine($"  {WebAppStart.NotOpenedLine()}");

            Console.WriteLine("  Ctrl-C here ends the session.");
            Console.WriteLine("");

            host.Run(session, Interrupt.Token);

            Console.WriteLine("  The chat session ended.");
            Console.WriteLine("");
        }
    }
}
