using CodeGenerationEngine.Helper;
using System.Globalization;

namespace CodeGenerator.App;

/// <summary>
/// Turns the result of compiling a freshly generated app into the lines the user reads.
/// </summary>
/// <remarks>
/// Pure, and tested, for the reason <c>StoreStatusReport</c> is: the console loop that prints
/// this cannot be driven from a test, so every decision it would otherwise make — what counts
/// as an error, how many to show, what to call a timeout — is lifted out of it.
/// </remarks>
internal static class BuildReport
{
    /// <summary>
    /// How many errors are quoted before the rest are counted instead. A failing generated app
    /// usually fails the same way many times over; the first handful say what went wrong, and
    /// the remainder only push them off the screen.
    /// </summary>
    internal const int MaxErrorsShown = 10;

    /// <summary>The marker MSBuild puts in front of every diagnostic it classes as an error.</summary>
    private const string ErrorMarker = ": error ";

    /// <summary>Whether the build produced a usable app.</summary>
    /// <remarks>
    /// A killed child can report exit 0 through an intermediate shell, so the timeout flag is
    /// consulted as well as the code — the two together are what "it built" means.
    /// </remarks>
    public static bool Succeeded(ProcessResult result)
        => result is { ExitCode: 0, TimedOut: false };

    /// <param name="elapsed">How long this build took, or null when that is not known: the time is then left out.</param>
    public static IReadOnlyList<string> Lines(
        ProcessResult result, TimeSpan? elapsed, string projectPath)
    {
        if (result.TimedOut)
        {
            return
            [
                // Deliberately not "FAILED". Nothing is known to be wrong with the generated
                // code; we simply stopped waiting, and telling the user their app does not
                // compile on that evidence would be a lie.
                elapsed is { } t ? $"Build timed out after {Elapsed(t)} and was cancelled." : "Build timed out and was cancelled.",
                RerunLine(projectPath)
            ];
        }

        if (Succeeded(result))
            return [$"Build succeeded{Took(elapsed)}"];

        var errors = ErrorsIn(result, projectPath);

        var lines = new List<string>
        {
            errors.Count == 0
                ? $"Build FAILED{Took(elapsed)}"
                : $"Build FAILED{Took(elapsed)} — {errors.Count} {Plural(errors.Count)}"
        };

        lines.AddRange(errors.Take(MaxErrorsShown).Select(e => "  " + e));

        if (errors.Count > MaxErrorsShown)
            lines.Add($"  ... and {errors.Count - MaxErrorsShown} more");

        lines.Add(RerunLine(projectPath));

        return lines;
    }

    private static string Took(TimeSpan? elapsed) => elapsed is { } t ? $" ({Elapsed(t)})" : string.Empty;

    private static string RerunLine(string projectPath)
        => $"  Rerun with: dotnet build {projectPath}";

    private static string Plural(int count) => count == 1 ? "error" : "errors";

    /// <summary>
    /// The distinct compiler errors in either output stream, in the order MSBuild reported
    /// them and trimmed of the parts the user already knows.
    /// </summary>
    /// <remarks>
    /// Both streams are read because which one carries a diagnostic depends on the logger and
    /// on whether MSBuild itself failed. Duplicates are dropped: MSBuild repeats an error once
    /// per referencing project, and reporting "2 errors" for one mistake sends the user hunting
    /// for a second that does not exist.
    /// </remarks>
    private static List<string> ErrorsIn(ProcessResult result, string projectPath)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();

        foreach (var raw in (result.StandardOutput + "\n" + result.StandardError)
                 .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();

            if (line.IndexOf(ErrorMarker, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var tidied = StripProjectPrefix(StripCsprojSuffix(line), projectPath);

            if (seen.Add(tidied))
                errors.Add(tidied);
        }

        return errors;
    }

    /// <summary>
    /// Drops the ` [C:\...\App.csproj]` MSBuild appends. It names the project the user just
    /// generated and asked to build, so it is the same on every line and pushes the message
    /// that differs off the edge of the screen.
    /// </summary>
    private static string StripCsprojSuffix(string line)
    {
        if (!line.EndsWith(']'))
            return line;

        var open = line.LastIndexOf('[');

        return open > 0
               && line.AsSpan(open).Contains(".csproj", StringComparison.OrdinalIgnoreCase)
            ? line[..open].TrimEnd()
            : line;
    }

    /// <summary>Makes the file reference relative to the project, for the same reason.</summary>
    private static string StripProjectPrefix(string line, string projectPath)
    {
        var prefix = projectPath.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? line[prefix.Length..]
            : line;
    }

    /// <summary>
    /// A duration a person can read at a glance. Invariant culture throughout: a machine with a
    /// comma decimal separator would otherwise render "4,1s".
    /// </summary>
    private static string Elapsed(TimeSpan elapsed)
        => elapsed.TotalSeconds < 60
            ? elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s"
            : string.Format(
                CultureInfo.InvariantCulture, "{0}m {1:00}s",
                (int)elapsed.TotalMinutes, elapsed.Seconds);
}
