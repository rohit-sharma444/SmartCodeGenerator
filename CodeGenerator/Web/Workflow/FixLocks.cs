namespace CodeGenerator.Web.Workflow;

/// <summary>
/// Which projects have a fixer running, shared by every page and tab in this process.
/// </summary>
/// <remarks>
/// The fixer is a singleton but its "already running" flag was per page, so two tabs could edit one
/// project's files at the same time and revert each other's work.
/// </remarks>
internal static class FixLocks
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> Running = new();

    /// <summary>True when the caller now owns the project; false when a fix is already running there.</summary>
    public static bool TryEnter(string projectPath) => Running.TryAdd(Key(projectPath), true);

    public static void Leave(string projectPath) => Running.TryRemove(Key(projectPath), out _);

    private static string Key(string projectPath)
        => Path.GetFullPath(projectPath).TrimEnd('\\', '/').ToUpperInvariant();
}
