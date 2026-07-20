namespace CodeGenerator.App;

/// <summary>
/// Recognizes the interactive screen-clear command (`clear`, alias `cls`).
/// Clearing affects only the display; command history is never touched.
/// </summary>
internal static class ClearCommand
{
    public static bool IsClear(string input)
    {
        var trimmed = input.Trim();
        return trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("cls", StringComparison.OrdinalIgnoreCase);
    }
}
