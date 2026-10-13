using System.Text;

namespace CodeGenerator.BuildFix;

internal sealed class FixEdit(string file, string by, int round)
{
    public string File { get; } = file;
    public string By { get; } = by;
    public int Round { get; } = round;

    /// <summary>For an AI edit: the first changed line and how many lines changed. Null for a rule edit.</summary>
    public EditChange? Change { get; set; }
    private bool _reverted;
    private bool _restoreFailed;

    /// <summary>True when this edit was undone. Never true together with <see cref="RestoreFailed"/>.</summary>
    public bool Reverted
    {
        get => _reverted;
        set
        {
            _reverted = value;
            if (value)
                _restoreFailed = false;
        }
    }

    /// <summary>True when undoing this edit was tried and failed: the file still holds the edit.</summary>
    public bool RestoreFailed
    {
        get => _restoreFailed;
        set
        {
            _restoreFailed = value;
            if (value)
                _reverted = false;
        }
    }
}

/// <summary>The first changed line of an edit (no line ending), and how many lines changed in all.</summary>
internal sealed record EditChange(int Line, string Before, string After, int Lines);

/// <summary>
/// The only way a fix touches a file: every path is checked to be inside the generated project (guarding against
/// symlinks and junctions), and the first write to a file in a round copies the original aside so the round can be undone.
/// </summary>
internal sealed class FixSession
{
    private readonly string _backupRoot;
    private readonly Dictionary<int, Dictionary<string, string>> _backups = [];
    private readonly List<FixEdit> _edits = [];
    private readonly List<byte[]> _before = [];
    private int _round;
    private bool _roundStarted;

    public FixSession(string projectRoot)
    {
        Root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var suffix = Guid.NewGuid().ToString("N")[..6];
        _backupRoot = Path.Combine(Root, ".codegen", "fix-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + suffix);
    }

    public string Root { get; }

    public int CurrentRound => _round;

    public IReadOnlyList<FixEdit> Edits => _edits;

    public void BeginRound(int round)
    {
        _round = round;
        _roundStarted = true;
        _backups.TryAdd(round, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    public string Relative(string path)
        => Path.GetRelativePath(Root, Path.GetFullPath(path));

    public string Read(string path)
    {
        if (!_roundStarted)
            throw new InvalidOperationException("Call BeginRound first");

        var bytes = File.ReadAllBytes(Guard(path));
        var skip = HasBom(bytes) ? 3 : 0;
        return Encoding.UTF8.GetString(bytes, skip, bytes.Length - skip);
    }

    public void Write(string path, string text, string by)
    {
        if (!_roundStarted)
            throw new InvalidOperationException("Call BeginRound first");

        var full = Guard(path);

        if (InGeneratedFolder(Root, full))
            throw new InvalidOperationException($"Refusing to edit '{Relative(full)}': it is in a build or tool folder.");

        var old = File.ReadAllBytes(full);

        var backups = _backups[_round];
        if (!backups.ContainsKey(full))
        {
            Directory.CreateDirectory(Path.Combine(_backupRoot, "r" + _round));
            var index = backups.Count;
            var bak = Path.Combine(_backupRoot, "r" + _round, index + ".bak");
            File.WriteAllBytes(bak, old);

            // The .bak names say nothing about the file: this map does, with relative paths only.
            // A map that cannot be written must never block the edit or lose the original.
            try
            {
                File.AppendAllText(
                    Path.Combine(_backupRoot, BuildFixReport.BackupMapName),
                    $"r{_round}\\{index}.bak = {Relative(full)}{Environment.NewLine}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The backup itself exists and is registered below.
            }

            backups[full] = bak;
        }

        File.WriteAllText(full, text, new UTF8Encoding(HasBom(old)));
        _edits.Add(new FixEdit(full, by, _round));
        _before.Add(old);
    }

    /// <summary>True when a fix may edit this file: inside the project, and not under obj, bin or .codegen.</summary>
    public bool IsEditable(string path) => IsEditableIn(Root, path);

    /// <summary>The same check, for a project no session has been opened on.</summary>
    public static bool IsEditableIn(string projectRoot, string path)
    {
        try
        {
            var root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(path);
            return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !InGeneratedFolder(root, full);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>The number of edits so far; pass it to <see cref="UndoSince"/>.</summary>
    public int Mark() => _edits.Count;

    /// <summary>
    /// Puts back what each edit made after <paramref name="mark"/> replaced, newest first.
    /// Returns the relative paths that could not be restored.
    /// </summary>
    public IReadOnlyList<string> UndoSince(int mark)
    {
        var failed = new List<string>();

        for (var i = _edits.Count - 1; i >= mark; i--)
        {
            if (_edits[i].Reverted)
                continue;

            try
            {
                File.WriteAllBytes(_edits[i].File, _before[i]);
                _edits[i].Reverted = true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _edits[i].RestoreFailed = true;
                failed.Add(Relative(_edits[i].File));
            }
        }

        return failed.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Restores every file the round changed. One file that cannot be restored does not stop the
    /// others; it is returned (relative path) and its edits stay marked as not reverted.
    /// </summary>
    public IReadOnlyList<string> RevertRound(int round)
    {
        var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_backups.TryGetValue(round, out var backups))
            foreach (var (file, bak) in backups)
            {
                try
                {
                    File.WriteAllBytes(file, File.ReadAllBytes(bak));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failed.Add(file);
                }
            }

        foreach (var edit in _edits.Where(e => e.Round == round && !e.Reverted))
        {
            if (failed.Contains(edit.File))
                edit.RestoreFailed = true;
            else
                edit.Reverted = true;
        }

        return failed.Select(Relative).ToList();
    }

    private static bool InGeneratedFolder(string root, string full)
    {
        var parts = Path.GetRelativePath(root, full).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return parts[..^1].Any(p => p.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || p.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || p.Equals(".codegen", StringComparison.OrdinalIgnoreCase));
    }

    private string Guard(string path)
    {
        var full = Path.GetFullPath(path);

        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to edit '{path}': it is outside the generated project.");

        // Check for junctions and symlinks from the file (if it exists) up to (but not including) Root.
        var toCheck = File.Exists(full) ? full : Path.GetDirectoryName(full);
        while (toCheck != null && toCheck.Length > Root.Length)
        {
            if (new FileInfo(toCheck).Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException($"Refusing to edit '{path}': it passes through a junction or symlink.");

            var parent = Path.GetDirectoryName(toCheck);
            if (parent == toCheck) break; // Reached filesystem root
            toCheck = parent;
        }

        return full;
    }

    private static bool HasBom(byte[] b) => b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;
}
