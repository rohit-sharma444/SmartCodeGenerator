using System.Text.RegularExpressions;

namespace CodeGenerator.App.Scripts;

/// <summary>
/// <c>save session &lt;file&gt; [replace]</c>: writes this session's commands as a script.
/// </summary>
internal static partial class SaveSessionCommand
{
    [GeneratedRegex(@"^\s*save\s+session(?:\s+(?<rest>.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Shape();

    public static bool IsSaveSession(string? input) => Shape().IsMatch(input ?? string.Empty);
}
