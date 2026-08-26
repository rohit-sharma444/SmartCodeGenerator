namespace CodeGenerator.App.Relationships;

/// <summary>Asks on the console, through the menu every other question in this tool uses.</summary>
/// <remarks>
/// <c>Menu.Choose</c> already picks an arrow-key list on a real console and a numbered list when
/// input or output is redirected, so nothing here needs to know which it is talking to.
/// </remarks>
internal sealed class ConsoleChoiceAsker : IChoiceAsker
{
    public int? Choose(string title, IReadOnlyList<string> options, int defaultIndex)
        => Menu.Choose(title, options, defaultIndex);
}

/// <summary>Whether there is a conversation to be had at all.</summary>
/// <remarks>
/// One place, so the console and the chat page cannot disagree about it, and so the piped case is
/// testable without a console: no asker, no flow. That is also how every existing scripted test
/// stays green without being edited — with no flow there is no menu, so a script's next line is
/// never swallowed as an answer to a question it could not see.
/// </remarks>
public static class RelationshipGate
{
    private static readonly string[] CreationVerbs =
        ["create", "add", "make", "new", "generate"];

    private static readonly string[] Articles = ["a", "an", "the"];

    public static RelationshipFlow? For(
        Func<string> workspaceRoot, IChoiceAsker? asker, Action<string> report)
        => asker is null ? null : new RelationshipFlow(workspaceRoot, asker, report);

    /// <summary>
    /// Whether the input was a class creation, which the offer stays out of so that creating
    /// several classes in a row is not interrupted.
    /// </summary>
    /// <remarks>
    /// A shape test on the words, not a reading of the parsed intent: the intent that survives
    /// into <c>PipelineOutcome</c> is the natural-language engine's own
    /// (<c>AddIntent</c>, <c>DescribeIntent</c>), not the <c>IntentType</c> the command was
    /// finally routed to, and <c>ParsedCommand</c> never leaves <c>CommandFront</c>. Getting this
    /// wrong costs an offer the user sees one command later than they might have, never a
    /// relationship written or lost, which is what makes a heuristic acceptable here.
    /// <para>
    /// The verb and the noun both have to match. Matching a creation verb anywhere alongside the
    /// word "class" would also swallow <c>add property CustomerId int to class Order</c> — the one
    /// command most likely to be the moment a relationship becomes detectable.
    /// </para>
    /// </remarks>
    public static bool IsClassCreation(string input)
    {
        var words = (input ?? string.Empty)
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

        if (words.Length < 2 || !CreationVerbs.Contains(words[0], StringComparer.OrdinalIgnoreCase))
            return false;

        var noun = Articles.Contains(words[1], StringComparer.OrdinalIgnoreCase) && words.Length > 2
            ? words[2]
            : words[1];

        return string.Equals(noun, "class", StringComparison.OrdinalIgnoreCase)
               || string.Equals(noun, "classes", StringComparison.OrdinalIgnoreCase);
    }
}
