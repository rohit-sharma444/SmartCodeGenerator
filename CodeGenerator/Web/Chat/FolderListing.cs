using CodeGenerationEngine.Core;

namespace CodeGenerator.Web.Chat;

/// <summary>One thing the picker can click.</summary>
public sealed record FolderEntry(string Name, string Path, bool IsDirectory);

/// <summary>
/// What the solution picker shows for a directory.
/// </summary>
/// <remarks>
/// Listed by the host process, not the browser. A browser file input hands over a name and no
/// path, and a native dialog started from here would open behind the browser window — so the page
/// walks the filesystem through this instead.
/// <para>
/// Solution files first, then folders: the file is what the user came to click, and a listing that
/// buries it under twenty directories makes them type the path anyway.
/// </para>
/// </remarks>
public static class FolderListing
{
    /// <summary>Folders never worth offering: every project has both, and neither holds a
    /// solution.</summary>
    private static readonly string[] Skipped = ["bin", "obj", ".git", ".vs"];

    public static IReadOnlyList<FolderEntry> For(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
                return [];

            var solutions = Directory.EnumerateFiles(directory)
                .Where(SolutionFile.IsSolution)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(f => new FolderEntry(Path.GetFileName(f), f, false));

            var folders = Directory.EnumerateDirectories(directory)
                .Where(d => !Skipped.Contains(
                    Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(d => new FolderEntry(Path.GetFileName(d), d, true));

            return [.. solutions, .. folders];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be read is a folder with nothing to offer. The picker keeps
            // working; the path box is still there.
            return [];
        }
    }

    public static string? ParentOf(string directory)
    {
        try
        {
            return Directory.GetParent(
                Path.TrimEndingDirectorySeparator(directory))?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException)
        {
            return null;
        }
    }
}
