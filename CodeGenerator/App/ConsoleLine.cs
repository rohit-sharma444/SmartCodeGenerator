namespace CodeGenerator.App;

/// <summary>
/// The one clipping rule for a line that must stay within a console row, shared by every caller
/// that paints one — <see cref="CommandLineEditor.FormatSuggestionLine"/> and
/// <see cref="SelectionPrompt"/> both need it, and a second, drifting copy of the same rule is
/// exactly the bug class this branch already fixed once.
/// </summary>
internal static class ConsoleLine
{
    /// <summary>
    /// Clips <paramref name="text"/> to fit a console <paramref name="width"/> columns wide,
    /// stopping one column short (so the last column is never written) and marking a real cut
    /// with a trailing <c>...</c> when there is room for it.
    /// </summary>
    internal static string Clip(string text, int width)
    {
        var limit = width - 1;

        if (limit <= 0)
            return string.Empty;

        if (text.Length <= limit)
            return text;

        const string ellipsis = "...";

        return limit <= ellipsis.Length
            ? text[..limit]
            : text[..(limit - ellipsis.Length)] + ellipsis;
    }
}
