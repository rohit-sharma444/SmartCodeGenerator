namespace CodeGenerator.App;

/// <summary>
/// The full system report, printed by <c>status</c>.
/// </summary>
/// <remarks>
/// Startup used to print all of this every time, which pushed the prompt and the command the
/// banner had just recommended off the top of the screen — and, worse, trained the eye to skim
/// the block that also carries the store's warnings. Startup is quiet when all is well; this is
/// where the detail moved to, so going quiet did not mean losing it.
/// <para>
/// Pure, and given the store lines rather than rebuilding them, so this and the startup warning
/// cannot disagree about what the store is doing.
/// </para>
/// </remarks>
internal static class StatusReport
{
    private const int LabelWidth = -12;

    public static IReadOnlyList<string> Lines(
        string workspaceRoot,
        int projectCount,
        string? activeProject,
        int ruleCount,
        IReadOnlyList<string> storeLines,
        string aiState,
        string version)
    {
        var lines = new List<string>
        {
            "System state",
            string.Empty,

            // First line of the report proper: the first thing anyone needs when describing a
            // problem to someone else.
            Row("Version", version),

            // First, because files landing somewhere unexpected — PROJECTPATH set and forgotten —
            // is the most common confusion this tool produces.
            Row("Workspace", workspaceRoot),
            Row("Projects", Projects(projectCount, activeProject)),
        };

        // Rare enough that a "0 rules" line would be noise for everyone who has no rules file.
        if (ruleCount > 0)
            lines.Add(Row("Rules", $"{ruleCount} loaded from rules.json"));

        lines.Add(Row("AI", aiState));
        lines.Add(string.Empty);
        lines.AddRange(storeLines);

        return lines;
    }

    /// <summary>
    /// Says "none" rather than showing a bare zero: an empty workspace is the normal state of a
    /// first run, and a user who reads "0" wonders what went wrong.
    /// </summary>
    private static string Projects(int count, string? active) => count switch
    {
        0 => "none in this workspace yet",
        _ when active is null => $"{count} found, none selected",
        _ => $"{count} found, {active} selected",
    };

    private static string Row(string label, string value)
        => $"  {label,LabelWidth}{value}";
}
