using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CodeGenerationEngine.Core;

/// <summary>What a solution file declares, and where the workspace it implies starts.</summary>
public sealed record SolutionProjects(
    string Root, IReadOnlyList<string> ProjectFiles, string? SolutionPath);

/// <summary>
/// Reads a .sln or .slnx and works out the workspace root it implies.
/// </summary>
/// <remarks>
/// Nothing in this tool read a solution file before this. The workspace root was resolved once
/// from <c>PROJECTPATH</c> or the launch directory, and the "projects" were every .csproj under it.
/// <para>
/// The root is the part worth being careful about, and it is <b>not</b> the solution file's own
/// folder. This repository's own <c>SmartCodeGenerator.slnx</c> points every path at <c>../</c>,
/// so that reading would root a workspace at a folder containing none of the seventeen projects,
/// and every command afterwards would be correct about the wrong tree.
/// </para>
/// </remarks>
public static class SolutionFile
{
    /// <summary>The `Project(...) = "Name", "Path", "{Guid}"` line of a classic .sln.</summary>
    private static readonly Regex ClassicProject = new(
        """^Project\("\{[^}]*\}"\)\s*=\s*"[^"]*",\s*"([^"]*)",""",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static bool IsSolution(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var extension = Path.GetExtension(path);

        return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The projects a solution declares, and the root they imply.
    /// </summary>
    /// <remarks>
    /// A malformed or unreadable file reports no projects rather than throwing: the caller is a
    /// picker in a browser, and a path the user typed being wrong is an ordinary situation with a
    /// remedy, not a fault to report.
    /// </remarks>
    public static SolutionProjects Read(string solutionPath)
    {
        var full = Path.GetFullPath(solutionPath);
        var folder = Path.GetDirectoryName(full) ?? full;

        IReadOnlyList<string> projects;

        try
        {
            projects = Path.GetExtension(full).Equals(".slnx", StringComparison.OrdinalIgnoreCase)
                ? ReadSlnx(full, folder)
                : ReadClassic(full, folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or System.Xml.XmlException or ArgumentException)
        {
            projects = [];
        }

        var root = projects.Count == 0 ? folder : CommonAncestorDirectory(projects);

        return new SolutionProjects(root, projects, full);
    }

    private static List<string> ReadSlnx(string full, string folder)
        => [.. XDocument.Load(full)
            .Descendants("Project")
            .Select(x => (string?)x.Attribute("Path"))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Resolve(folder, p!))
            .Where(IsProjectFile)];

    private static List<string> ReadClassic(string full, string folder)
        => [.. ClassicProject.Matches(File.ReadAllText(full))
            .Select(m => Resolve(folder, m.Groups[1].Value))
            .Where(IsProjectFile)];

    /// <summary>
    /// Solution folders appear as Project lines too, with a path that is not a project file. They
    /// are not projects and must not be registered as any.
    /// </summary>
    private static bool IsProjectFile(string path)
        => Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase);

    private static string Resolve(string folder, string relative)
        => Path.GetFullPath(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// The deepest directory that contains every one of <paramref name="filePaths"/>.
    /// </summary>
    /// <remarks>
    /// Compared segment by segment rather than by string prefix: a prefix comparison makes
    /// <c>C:\App</c> an ancestor of <c>C:\AppData</c>, which is how a workspace ends up rooted one
    /// directory too high and quietly scanning a sibling tree.
    /// <para>
    /// The result carries no trailing separator, matching <see cref="Path.GetDirectoryName(string)"/>,
    /// so callers can compare roots as strings. The one exception is a bare drive: "C:" alone is a
    /// relative path that resolves against the process's current directory, so it keeps its "\".
    /// </para>
    /// </remarks>
    public static string CommonAncestorDirectory(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0)
            return string.Empty;

        var folders = filePaths
            .Select(p => Path.GetDirectoryName(Path.GetFullPath(p)) ?? p)
            .ToList();

        if (folders.Count == 1)
            return folders[0];

        var split = folders
            .Select(f => f.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        var shared = new List<string>();

        for (var i = 0; i < split.Min(s => s.Length); i++)
        {
            var segment = split[0][i];

            if (split.Any(s => !s[i].Equals(segment, StringComparison.OrdinalIgnoreCase)))
                break;

            shared.Add(segment);
        }

        var rebuilt = string.Join(Path.DirectorySeparatorChar, shared);

        // "C:" is a drive-relative path, not the drive. Anything deeper is returned bare.
        return rebuilt.EndsWith(Path.VolumeSeparatorChar)
            ? rebuilt + Path.DirectorySeparatorChar
            : rebuilt;
    }
}
