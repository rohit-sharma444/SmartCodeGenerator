using System.Text;
using System.Text.RegularExpressions;

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

    /// <summary>The map in each backup folder: which .bak is which file.</summary>
    internal const string BackupMapName = "files.txt";

    internal static IReadOnlyList<string> Options { get; } = ["Yes, fix them", "No, leave them"];

    internal static string Title(int errors)
        => $"The build failed with {errors} {(errors == 1 ? "error" : "errors")}. Fix them?";

    internal static bool Accepted(int? choice) => choice == 0;

    public static IReadOnlyList<string> Lines(FixOutcome outcome, string projectPath)
    {
        var stuck = outcome.Edits.Count(e => e.RestoreFailed);
        var applied = outcome.Edits.Where(e => !e.Reverted && !e.RestoreFailed).ToList();
        var undone = outcome.Edits.Count(e => e.Reverted);
        var left = outcome.Remaining.Count;
        var lines = new List<string>();

        if (outcome.Green)
        {
            lines.Add(applied.Count > 0
                ? $"The build now succeeds with {Count(applied.Count, "edit")} kept."
                : outcome.Edits.Count == 0
                    ? "The build succeeds. No edits were needed."
                    : stuck > 0
                        ? "The build succeeds."
                        : $"The build succeeds. No edits were kept: all {Count(undone, "edit")} made {(undone == 1 ? "was" : "were")} undone.");
        }
        else
        {
            lines.Add(outcome.Edits.Count == 0
                ? "Nothing could be fixed."
                : applied.Count > 0
                    ? $"Partly fixed: {Count(applied.Count, "edit")} kept."
                    : stuck > 0
                        ? "Nothing was fixed."
                        : $"Nothing was kept: all {Count(undone, "edit")} made {(undone == 1 ? "was" : "were")} undone.");
            lines.Add($"The build still fails: {Errors(left)}.");
        }

        if (stuck > 0)
            lines.Add($"  {Count(stuck, "edit")} could not be undone; check {(stuck == 1 ? "it" : "them")} by hand.");

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
            lines.Add($"  in the newest folder. The file {BackupMapName} there names each backup.");
            lines.Add("  If a file was edited in more than one round, its true original is in");
            lines.Add("  the earliest round (r1 before r2 before r3).");
        }

        return lines;
    }

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? string.Empty : "s")}";

    private static string Errors(int n)
        => n == 0 ? "its errors could not be read" : $"{n} {(n == 1 ? "error remains" : "errors remain")}";

    /// <summary>The path relative to the project; the bare file name when it is not inside it.</summary>
    private static string Display(string file, string projectPath)
    {
        try
        {
            var root = Path.GetFullPath(projectPath).TrimEnd('\\', '/');

            if (!Path.IsPathRooted(file))
                return file.Split('\\', '/').Contains("..") ? Path.GetFileName(file) : file;

            var full = Path.GetFullPath(file);

            return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? full[(root.Length + 1)..]
                : Path.GetFileName(full);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Path.GetFileName(file);
        }
    }

    // A path segment may hold spaces in a folder name (C:\Users\Jo Smith\...), never in the last one.
    private const string Seg = @"[^\\/\s'""<>|*?:]+";
    private const string DirSeg = Seg + @"(?: " + Seg + @")*";

    private static readonly Regex DrivePath = new(
        @"(?<!\w)[A-Za-z]:[\\/](?![\\/])(?:" + DirSeg + @"[\\/])*(?:" + Seg + ")?", RegexOptions.Compiled);

    private static readonly Regex UncPath = new(
        @"\\\\" + DirSeg + @"(?:\\(?:" + DirSeg + @"\\)*" + Seg + ")?", RegexOptions.Compiled);

    private static readonly Regex RootedPath = new(
        @"(?<![\w.:/\\])/(?:" + Seg + @"/)+(?:" + Seg + ")?", RegexOptions.Compiled);

    /// <summary>
    /// Plain text only: no project folder, no other absolute path (reduced to its file name), and no
    /// control or non-ASCII character, so a line cannot carry a path or break the screen.
    /// </summary>
    internal static string Clean(string text, string projectPath)
    {
        text = Plain(text);

        var root = Path.GetFullPath(projectPath).TrimEnd('\\', '/');
        var pattern = string.Join(
            @"[\\/]+", root.Split('\\', '/').Select(Regex.Escape)) + @"[\\/]";
        text = Regex.Replace(text, pattern, string.Empty, RegexOptions.IgnoreCase);

        return RootedPath.Replace(UncPath.Replace(DrivePath.Replace(text, LastSegment), LastSegment), LastSegment);
    }

    private static string LastSegment(Match m)
        => m.Value.TrimEnd('\\', '/').Split('\\', '/')[^1];

    private static string Plain(string text)
    {
        var sb = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            sb.Append(c switch
            {
                '\u2018' or '\u2019' or '\u201A' => "'",
                '\u201C' or '\u201D' or '\u201E' => "\"",
                '\u2013' or '\u2014' or '\u2212' => "-",
                '\u2026' => "...",
                '\u00A0' => " ",
                < ' ' or '\u007F' => " ",
                > '~' => "?",
                _ => c.ToString(),
            });
        }

        return sb.ToString();
    }

    /// <summary>Wraps at <see cref="Width"/> on spaces; a continuation is indented four more than its first line.</summary>
    private static void AddWrapped(List<string> lines, string text, string projectPath)
    {
        text = Clean(text, projectPath);

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
