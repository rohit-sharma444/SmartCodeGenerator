namespace CodeGenerator.BuildFix;

/// <summary>
/// The words the user reads about fixing build errors. Pure, so both screens say the same thing.
/// Every line is plain ASCII, at most <see cref="Width"/> characters, and names files relative to the
/// project: the headline never claims more than <see cref="FixOutcome.Green"/> proves.
/// </summary>
internal static class BuildFixReport
{
    internal const int MaxShown = 10;

    internal const int Width = 78;

    internal const int DefaultIndex = 0;

    /// <summary>Where the originals go: see <see cref="FixSession"/> (folder, then one per round).</summary>
    internal const string BackupLocation = ".codegen\\fix-backups";

    internal static IReadOnlyList<string> Options { get; } = ["Yes, fix them", "No, leave them"];

    internal static string Title(int errors)
        => $"The build failed with {errors} {(errors == 1 ? "error" : "errors")}. Fix them?";

    internal static bool Accepted(int? choice) => choice == 0;

    public static IReadOnlyList<string> Lines(FixOutcome outcome, string projectPath)
    {
        var applied = outcome.Edits.Where(e => !e.Reverted).ToList();
        var undone = outcome.Edits.Count - applied.Count;
        var left = outcome.Remaining.Count;
        var lines = new List<string>();

        if (outcome.Green)
        {
            lines.Add(applied.Count == 0
                ? "The build succeeds. No edits were needed."
                : $"The build fixed itself: it builds now ({Count(applied.Count, "edit")} kept).");
        }
        else
        {
            lines.Add(outcome.Edits.Count == 0
                ? "Nothing could be fixed."
                : applied.Count == 0
                    ? $"Nothing was kept: all {Count(undone, "edit")} made {(undone == 1 ? "was" : "were")} undone."
                    : $"Partly fixed: {Count(applied.Count, "edit")} kept.");
            lines.Add($"The build still fails: {Errors(left)}.");
        }

        foreach (var edit in applied)
            AddWrapped(lines, $"  Edited {Display(edit.File, projectPath)}: {edit.By}", projectPath);

        if (undone > 0 && applied.Count > 0)
            lines.Add($"  {Count(undone, "other edit")} {(undone == 1 ? "was" : "were")} undone.");

        if (!outcome.Green)
        {
            foreach (var d in outcome.Remaining.Take(MaxShown))
            {
                var where = d.File.Length == 0 ? string.Empty : $"{Display(d.File, projectPath)}({d.Line},{d.Column}): ";
                AddWrapped(lines, $"  {where}{d.Code}: {d.Message}", projectPath);
            }

            if (left > MaxShown)
                lines.Add($"  ... and {left - MaxShown} more");
        }

        foreach (var note in outcome.Notes)
            AddWrapped(lines, "  " + note, projectPath);

        if (outcome.Edits.Count > 0)
        {
            lines.Add($"  The original of each edited file is kept under {BackupLocation}");
            lines.Add("  (newest folder, then r1, r2 and r3 for each round).");
        }

        return lines;
    }

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? string.Empty : "s")}";

    private static string Errors(int n)
        => n == 0 ? "its errors could not be read" : $"{n} {(n == 1 ? "error remains" : "errors remain")}";

    /// <summary>The path relative to the project; the bare file name when it is not inside it.</summary>
    private static string Display(string file, string projectPath)
    {
        string relative;
        try
        {
            relative = Path.GetRelativePath(projectPath, file);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Path.GetFileName(file);
        }

        return Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal)
            ? Path.GetFileName(file)
            : relative;
    }

    /// <summary>Wraps at <see cref="Width"/> on spaces; a continuation is indented four more than its first line.</summary>
    private static void AddWrapped(List<string> lines, string text, string projectPath)
    {
        var root = Path.GetFullPath(projectPath).TrimEnd('\\', '/');
        text = text.Replace(root + "\\", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(root + "/", string.Empty, StringComparison.OrdinalIgnoreCase);

        var indent = text.Length - text.TrimStart().Length;
        var current = new string(' ', indent);
        var hasWord = false;

        foreach (var word in text.TrimStart().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var rest = word;

            while (rest.Length > 0)
            {
                var room = Width - current.Length - (hasWord ? 1 : 0);

                if (rest.Length <= room)
                {
                    current += (hasWord ? " " : string.Empty) + rest;
                    hasWord = true;
                    rest = string.Empty;
                }
                else if (hasWord)
                {
                    lines.Add(current);
                    current = new string(' ', indent + 4);
                    hasWord = false;
                }
                else
                {
                    // One word longer than a line: cut it.
                    lines.Add(current + rest[..room]);
                    rest = rest[room..];
                    current = new string(' ', indent + 4);
                }
            }
        }

        if (hasWord)
            lines.Add(current);
    }
}
