using CodeGenerationEngine.Helper;
using CodeGenerator.App.Relationships;
using CodeGenerator.Web.Relationships;
using CodeGenerator.Web.Workflow;

namespace CodeGenerator.App;

/// <summary>
/// <c>show relationships</c> — the classes in the workspace and what each is related to.
/// </summary>
/// <remarks>
/// A relationship is a pair, an <c>int &lt;Target&gt;Id</c> and a property typed as the target, and
/// reading a set of them out of a directory of files is exactly the job a picture does better than
/// a list. So this opens a page, the way <c>chat</c> does.
/// <para>
/// Unlike <c>chat</c>, it does not refuse when there is no browser. A conversation with no one to
/// type into it has no non-visual form; a set of relationships does, so a piped or scripted run
/// gets the same answer written out as lines. Refusing there would withhold something the tool can
/// perfectly well say.
/// </para>
/// </remarks>
internal sealed class ShowRelationshipsCommand
{
    internal static bool IsShowRelationships(string? input)
    {
        var text = (input ?? string.Empty).Trim().TrimEnd('.', '?', '!');

        // The spellings people actually type. Not a parser: anything more elaborate belongs to the
        // natural-language path, which reaches this command through the catalog.
        return text.Equals("show relationships", StringComparison.OrdinalIgnoreCase)
            || text.Equals("show relationship", StringComparison.OrdinalIgnoreCase)
            || text.Equals("list relationships", StringComparison.OrdinalIgnoreCase)
            || text.Equals("relationships", StringComparison.OrdinalIgnoreCase);
    }

    internal void Handle(string workspaceRoot)
    {
        var view = new RelationshipView(workspaceRoot, WorkspaceRelationships.Of(workspaceRoot));

        if (view.Sources.Count == 0)
        {
            Output.Warning("No classes in this workspace yet, so there is nothing to relate.");
            return;
        }

        if (!ChatCommand.UseWeb(
                Console.IsInputRedirected,
                Environment.GetEnvironmentVariable(ChatCommand.NoWebVariable) == "1"))
        {
            Report(view);
            return;
        }

        if (!RelationshipWebHost.TryStart(view, out var host))
        {
            // Could not bind loopback. The answer still exists; say it here rather than lose it.
            Output.Warning("Could not open the page, so here it is as text.");
            Report(view);
            return;
        }

        using (host)
        {
            Console.WriteLine($"  Opening the relationships at {host.Url}");

            if (!WebAppStart.OpenBrowser(host.Url))
                Console.WriteLine($"  {WebAppStart.NotOpenedLine()}");

            Console.WriteLine("  Close the tab, or Ctrl-C here, to come back.");
            Console.WriteLine("");

            host.Run(Interrupt.Token);
        }
    }

    /// <summary>
    /// The same answer, in lines.
    /// </summary>
    /// <remarks>
    /// The classes related to nothing are listed too, and are the reason this is worth printing at
    /// all: an app whose classes are mostly unjoined looks fine one file at a time.
    /// </remarks>
    internal static IReadOnlyList<string> Lines(RelationshipView view)
    {
        var lines = new List<string>();

        foreach (var source in view.Sources.Where(s => s.Targets.Count > 0))
            lines.Add($"  {source.Name} -> {string.Join(", ", source.Targets)}");

        var alone = view.Sources.Where(s => s.Targets.Count == 0)
            .Select(s => s.Name)
            .Where(name => !view.Sources.Any(s => s.Targets.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        if (lines.Count == 0)
            lines.Add("  Nothing is related to anything yet.");

        if (alone.Count > 0)
            lines.Add($"  Related to nothing: {string.Join(", ", alone)}");

        return lines;
    }

    private static void Report(RelationshipView view)
    {
        foreach (var line in Lines(view))
            Output.Line(line);
    }
}
