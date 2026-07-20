namespace CodeGenerator.App;

/// <summary>
/// Recognizes the interactive <c>history</c> command, which lists recent commands.
/// Like <see cref="ClearCommand"/> it is a transient display command and is not
/// itself recorded in history.
/// </summary>
internal static class HistoryCommand
{
    public static bool IsHistory(string input)
        => input.Trim().Equals("history", StringComparison.OrdinalIgnoreCase);
}
