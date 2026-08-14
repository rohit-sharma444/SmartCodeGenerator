namespace CodeGenerator.App;

/// <summary>
/// What to say when the workspace root does not exist.
/// </summary>
/// <remarks>
/// This used to arrive as <c>Warning: Failed to initialize existing projects: Could not find a
/// part of the path …</c> — the tool narrating its own method failing. The situation is simpler
/// than that and entirely the user's to fix: they pointed at a directory that is not there.
/// <para>
/// Pure, so the wording is tested rather than only the fact that something was printed.
/// </para>
/// </remarks>
internal static class WorkspaceNotice
{
    private static readonly string[] Nothing = [];

    /// <param name="projectPathSet">
    /// Whether <c>PROJECTPATH</c> is what chose this root. Naming the variable is the actionable
    /// half of the message when it is set — and misleading when it is not, since the root is then
    /// just the current directory and the user never set anything.
    /// </param>
    public static IReadOnlyList<string> Lines(string workspaceRoot, bool projectPathSet)
    {
        if (Directory.Exists(workspaceRoot))
            return Nothing;

        var lines = new List<string>
        {
            $"Workspace does not exist yet: {workspaceRoot}",
        };

        lines.Add(projectPathSet
            ? "  PROJECTPATH points there. It will be created when something is first written,"
            : "  It will be created when something is first written,");

        lines.Add(projectPathSet
            ? "  or unset PROJECTPATH to work in the current directory instead."
            : "  so nothing is wrong unless that is not where you meant to work.");

        return lines;
    }
}
