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

    // Nor is what was typed: every command appends to it, a script's build step included, and so
    // does typing undo itself — so journalling it would make every such undo refuse.
    private static readonly string[] ExcludedFiles = [".codegen_history"];

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

    // Files that could not be read into the shadow (a running app's log or database held open):
    // left out of every entry, and tried again before each command. Each is named once.
    private readonly HashSet<string> _uncovered = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _named = new(StringComparer.OrdinalIgnoreCase);

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
        return new UndoResult(true, "Undid: " + entry.Label + Describe(entry, entry.Before, entry.After, redo: false));
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
        return new UndoResult(true, "Redid: " + entry.Label + Describe(entry, entry.After, entry.Before, redo: true));
    }

    private const string ShadowPrefix = "codegen_undo_";

    /// <summary>
    /// Deletes the shadow copies — and the batch, run and build snapshots
    /// (<see cref="WorkspaceSnapshot"/>) — whose session was killed before it could delete its own:
    /// those named after a process that is no longer running. A copy named after a running process,
    /// with no process in its name (from an older version), or kept because putting it back failed,
    /// is never touched.
    /// </summary>
    /// <returns>How many copies were deleted.</returns>
    public static int RemoveStale(string? parent = null, Func<int, bool>? isRunning = null)
    {
        isRunning ??= IsRunning;
        var removed = 0;

        try
        {
            foreach (var (folder, prefix) in new[] { ShadowPrefix, WorkspaceSnapshot.FolderPrefix }
                         .SelectMany(prefix => Directory.EnumerateDirectories(parent ?? Path.GetTempPath(), prefix + "*")
                             .Select(folder => (folder, prefix))))
            {
                if (File.Exists(Path.Combine(folder, WorkspaceSnapshot.KeptMarker)))
                    continue;

                var rest = Path.GetFileName(folder)[prefix.Length..];
                var underscore = rest.IndexOf('_');
                if (underscore <= 0 || !int.TryParse(rest[..underscore], out var pid))
                    continue;
                if (pid == Environment.ProcessId || isRunning(pid))
                    continue;

                TryDeleteTree(folder);
                if (!Directory.Exists(folder))
                    removed++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Housekeeping: a folder that cannot be listed is left for next time.
        }

        return removed;
    }

    // A process this one may not inspect (another user's) counts as running: never guess a copy
    // is unowned.
    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (Exception)
        {
            return true;
        }
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

            // A file the shadow could not read is not undoable, so it is not part of the entry.
            changed.RemoveAll(_uncovered.Contains);

            // A file written again with the same bytes (a csproj saved unchanged) has a new stamp
            // but nothing to undo; counting it would only inflate the entry and its message.
            changed.RemoveAll(p => SameBytes(Path.Combine(_shadow!, "files", p), Path.Combine(WorkspaceRoot, p)));

            var createdRoots = Roots(Directories().Except(open.Directories, StringComparer.OrdinalIgnoreCase));

            var stateAfter = _captureState?.Invoke();
            var stateChanged = open.State is not null && stateAfter is not null && !open.State.SameAs(stateAfter);

            var registryAfter = ReadRegistry();
            var registryChanged = !Same(open.Registry, registryAfter);

            if (changed.Count == 0 && createdRoots.Count == 0 && !stateChanged && !registryChanged)
            {
                // What differs is only stamps over the same bytes; knowing them keeps the next
                // command from being credited with them.
                _known = now;
                return;
            }

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
        var files = Files(WorkspaceRoot).ToList();
        var bytes = files.Sum(f => new FileInfo(f).Length);

        if (files.Count > _maxFiles || bytes > _maxBytes)
        {
            _started = true;
            Enabled = false;
            _notify?.Invoke(OffMessage);
            return false;
        }

        // Named after this process, so a later session can tell a copy whose owner was killed
        // from one a running session still needs. See RemoveStale.
        _shadow = Path.Combine(_shadowParent, $"{ShadowPrefix}{Environment.ProcessId}_{Path.GetRandomFileName()}");
        try
        {
            Directory.CreateDirectory(Path.Combine(_shadow, "files"));

            _known = Scan();
            foreach (var path in _known.Keys)
                TryMirror(path);
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
            if (!_known.TryGetValue(path, out var known) || known != stamp || _uncovered.Contains(path))
                TryMirror(path);

        _uncovered.RemoveWhere(p => !now.ContainsKey(p));

        foreach (var path in _known.Keys.Where(p => !now.ContainsKey(p)))
            Mirror(path);

        _known = now;
    }

    // Mirror, but a file that cannot be read is left out of the shadow and named once, rather
    // than failing the whole copy: one locked app.log used to make every command unrecorded, so
    // undo said "Nothing to undo." with no word why, and the full copy was tried again each time.
    private void TryMirror(string path)
    {
        try
        {
            Mirror(path);
            _uncovered.Remove(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var copy = Path.Combine(_shadow!, "files", path);
            try
            {
                if (File.Exists(copy))
                    File.Delete(copy);
            }
            catch (Exception again) when (again is IOException or UnauthorizedAccessException)
            {
                // A stale copy is never used: the path is left out of every entry.
            }

            _uncovered.Add(path);
            if (_named.Add(path))
                _notify?.Invoke($"Undo cannot cover {path}: it could not be read ({ex.Message}). "
                    + "Changes to it cannot be undone; undo still covers the other files.");
        }
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
        Files(WorkspaceRoot)
            .Select(f => new FileInfo(f))
            .ToDictionary(
                f => Path.GetRelativePath(WorkspaceRoot, f.FullName),
                f => new Stamp(f.Length, f.LastWriteTimeUtc),
                StringComparer.OrdinalIgnoreCase);

    // Every directory under the root, relative, skipping (and not listing) excluded ones.
    private HashSet<string> Directories()
    {
        // The snapshot's own list, so the two never disagree about what the workspace is: this
        // one once lacked .vs while the file walk had it.
        var excluded = new HashSet<string>(
            [.. WorkspaceSnapshot.ExcludedDirectories, .. AlsoExcluded], StringComparer.OrdinalIgnoreCase);
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

            var stranger = Files(full)
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
        {
            CopyIfExists(version, target);

            // A copy keeps the version's own write time, older than whatever was built from the
            // file it replaces — and an incremental build would skip compiling it.
            File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
        }
        else if (File.Exists(target))
            File.Delete(target);

        Mirror(path);

        var info = new FileInfo(target);
        if (info.Exists)
            _known[path] = new Stamp(info.Length, info.LastWriteTimeUtc);
        else
            _known.Remove(path);
    }

    // What the user did, in files: the files they asked for named or counted, and a project or
    // solution file the command also touched (a package reference) mentioned on its own, so
    // undoing "create class Foo" does not read "restored 1 file, removed 1 file" about a .csproj.
    // A file a redo writes back that the command had created is "re-created", not "restored".
    private static string Describe(Entry entry, string side, string otherSide, bool redo)
    {
        var projectFiles = entry.Paths.Where(IsProjectFile).ToList();
        var files = entry.Paths.Where(p => !IsProjectFile(p)).ToList();

        bool OnSide(string p) => File.Exists(Path.Combine(side, p));
        bool OnOther(string p) => File.Exists(Path.Combine(otherSide, p));

        var parts = new List<string>();

        if (files.Count == 1)
        {
            var path = files[0];
            parts.Add((!OnSide(path) ? "removed " : redo && !OnOther(path) ? "re-created " : "restored ") + path);
        }
        else if (files.Count > 1)
        {
            var recreated = redo ? files.Count(p => OnSide(p) && !OnOther(p)) : 0;
            var restored = files.Count(OnSide) - recreated;
            var removed = files.Count - restored - recreated;

            var counts = new List<string>();
            if (restored > 0)
                counts.Add($"restored {Files(restored)}");
            if (recreated > 0)
                counts.Add($"re-created {Files(recreated)}");
            if (removed > 0)
                counts.Add($"removed {Files(removed)}");
            parts.Add(string.Join(", ", counts));
        }

        foreach (var group in projectFiles.GroupBy(p =>
                     !OnSide(p) ? "removed"
                     : !OnOther(p) ? (redo ? "re-created" : "restored")
                     : redo ? "updated again" : "put back as it was"))
            parts.Add($"{string.Join(", ", group.Select(Path.GetFileName))} {group.Key}");

        return parts.Count == 0 ? string.Empty : " — " + string.Join("; ", parts);

        static string Files(int n) => $"{n} file{(n == 1 ? "" : "s")}";
    }

    private static bool IsProjectFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".csproj" or ".sln" or ".slnx" or ".props" or ".targets";

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

    private static bool SameBytes(string a, string b)
    {
        var first = new FileInfo(a);
        var second = new FileInfo(b);
        return first.Exists && second.Exists && first.Length == second.Length
            && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }

    private static bool Same(byte[]? a, byte[]? b) =>
        a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);

    // ----- small helpers ------------------------------------------------------------------------

    private static IEnumerable<string> Files(string directory) =>
        WorkspaceSnapshot.IncludedFiles(directory, AlsoExcluded)
            .Where(f => !ExcludedFiles.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase));

    private static void CopyIfExists(string source, string destination)
    {
        if (!File.Exists(source))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        // With the source's own spelling: the paths compare without case, so an undo of a rename
        // that changed only the case put the old text back under the new name.
        FileNameCase.Copy(source, destination);
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
