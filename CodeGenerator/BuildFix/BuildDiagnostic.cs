using System.Text.RegularExpressions;
using CodeGenerationEngine.Helper;

namespace CodeGenerator.BuildFix;

/// <summary>One compiler error from <c>dotnet build</c>.</summary>
/// <param name="File">Absolute path, or empty when the compiler gave no location.</param>
internal sealed record BuildDiagnostic(string File, int Line, int Column, string Code, string Message);

/// <summary>Reads <c>dotnet build</c> output into <see cref="BuildDiagnostic"/>s.</summary>
/// <remarks>
/// MSBuild repeats an error once per target framework and per referencing project, so equal
/// diagnostics are dropped: "2 errors" for one mistake sends the user hunting for a second.
/// Warnings are ignored; only the lines MSBuild classes as errors are read.
/// </remarks>
internal static class BuildDiagnostics
{
    private const string Suffix = @"(?:\s*\[[^\]]*\.csproj\])?\s*$";

    private static readonly Regex Located = new(
        @"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s*error\s+(?<code>[A-Za-z]+\d+):\s*(?<msg>.*?)" + Suffix,
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Unlocated = new(
        @"^(?:.*?:\s*)?error\s+(?<code>[A-Za-z]+\d+):\s*(?<msg>.*?)" + Suffix,
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<BuildDiagnostic> Parse(ProcessResult result, string projectPath)
    {
        var seen = new HashSet<BuildDiagnostic>();
        var list = new List<BuildDiagnostic>();

        foreach (var raw in (result.StandardOutput + "\n" + result.StandardError)
                 .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();

            if (line.IndexOf(": error ", StringComparison.OrdinalIgnoreCase) < 0
                && !line.StartsWith("error ", StringComparison.OrdinalIgnoreCase))
                continue;

            BuildDiagnostic? d = null;

            if (Located.Match(line) is { Success: true } m)
            {
                var file = m.Groups["file"].Value.Trim();
                if (!Path.IsPathRooted(file))
                    file = Path.GetFullPath(Path.Combine(projectPath, file));

                d = new BuildDiagnostic(
                    file, int.Parse(m.Groups["line"].Value), int.Parse(m.Groups["col"].Value),
                    m.Groups["code"].Value.ToUpperInvariant(), m.Groups["msg"].Value.Trim());
            }
            else if (Unlocated.Match(line) is { Success: true } u)
            {
                d = new BuildDiagnostic(
                    string.Empty, 0, 0, u.Groups["code"].Value.ToUpperInvariant(), u.Groups["msg"].Value.Trim());
            }

            if (d is not null && seen.Add(d))
                list.Add(d);
        }

        return list;
    }

    /// <summary>"Could not copy" and "unable to copy": the app being built is running and holds its exe.</summary>
    private static readonly string[] LockedOutputCodes = ["MSB3027", "MSB3021"];

    /// <summary>
    /// True when a fix could edit the cause: a compiler (CS) error with a line, in a .cs file inside
    /// the project and outside obj, bin and .codegen. Anything else (an MSBuild error in a targets
    /// file, a file elsewhere) no rule and no AI may touch.
    /// </summary>
    public static bool IsFixable(BuildDiagnostic d, string projectPath)
        => d.Code.StartsWith("CS", StringComparison.OrdinalIgnoreCase)
            && d.Line >= 1
            && d.File.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            && FixSession.IsEditableIn(projectPath, d.File);

    /// <summary>How many of <paramref name="diagnostics"/> a fix could edit; the fix is offered only when some are.</summary>
    public static int Fixable(IReadOnlyList<BuildDiagnostic> diagnostics, string projectPath)
        => diagnostics.Count(d => IsFixable(d, projectPath));

    /// <summary>True when the build could not replace the app's files because the app is running.</summary>
    public static bool OutputLocked(IReadOnlyList<BuildDiagnostic> diagnostics)
        => diagnostics.Any(d => LockedOutputCodes.Contains(d.Code, StringComparer.OrdinalIgnoreCase));
}
