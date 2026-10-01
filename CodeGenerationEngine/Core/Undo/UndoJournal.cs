namespace CodeGenerationEngine.Core.Undo;

/// <summary>What an undo or redo did, or why it did nothing.</summary>
public sealed record UndoResult(bool Done, string Message);

/// <summary>The in-memory state a command can change, captured so undo can put it back.</summary>
public interface ISessionState
{
    bool SameAs(ISessionState other);

    /// <summary>
    /// What has changed, since <paramref name="replacing"/> was captured, in the parts this state
    /// would put back — or null when restoring over it loses nothing.
    /// </summary>
    string? Conflict(ISessionState replacing);

    /// <summary>
    /// Puts back the parts in which this state differs from <paramref name="replacing"/>, leaving
    /// the rest as it is now, so another session's changes survive.
    /// </summary>
    void Restore(ISessionState replacing);
}

/// <summary>
/// Undo and redo for whatever a command did: to the workspace's files, to the project registry
/// beside the exe, and to the session's in-memory state.
/// </summary>
/// <remarks>
/// <para>
/// It knows nothing about commands. A shadow copy of the workspace is taken once; around each
/// command the workspace is scanned by length and last-write time, and only the files that differ
/// are kept — their version before, and after, in a folder per entry. So it covers every command,
/// including ones written later, at a cost proportional to what a command changed rather than to
/// the size of the workspace.
/// </para>
/// <para>
/// Edits made outside the tool are folded into the shadow before the next command runs, so they
/// are never credited to it; and an undo refuses, naming the file, rather than overwrite one.
/// </para>
/// </remarks>
public sealed class UndoJournal : IDisposable
{
    public const int Capacity = 50;

    // Never rolled back: undoing a class edit must not erase what the tool has learned.
    private static readonly string[] AlsoExcluded = [".codegen", ".dd-cache"];

    private readonly Action<string>? _notify;
    private readonly string? _registryPath;
    private readonly Func<ISessionState>? _captureState;
    private readonly Action? _reloadRegistry;
    private readonly string _shadowParent;
    private readonly int _maxFiles;
    private readonly long _maxBytes;

    private readonly List<Entry> _undo = [];
    private readonly Stack<Entry> _redo = new();
    private Dictionary<string, Stamp> _known = new(StringComparer.OrdinalIgnoreCase);

    private string? _shadow;
    private bool _started;
    private int _depth;
    private int _entryCount;
    private Open? _open;
    private bool _disposed;

    public UndoJournal(
        string workspaceRoot,
        Action<string>? notify = null,
        string? registryPath = null,
        Func<ISessionState>? captureState = null,
        string? shadowParent = null,
        int maxFiles = WorkspaceSnapshot.MaxFiles,
        long maxBytes = WorkspaceSnapshot.MaxBytes,
        Action? reloadRegistry = null)
    {
        WorkspaceRoot = Path.GetFullPath(workspaceRoot);
        _notify = notify;
        _registryPath = registryPath;
        _captureState = captureState;
        _reloadRegistry = reloadRegistry;
        _shadowParent = shadowParent ?? Path.GetTempPath();
        _maxFiles = maxFiles;
        _maxBytes = maxBytes;
    }

    /// <summary>
    /// The journal a CLI or chat session uses: the real project registry, and the real in-memory
    /// state. The constructor's defaults track neither, so tests see only what they set up.
    /// </summary>
    public static UndoJournal ForSession(string workspaceRoot, Action<string>? notify = null) =>
        new(workspaceRoot, notify, ProjectManager.RegistryPath, SessionState.Capture,
            reloadRegistry: () => ProjectManager.ReloadWithin(workspaceRoot));

    /// <summary>
    /// A journal that never copies the workspace and offers no undo, for the copy of the tool a
    /// script runs a build in: nobody can type undo there, and its parent already holds a snapshot.
    /// What commands keep is still raised, through <see cref="KeptWithoutEntry"/>.
    /// </summary>
    public static UndoJournal Disabled(string workspaceRoot, string? shadowParent = null) =>
        new(workspaceRoot, shadowParent: shadowParent) { Enabled = false };

    public string WorkspaceRoot { get; }

    /// <summary>False once the size guard has switched undo off for the session.</summary>
    public bool Enabled { get; private set; } = true;

    public bool CanUndo => Enabled && _undo.Count > 0;
    public bool CanRedo => Enabled && _redo.Count > 0;
    public string? NextUndoLabel => CanUndo ? _undo[^1].Label : null;
    public string? NextRedoLabel => CanRedo ? _redo.Peek().Label : null;

    /// <summary>An entry was recorded, with the lines its command asked to keep (maybe none).</summary>
    public event Action<IReadOnlyList<string>>? Recorded;

    /// <summary>A command kept lines but no entry could be made for it — undo is off, or the journal
    /// could not copy a file — so no undo will ever take them back.</summary>
    public event Action<IReadOnlyList<string>>? KeptWithoutEntry;

    /// <summary>The latest entry was undone.</summary>
    public event Action? Undone;

    /// <summary>The latest undone entry was redone.</summary>
    public event Action? Redone;

    private const string OffMessage =
        "Undo is off for this session: the workspace has more than 5,000 files or 200 MB.";

    /// <summary>
    /// Starts an entry for one command; disposing the result ends it. A Begin inside an open one
    /// — a batch running its commands — belongs to the outer entry.
    /// </summary>
    public UndoScope Begin(string label)
    {
        if (_disposed)
            return new UndoScope(this, UndoScope.Mode.Ignored);
        if (!Enabled)
            return new UndoScope(this, UndoScope.Mode.Off);

        if (_depth++ > 0)
            return new UndoScope(this, UndoScope.Mode.Journaled);

        try
        {
            if (!_started && !Start())
            {
                _depth = 0;
                return new UndoScope(this, UndoScope.Mode.Off);
            }

            Sync();

            _open = new Open(label, _captureState?.Invoke(), ReadRegistry(), Directories(), []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable workspace must not stop the command; it only goes unjournalled — and
            // what it keeps is still saved, since a saved session silently missing it would replay
            // differently.
            _open = null;
            _depth = 0;
            return new UndoScope(this, UndoScope.Mode.Unrecorded);
        }

        return new UndoScope(this, UndoScope.Mode.Journaled);
    }

    public UndoResult Undo()
    {
        if (!Enabled)
            return new UndoResult(false, OffMessage);
        if (_undo.Count == 0)
            return new UndoResult(false, "Nothing to undo.");

        var entry = _undo[^1];

        var conflict = FirstDifference(entry, entry.After) ?? NewFileInside(entry)
            ?? RegistryConflict(entry, entry.RegistryAfter) ?? StateConflict(entry.StateBefore, entry.StateAfter);
        if (conflict is not null)
            return new UndoResult(false, $"Can't undo: {conflict} has changed since {entry.Label}.");

        if (TryApply(entry, entry.Before, entry.After) is { } failure)
            return new UndoResult(false, $"Can't undo {entry.Label}: {failure} Nothing was changed.");

        foreach (var root in entry.CreatedRoots)
            TryDeleteTree(Path.Combine(WorkspaceRoot, root));
        if (entry.RegistryChanged)
            WriteRegistry(entry.RegistryBefore);
        if (entry.StateBefore is not null && entry.StateAfter is not null)
            entry.StateBefore.Restore(entry.StateAfter);

        _undo.RemoveAt(_undo.Count - 1);
        _redo.Push(entry);

        Undone?.Invoke();
        return new UndoResult(true, "Undid: " + entry.Label + Describe(entry, entry.Before));
    }

    public UndoResult Redo()
    {
        if (!Enabled)
            return new UndoResult(false, OffMessage);
        if (_redo.Count == 0)
            return new UndoResult(false, "Nothing to redo.");

        var entry = _redo.Peek();

        var conflict = FirstDifference(entry, entry.Before)
            ?? RegistryConflict(entry, entry.RegistryBefore) ?? StateConflict(entry.StateAfter, entry.StateBefore);
        if (conflict is not null)
            return new UndoResult(false, $"Can't redo: {conflict} has changed since you undid {entry.Label}.");

        if (TryApply(entry, entry.After, entry.Before) is { } failure)
            return new UndoResult(false, $"Can't redo {entry.Label}: {failure} Nothing was changed.");

        if (entry.RegistryChanged)
            WriteRegistry(entry.RegistryAfter);
        if (entry.StateBefore is not null && entry.StateAfter is not null)
            entry.StateAfter.Restore(entry.StateBefore);

        _redo.Pop();
        _undo.Add(entry);

        Redone?.Invoke();
        return new UndoResult(true, "Redid: " + entry.Label + Describe(entry, entry.After));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_shadow is not null)
            TryDeleteTree(_shadow);
    }

    // ----- one command --------------------------------------------------------------------------

    internal void End(IReadOnlyList<string> kept)
    {
        _open?.Kept.AddRange(kept);

        if (--_depth > 0 || _open is null)
            return;

        // Disposed while the command ran (the chat page closed): the shadow is gone, and copying
        // into it now would only recreate it where nothing cleans it up.
        if (_disposed)
        {
            _open = null;
            return;
        }

        var open = _open;
        _open = null;

        try
        {
            var now = Scan();
            var changed = now.Keys.Where(p => !_known.TryGetValue(p, out var s) || s != now[p])
                .Concat(_known.Keys.Where(p => !now.ContainsKey(p)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var createdRoots = Roots(Directories().Except(open.Directories, StringComparer.OrdinalIgnoreCase));

            var stateAfter = _captureState?.Invoke();
            var stateChanged = open.State is not null && stateAfter is not null && !open.State.SameAs(stateAfter);

            var registryAfter = ReadRegistry();
            var registryChanged = !Same(open.Registry, registryAfter);

            if (changed.Count == 0 && createdRoots.Count == 0 && !stateChanged && !registryChanged)
                return;

            var folder = Path.Combine(_shadow!, "entries", (++_entryCount).ToString());
            var before = Path.Combine(folder, "before");
            var after = Path.Combine(folder, "after");

            // A file that existed before but has no shadow copy would be deleted, not restored,
            // by an undo. Better no entry; the next Sync re-mirrors whatever changed.
            if (changed.FirstOrDefault(p => _known.ContainsKey(p) && !File.Exists(Path.Combine(_shadow!, "files", p)))
                is { } unmirrored)
                throw new IOException("No shadow copy of " + unmirrored);

            foreach (var path in changed)
            {
                CopyIfExists(Path.Combine(_shadow!, "files", path), Path.Combine(before, path));
                CopyIfExists(Path.Combine(WorkspaceRoot, path), Path.Combine(after, path));
                Mirror(path);
            }

            _known = now;

            _undo.Add(new Entry(open.Label, folder, before, after, changed, createdRoots,
                open.State, stateAfter, open.Registry, registryAfter, registryChanged, [.. open.Kept]));

            foreach (var undone in _redo)
                TryDeleteTree(undone.Folder);
            _redo.Clear();

            Recorded?.Invoke(_undo[^1].Kept);

            while (_undo.Count > Capacity)
            {
                TryDeleteTree(_undo[0].Folder);
                _undo.RemoveAt(0);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Better no entry than a half-written one that would restore the wrong thing. The
            // command still happened, so what it kept is still saved.
            KeepWithoutEntry(open.Kept);
        }
    }

    // No entry, so no undo will ever take these back; they are kept as they are.
    internal void KeepWithoutEntry(IReadOnlyList<string> kept)
    {
        if (kept.Count > 0)
            KeptWithoutEntry?.Invoke([.. kept]);
    }

    // ----- the shadow ---------------------------------------------------------------------------

    // Started only once the whole shadow is taken. A copy that fails part way (a file another
    // program holds open) is thrown away and taken again next time: a shadow missing a file would
    // let an undo delete that file instead of restoring it.
    private bool Start()
    {
        var files = WorkspaceSnapshot.IncludedFiles(WorkspaceRoot, AlsoExcluded).ToList();
        var bytes = files.Sum(f => new FileInfo(f).Length);

        if (files.Count > _maxFiles || bytes > _maxBytes)
        {
            _started = true;
            Enabled = false;
            _notify?.Invoke(OffMessage);
            return false;
        }

        _shadow = Path.Combine(_shadowParent, "codegen_undo_" + Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(Path.Combine(_shadow, "files"));

            _known = Scan();
            foreach (var path in _known.Keys)
                Mirror(path);
        }
        catch
        {
            TryDeleteTree(_shadow);
            _shadow = null;
            _known = new(StringComparer.OrdinalIgnoreCase);
            throw;
        }

        _started = true;
        return true;
    }

    // Folds edits made outside the tool into the shadow, so the next entry does not claim them.
    private void Sync()
    {
        var now = Scan();

        foreach (var (path, stamp) in now)
            if (!_known.TryGetValue(path, out var known) || known != stamp)
                Mirror(path);

        foreach (var path in _known.Keys.Where(p => !now.ContainsKey(p)))
            Mirror(path);

        _known = now;
    }

    // Makes the shadow's copy of one path match the workspace: copied, or deleted.
    private void Mirror(string path)
    {
        var source = Path.Combine(WorkspaceRoot, path);
        var copy = Path.Combine(_shadow!, "files", path);

        if (File.Exists(source))
            CopyIfExists(source, copy);
        else if (File.Exists(copy))
            File.Delete(copy);
    }

    private Dictionary<string, Stamp> Scan() =>
        WorkspaceSnapshot.IncludedFiles(WorkspaceRoot, AlsoExcluded)
            .Select(f => new FileInfo(f))
            .ToDictionary(
                f => Path.GetRelativePath(WorkspaceRoot, f.FullName),
                f => new Stamp(f.Length, f.LastWriteTimeUtc),
                StringComparer.OrdinalIgnoreCase);

    // Every directory under the root, relative, skipping (and not listing) excluded ones.
    private HashSet<string> Directories()
    {
        var excluded = new HashSet<string>(
            ["bin", "obj", ".git", "TestResults", "node_modules", .. AlsoExcluded], StringComparer.OrdinalIgnoreCase);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>([WorkspaceRoot]);

        while (pending.Count > 0)
            foreach (var directory in Directory.EnumerateDirectories(pending.Pop()))
            {
                if (excluded.Contains(Path.GetFileName(directory)))
                    continue;
                result.Add(Path.GetRelativePath(WorkspaceRoot, directory));
                pending.Push(directory);
            }

        return result;
    }

    // The created directories whose parent already existed: deleting these removes the rest.
    private static List<string> Roots(IEnumerable<string> created)
    {
        var set = created.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return set.Where(d => Path.GetDirectoryName(d) is not { Length: > 0 } parent || !set.Contains(parent))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ----- undo and redo ------------------------------------------------------------------------

    // The first changed path whose current contents differ from the side we expect to find.
    private string? FirstDifference(Entry entry, string expectedSide)
    {
        foreach (var path in entry.Paths)
        {
            var current = Path.Combine(WorkspaceRoot, path);
            var expected = Path.Combine(expectedSide, path);

            if (File.Exists(current) != File.Exists(expected))
                return path;
            if (File.Exists(current) && !File.ReadAllBytes(current).AsSpan().SequenceEqual(File.ReadAllBytes(expected)))
                return path;
        }

        return null;
    }

    // A file added since, inside a folder this entry created: deleting the folder would lose it.
    private string? NewFileInside(Entry entry)
    {
        var mine = entry.Paths.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var root in entry.CreatedRoots)
        {
            var full = Path.Combine(WorkspaceRoot, root);
            if (!Directory.Exists(full))
                continue;

            // Never scanned, so never in Paths; but a repository started in it since is the
            // user's, and deleting the folder would take its history.
            if (Directory.EnumerateDirectories(full, ".git", SearchOption.AllDirectories).FirstOrDefault() is { } git)
                return Path.GetRelativePath(WorkspaceRoot, git);

            var stranger = WorkspaceSnapshot.IncludedFiles(full, AlsoExcluded)
                .Select(f => Path.GetRelativePath(WorkspaceRoot, f))
                .FirstOrDefault(f => !mine.Contains(f));
            if (stranger is not null)
                return stranger;
        }

        return null;
    }

    private string? RegistryConflict(Entry entry, byte[]? expected) =>
        entry.RegistryChanged && !Same(ReadRegistry(), expected) ? "the project list" : null;

    private static string? StateConflict(ISessionState? restoring, ISessionState? replacing) =>
        restoring is not null && replacing is not null ? restoring.Conflict(replacing) : null;

    // Puts every changed path to the given side; if one cannot be written (a file another program
    // holds open), puts the ones already done back to the other side and returns why.
    private string? TryApply(Entry entry, string side, string otherSide)
    {
        var done = 0;
        try
        {
            foreach (var path in entry.Paths)
            {
                done++;
                Apply(path, side);
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var path in entry.Paths.Take(done))
                try { Apply(path, otherSide); }
                catch (Exception again) when (again is IOException or UnauthorizedAccessException) { }

            return ex.Message;
        }
    }

    // Puts one path to the given side's version, in the workspace and the shadow.
    private void Apply(string path, string side)
    {
        var target = Path.Combine(WorkspaceRoot, path);
        var version = Path.Combine(side, path);

        if (File.Exists(version))
            CopyIfExists(version, target);
        else if (File.Exists(target))
            File.Delete(target);

        Mirror(path);

        var info = new FileInfo(target);
        if (info.Exists)
            _known[path] = new Stamp(info.Length, info.LastWriteTimeUtc);
        else
            _known.Remove(path);
    }

    private static string Describe(Entry entry, string side)
    {
        if (entry.Paths.Count == 0)
            return string.Empty;

        if (entry.Paths.Count == 1)
            return (File.Exists(Path.Combine(side, entry.Paths[0])) ? " — restored " : " — removed ") + entry.Paths[0];

        var restored = entry.Paths.Count(p => File.Exists(Path.Combine(side, p)));
        var removed = entry.Paths.Count - restored;

        var parts = new List<string>();
        if (restored > 0)
            parts.Add($"restored {restored} file{(restored == 1 ? "" : "s")}");
        if (removed > 0)
            parts.Add($"removed {removed}");

        return " — " + string.Join(", ", parts);
    }

    // ----- the registry beside the exe ----------------------------------------------------------

    private byte[]? ReadRegistry() =>
        _registryPath is not null && File.Exists(_registryPath) ? File.ReadAllBytes(_registryPath) : null;

    private void WriteRegistry(byte[]? contents)
    {
        if (_registryPath is null)
            return;

        if (contents is null)
        {
            if (File.Exists(_registryPath))
                File.Delete(_registryPath);
        }
        else
        {
            File.WriteAllBytes(_registryPath, contents);
        }

        // The file is only half of it: the project list in memory would otherwise still offer
        // a project the undo removed, and write it back on the next save.
        _reloadRegistry?.Invoke();
    }

    private static bool Same(byte[]? a, byte[]? b) =>
        a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);

    // ----- small helpers ------------------------------------------------------------------------

    private static void CopyIfExists(string source, string destination)
    {
        if (!File.Exists(source))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    private static void TryDeleteTree(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private readonly record struct Stamp(long Length, DateTime WriteUtc);

    private sealed record Open(
        string Label, ISessionState? State, byte[]? Registry, HashSet<string> Directories, List<string> Kept);

    private sealed record Entry(
        string Label,
        string Folder,
        string Before,
        string After,
        IReadOnlyList<string> Paths,
        IReadOnlyList<string> CreatedRoots,
        ISessionState? StateBefore,
        ISessionState? StateAfter,
        byte[]? RegistryBefore,
        byte[]? RegistryAfter,
        bool RegistryChanged,
        IReadOnlyList<string> Kept);
}
