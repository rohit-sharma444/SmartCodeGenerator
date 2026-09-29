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

    /// <summary>The active memory. Process-wide, and replaced by every reload.</summary>
    public static LearnedIntentMemory Current => _current;

    /// <summary>Whether learning is on; <c>CG_NO_LEARN=1</c> turns it off.</summary>
    public static bool Enabled =>
        !string.Equals(
            Environment.GetEnvironmentVariable("CG_NO_LEARN"),
            "1",
            StringComparison.Ordinal);

    /// <summary>
    /// Points the store at <paramref name="workspaceRoot"/>'s <c>.codegen</c>
    /// folder, loads what was learned there, and ages out stale entries.
    /// </summary>
    public static void Initialize(string workspaceRoot)
    {
        var folder = Path.Combine(workspaceRoot, ".codegen");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "learned.json");
        PhraseCandidates.Initialize(folder);

        var memory = new LearnedIntentMemory();
        memory.Load(_filePath);
        memory.Decay(DateTime.UtcNow);
        _current = memory;
    }

    /// <summary>Writes the memory to the backing file, if there is one.</summary>
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
        PhraseCandidates.Initialize(null);
    }
}
