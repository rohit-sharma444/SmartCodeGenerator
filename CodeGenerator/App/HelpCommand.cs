using CodeGenerationEngine.Core;

namespace CodeGenerator.App;

/// <summary>
/// Recognizes <c>help</c>, <c>help &lt;topic&gt;</c> and <c>help all</c>, and renders the answer.
/// </summary>
/// <remarks>
/// Handled here, ahead of the recognition pipeline, for the reason <c>clear</c>, <c>history</c>
/// and <c>ai</c> are: <c>LiteralCommandTable</c> keys on the exact word "help", so a line with a
/// topic on it matches nothing there and falls through to the NLP path, arriving mangled or not
/// at all. Intercepting keeps the text exactly as typed.
/// </remarks>
internal static class HelpCommand
{
    private const string Command = "help";

    /// <summary>What <c>help all</c> asks for: the complete catalog.</summary>
    private const string Everything = "all";

    /// <summary>
    /// Whether <paramref name="input"/> is a help line.
    /// </summary>
    /// <remarks>
    /// Deliberately not a bare prefix test — "helper" and "helpful commands" both start with
    /// "help". The character after the word must be whitespace, or nothing. The same trap
    /// <c>BuildAppCommand.IsBuildApp</c> documents.
    /// </remarks>
    public static bool IsHelp(string? input)
    {
        var text = (input ?? string.Empty).Trim();

        if (!text.StartsWith(Command, StringComparison.OrdinalIgnoreCase))
            return false;

        return text.Length == Command.Length || char.IsWhiteSpace(text[Command.Length]);
    }

    /// <summary>Everything after the word, or empty for a bare <c>help</c>.</summary>
    public static string Topic(string? input)
    {
        var text = (input ?? string.Empty).Trim();

        return text.Length <= Command.Length
            ? string.Empty
            : text[Command.Length..].Trim();
    }

    /// <summary>What to print.</summary>
    public static string Render(string? input)
    {
        var topic = Topic(input);

        if (topic.Length == 0)
            return CommandDictionary.GetOverview();

        if (string.Equals(topic, Everything, StringComparison.OrdinalIgnoreCase))
            return CommandDictionary.GetHelpText();

        return CommandDictionary.GetTopicHelp(topic) ?? Unknown(topic);
    }

    /// <summary>
    /// What to say when the topic names neither a category nor a command.
    /// </summary>
    /// <remarks>
    /// Names the topic back, so a typo is visible, and lists what could have been asked for.
    /// Printing nothing, or the whole catalog, would both leave the user no better off than the
    /// wall of text this change exists to remove.
    /// </remarks>
    private static string Unknown(string topic)
    {
        var lines = new List<string>
        {
            $"No help for '{topic}'. Try one of:",
            string.Empty,
        };

        lines.AddRange(CommandDictionary.Categories.Select(c => "    " + c));

        lines.Add(string.Empty);
        lines.Add("    all    every command in one list");

        return string.Join(Environment.NewLine, lines);
    }
}
