using System.Text.RegularExpressions;

namespace CodeGenerator.App.Scripts;

/// <summary>
/// <c>run &lt;file&gt;</c>: replays a script, all or nothing.
/// </summary>
internal static partial class RunCommand
{
    // Anchored on the whole word: "running total in Order" is a sentence for the pipeline.
    [GeneratedRegex(@"^\s*run(?:\s+(?<file>.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Shape();

    public static bool IsRun(string? input) => Shape().IsMatch(input ?? string.Empty);

    /// <summary>The file named after <c>run</c>, trimmed, or empty.</summary>
    public static string FileArgument(string input) => Shape().Match(input).Groups["file"].Value.Trim();
}
