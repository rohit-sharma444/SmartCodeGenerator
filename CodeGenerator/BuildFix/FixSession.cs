using System.Text;

namespace CodeGenerator.BuildFix;

internal sealed class FixEdit(string file, string by, int round)
{
    public string File { get; } = file;
    public string By { get; } = by;
    public int Round { get; } = round;
    public bool Reverted { get; set; }
}

/// <summary>
/// The only way a fix touches a file: every path is checked to be inside the generated project (guarding against
/// symlinks and junctions), and the first write to a file in a round copies the original aside so the round can be undone.
/// </summary>
internal sealed class FixSession
{
    private readonly string _backupRoot;
    private readonly Dictionary<int, Dictionary<string, string>> _backups = [];
    private readonly List<FixEdit> _edits = [];
    private int _round;
    private bool _roundStarted;

    public FixSession(string projectRoot)
    {
        Root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var suffix = Guid.NewGuid().ToString("N")[..6];
        _backupRoot = Path.Combine(Root, ".codegen", "fix-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + suffix);
    }

    public string Root { get; }

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
        var old = File.ReadAllBytes(full);

        var backups = _backups[_round];
        if (!backups.ContainsKey(full))
        {
            Directory.CreateDirectory(Path.Combine(_backupRoot, "r" + _round));
            var bak = Path.Combine(_backupRoot, "r" + _round, backups.Count + ".bak");
            File.WriteAllBytes(bak, old);
            backups[full] = bak;
        }

        File.WriteAllText(full, text, new UTF8Encoding(HasBom(old)));
        _edits.Add(new FixEdit(full, by, _round));
    }

    public void RevertRound(int round)
    {
        if (_backups.TryGetValue(round, out var backups))
            foreach (var (file, bak) in backups)
                File.WriteAllBytes(file, File.ReadAllBytes(bak));

        foreach (var edit in _edits.Where(e => e.Round == round))
            edit.Reverted = true;
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
