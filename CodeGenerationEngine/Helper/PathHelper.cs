using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Helper;

/// <summary>
/// Helper class for extracting file paths from natural language input.
/// </summary>
public static class PathHelper
{
    public static string GetAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be null or empty.", nameof(path));

        return Path.GetFullPath(path);
    }

    /// <summary>
    /// Returns true when <paramref name="candidatePath"/> (a directory or file
    /// path) resides inside <paramref name="root"/>. Both are resolved to full
    /// paths and compared on a directory boundary, case-insensitively.
    /// </summary>
    public static bool IsWithin(string? candidatePath, string? root)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) ||
            string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        string normalizedRoot;
        string normalizedCandidate;

        try
        {
            normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            normalizedCandidate = Path.GetFullPath(candidatePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return false;
        }

        if (normalizedCandidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        return normalizedCandidate.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves the workspace root for the generator.
    /// <para>
    /// An explicit <paramref name="projectPathEnv"/> (e.g. the PROJECTPATH
    /// environment variable) always wins. Otherwise the current directory is
    /// used, but when that directory sits inside a build-output folder
    /// (<c>bin</c>/<c>obj</c>, as happens when launching from Visual Studio or
    /// <c>dotnet run</c>), it climbs up to the nearest ancestor that actually
    /// looks like a project or solution root so generated files land somewhere
    /// the user can find instead of under <c>bin</c>.
    /// </para>
    /// </summary>
    public static string ResolveWorkspaceRoot(string? projectPathEnv, string currentDirectory)
    {
        if (!string.IsNullOrWhiteSpace(projectPathEnv))
        {
            try
            {
                return Path.GetFullPath(projectPathEnv);
            }
            catch
            {
                // Fall through to directory-based resolution on a bad value.
            }
        }

        string start;
        try
        {
            start = Path.GetFullPath(currentDirectory);
        }
        catch
        {
            return currentDirectory;
        }

        // If we are running from a build-output folder, or otherwise not sitting
        // on a project/solution root, walk up to the nearest directory that is.
        var dir = new DirectoryInfo(start);
        var insideBuildOutput = IsInsideBuildOutput(dir);

        if (!insideBuildOutput && ContainsProjectOrSolution(dir))
            return dir.FullName;

        for (var current = dir; current != null; current = current.Parent)
        {
            // Skip build-output segments themselves; only accept a real root.
            if (IsBuildOutputSegment(current.Name))
                continue;

            if (ContainsProjectOrSolution(current))
                return current.FullName;
        }

        return start;
    }

    private static bool ContainsProjectOrSolution(DirectoryInfo dir)
    {
        if (!dir.Exists)
            return false;

        return dir.EnumerateFiles("*.csproj").Any()
            || dir.EnumerateFiles("*.sln").Any()
            || dir.EnumerateFiles("*.slnx").Any();
    }

    private static bool IsInsideBuildOutput(DirectoryInfo dir)
    {
        for (var current = dir; current != null; current = current.Parent)
        {
            if (IsBuildOutputSegment(current.Name))
                return true;
        }

        return false;
    }

    private static bool IsBuildOutputSegment(string name)
        => name.Equals("bin", StringComparison.OrdinalIgnoreCase)
        || name.Equals("obj", StringComparison.OrdinalIgnoreCase);

    public static string? ExtractPath(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var match = Regex.Match(
            input,
            @"([A-Za-z]:\\(?:[^\\/:*?""<>|\r\n]+\\?)*)",
            RegexOptions.IgnoreCase);

        return match.Success
            ? match.Groups[1].Value.TrimEnd('\\')
            : null;
    }
}