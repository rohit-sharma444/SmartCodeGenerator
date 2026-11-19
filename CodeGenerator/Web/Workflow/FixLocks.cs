namespace CodeGenerator.Web.Workflow;

/// <summary>Why a page holds a project's lock.</summary>
internal enum LockPurpose
{
    /// <summary>The Generate stage is writing the project and building it.</summary>
    Write,

    /// <summary>The fixer is editing the project's files.</summary>
    Fix,
}

/// <summary>
/// Which projects are being written or fixed, shared by every page and tab in this process.
/// </summary>
/// <remarks>
/// The fixer is a singleton but its "already running" flag was per page, so two tabs could edit one
/// project's files at the same time and revert each other's work. A write holds the same lock, so
/// a refused caller is told which of the two holds it: "a fix is running" was said to a page whose
/// project another page was only writing.
/// </remarks>
internal static class FixLocks
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, LockPurpose> Running = new();

    /// <summary>
    /// True when the caller now owns the project for <paramref name="purpose"/>; false when another
    /// page holds it, and then <paramref name="holder"/> says what that page is doing.
    /// </summary>
    public static bool TryEnter(string projectPath, LockPurpose purpose, out LockPurpose holder)
    {
        var key = Key(projectPath);

        while (true)
        {
            if (Running.TryAdd(key, purpose))
            {
                holder = purpose;
                return true;
            }

            // Read after the failed add; a holder that left in between means the add is tried again.
            if (Running.TryGetValue(key, out holder))
                return false;
        }
    }

    public static void Leave(string projectPath) => Running.TryRemove(Key(projectPath), out _);

    /// <summary>What a refused page tells the user, by what the holder is doing.</summary>
    public static string Busy(LockPurpose holder) => holder == LockPurpose.Fix
        ? "A fix is already running for this app. Wait for it to finish."
        : "This app is being written or built on another page. Wait for it to finish.";

    private static string Key(string projectPath)
        => Path.GetFullPath(projectPath).TrimEnd('\\', '/').ToUpperInvariant();
}
