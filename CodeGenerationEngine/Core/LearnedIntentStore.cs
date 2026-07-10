namespace CodeGenerationEngine.Core;

/// <summary>
/// Process-wide holder for the active <see cref="LearnedIntentMemory"/>. Learning
/// is on by default and disabled when the environment variable
/// <c>CG_NO_LEARN</c> is "1". Persistence is scoped to the current workspace.
/// </summary>
public static class LearnedIntentStore
{
    private static LearnedIntentMemory _current = new();
    private static string? _filePath;

    public static LearnedIntentMemory Current => _current;

    public static bool Enabled =>
        !string.Equals(
            Environment.GetEnvironmentVariable("CG_NO_LEARN"),
            "1",
            StringComparison.Ordinal);

    public static void Initialize(string workspaceRoot)
    {
        var folder = Path.Combine(workspaceRoot, ".codegen");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "learned.json");

        var memory = new LearnedIntentMemory();
        memory.Load(_filePath);
        memory.Decay(DateTime.UtcNow);
        _current = memory;
    }

    public static void Save()
    {
        if (_filePath != null)
            _current.Save(_filePath);
    }

    /// <summary>
    /// Re-reads the backing file, discarding in-memory changes that were never
    /// saved. Rollback needs this: restoring <c>learned.json</c> on disk is not
    /// enough on its own, because the next <see cref="Save"/> writes the
    /// un-reverted memory straight back over it. A no-op with no backing file.
    /// </summary>
    public static void ReloadFromDisk()
    {
        if (_filePath == null)
            return;

        var memory = new LearnedIntentMemory();
        memory.Load(_filePath);
        _current = memory;
    }

    /// <summary>Test seam: fresh in-memory store with no backing file.</summary>
    public static void UseInMemory()
    {
        _current = new LearnedIntentMemory();
        _filePath = null;
    }
}
