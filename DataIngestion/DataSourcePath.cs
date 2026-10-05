namespace DataIngestion;

/// <summary>
/// Where a data file named on a <c>build app</c> line, or typed at its prompts, actually is.
/// </summary>
/// <remarks>
/// The readers open <see cref="DataSourceSpec.Location"/> as given, so a bare <c>books.csv</c> was
/// read against the process's current directory. That is the workspace only when the console was
/// started from it; piped with PROJECTPATH from anywhere else it said "No file at books.csv" with
/// the file sitting in the workspace. Scripts (<c>run</c>, <c>save session</c>) already resolve
/// against the workspace, and this is the same rule for data files.
/// <para>
/// The current directory is a fallback, not dropped: a relative path that worked before, because
/// the console was started beside the file, keeps working. The workspace wins when both hold a
/// file of that name, since the workspace is what the user pointed the tool at. A file found in
/// neither is named at the workspace path, which is the one the user meant.
/// </para>
/// </remarks>
public static class DataSourcePath
{
    /// <summary>
    /// The full path for a file location: unchanged when rooted, else the workspace's file,
    /// else the current directory's, else the workspace path.
    /// </summary>
    public static string Resolve(string location, string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(location) || Path.IsPathRooted(location)
            || string.IsNullOrWhiteSpace(workspaceRoot))
            return location;

        try
        {
            var inWorkspace = Path.GetFullPath(Path.Combine(workspaceRoot, location));

            if (File.Exists(inWorkspace))
                return inWorkspace;

            var inCurrent = Path.GetFullPath(location);

            return File.Exists(inCurrent) ? inCurrent : inWorkspace;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                                      or PathTooLongException)
        {
            // Not a path this machine can form; the reader reports it as typed.
            return location;
        }
    }

    /// <summary>
    /// The same source with its file resolved. A SQL source is returned as is: its location is a
    /// connection string, not a path.
    /// </summary>
    public static DataSourceSpec Resolve(DataSourceSpec source, string? workspaceRoot)
        => source.Kind == DataSourceKind.Sql
            ? source
            : source with { Location = Resolve(source.Location, workspaceRoot) };
}
