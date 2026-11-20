using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

/// <summary>How much a workspace snapshot would copy.</summary>
/// <param name="FileCount">Files that would be captured, after exclusions.</param>
/// <param name="Bytes">Their total size in bytes.</param>
public sealed record SnapshotSizing(int FileCount, long Bytes);

/// <summary>
/// A point-in-time copy of the workspace, the project registry, and the
/// in-memory state a command can mutate. Used to make a batch of commands
/// all-or-nothing: capture before the first action, restore if any fails.
/// </summary>
/// <remarks>
/// Every command handler bottoms out in the filesystem, so one snapshot is
/// generically correct for all of them — present and future — where an undo log
/// would need a separate inverse per intent.
/// </remarks>
public sealed class WorkspaceSnapshot : IDisposable
{
    // .vs and .idea are an IDE's live state - SQLite with -wal and -shm, .vsidx, .dtbcache - that
    // it writes while a command runs: journalled, an undo refused over them or copied stale bytes
    // over open files, a rollback deleted -wal and -shm, a locked .vsidx made every command
    // Unrecorded, and their size switched undo off. .vscode holds the user's settings, so stays.
    internal static readonly string[] ExcludedDirectories =
        ["bin", "obj", ".git", "TestResults", "node_modules", ".vs", ".idea"];

    /// <summary>The most files, and bytes, worth copying for a rollback: batch asks first above
    /// these, and undo switches itself off.</summary>
    public const int MaxFiles = 5_000;
    public const long MaxBytes = 200L * 1024 * 1024;

    private const string WorkspaceFolder = "workspace";

    /// <summary>How a snapshot folder in the temp directory is named: this, the process id, '_',
    /// and a random name — so a later session can tell a copy whose owner was killed from one a
    /// running session still needs (see <c>UndoJournal.RemoveStale</c>).</summary>
    public const string FolderPrefix = "codegen_snapshot_";

    /// <summary>A file in a snapshot kept because putting it back failed: the user may still need
    /// it, so no clean-up deletes it.</summary>
    public const string KeptMarker = "KEPT-restore-failed.txt";
    private const string RegistryFileName = "ProjectManager.dat";
    private const string RegistryBackupName = "ProjectManager.dat.bak";

    private readonly string _workspaceRoot;
    private readonly ProjectMetaData? _currentProject;
    private readonly ParsingStateSnapshot _parsingState;
    private HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private bool _restoreFailed;

    // Build output a command can leave in a folder it created. Never .git or node_modules: those
    // are someone's work, not something a build regenerates.
    private static readonly string[] BuildOutput = ["bin", "obj", "TestResults"];

    /// <summary>The temp directory holding the captured copy.</summary>
    public string SnapshotPath { get; }

    private WorkspaceSnapshot(
        string workspaceRoot,
        string snapshotPath,
        ProjectMetaData? currentProject,
        ParsingStateSnapshot parsingState)
    {
        _workspaceRoot = workspaceRoot;
        SnapshotPath = snapshotPath;
        _currentProject = currentProject;
        _parsingState = parsingState;
    }

    /// <summary>
    /// Counts the files a capture would copy, without copying them.
    /// </summary>
    public static SnapshotSizing Measure(string workspaceRoot)
    {
        int count = 0;
        long bytes = 0;

        foreach (var file in EnumerateIncludedFiles(workspaceRoot))
        {
            count++;
            bytes += new FileInfo(file).Length;
        }

        return new SnapshotSizing(count, bytes);
    }

    /// <summary>
    /// Copies the workspace, the project registry, and the in-memory context to
    /// a temp directory. Throws if the copy cannot be made; the caller must then
    /// abort before executing anything.
    /// </summary>
    public static WorkspaceSnapshot Capture(string workspaceRoot)
    {
        var snapshotPath = Path.Combine(
            Path.GetTempPath(), $"{FolderPrefix}{Environment.ProcessId}_{Path.GetRandomFileName()}");

        try
        {
            Directory.CreateDirectory(Path.Combine(snapshotPath, WorkspaceFolder));

            foreach (var file in EnumerateIncludedFiles(workspaceRoot))
            {
                var relative = Path.GetRelativePath(workspaceRoot, file);
                var destination = Path.Combine(snapshotPath, WorkspaceFolder, relative);

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true);
            }

            // The project registry lives next to the exe, not in the workspace.
            var registry = Path.Combine(AppContext.BaseDirectory, RegistryFileName);
            if (File.Exists(registry))
                File.Copy(registry, Path.Combine(snapshotPath, RegistryBackupName), overwrite: true);

            return new WorkspaceSnapshot(
                workspaceRoot,
                snapshotPath,
                ProjectManager.CurrentProject,
                ParsingStateSnapshot.Capture())
            {
                _directories = IncludedDirectories(workspaceRoot),
            };
        }
        catch
        {
            TryDelete(snapshotPath);
            throw;
        }
    }

    /// <summary>
    /// Puts the workspace back exactly as it was. On failure the snapshot
    /// directory is retained — see <see cref="Dispose"/>.
    /// </summary>
    public void Restore()
    {
        try
        {
            var capturedRoot = Path.Combine(SnapshotPath, WorkspaceFolder);

            var captured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in EnumerateIncludedFiles(capturedRoot))
                captured[Path.GetRelativePath(capturedRoot, file)] = file;

            // A file that cannot be put back is named at the end, and the others are still put
            // back: the first failure used to stop the restore, so a data.csv a viewer held open,
            // which no command had touched, kept the class the script really changed as it was.
            var notRestored = new List<string>();

            // 1. Delete what the batch added. Removes `dotnet new` project trees.
            foreach (var file in EnumerateIncludedFiles(_workspaceRoot).ToList())
            {
                var relative = Path.GetRelativePath(_workspaceRoot, file);
                if (!captured.ContainsKey(relative))
                    PutBack(relative, () => File.Delete(file));
            }

            // 2 & 3. Overwrite what changed, recreate what was deleted. A file whose bytes are the
            // captured ones is left alone, so an open file nothing changed is never written.
            foreach (var (relative, source) in captured)
            {
                var destination = Path.Combine(_workspaceRoot, relative);
                PutBack(relative, () =>
                {
                    if (SameBytes(source, destination))
                        return;

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(source, destination, overwrite: true);
                });
            }

            void PutBack(string relative, Action action)
            {
                try
                {
                    action();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    notRestored.Add($"{relative} ({ex.Message})");
                }
            }

            // 4. Directories the batch created are now empty — but for the build output a build
            // inside one left there, which would otherwise keep the folder, and the undo history
            // would then offer an entry that only removes it.
            var createdDirectories = IncludedDirectories(_workspaceRoot)
                .Except(_directories, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var created in createdDirectories)
                foreach (var name in BuildOutput)
                {
                    var output = Path.Combine(_workspaceRoot, created, name);
                    if (Directory.Exists(output))
                        Directory.Delete(output, recursive: true);
                }

            // Only the directories the batch created, deepest first, and only when empty. Every
            // empty directory used to go, the user's own among them: a Visual Studio
            // <Folder Include="Migrations\" />, an empty wwwroot/images.
            foreach (var created in createdDirectories.OrderByDescending(Depth))
            {
                var path = Path.Combine(_workspaceRoot, created);
                if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                    Directory.Delete(path);
            }

            // An empty directory the batch deleted: no file brought it back.
            foreach (var existing in _directories)
                Directory.CreateDirectory(Path.Combine(_workspaceRoot, existing));

            // 5. Registry file, then reload it into memory.
            var registry = Path.Combine(AppContext.BaseDirectory, RegistryFileName);
            var backup = Path.Combine(SnapshotPath, RegistryBackupName);

            if (File.Exists(backup))
                File.Copy(backup, registry, overwrite: true);
            else if (File.Exists(registry))
                File.Delete(registry);

            // As startup lists them: this workspace's only, and the ones on disk too.
            ProjectManager.ReloadWithin(_workspaceRoot);

            // 6 & 7. In-memory state a handler may have set.
            ProjectManager.CurrentProject = _currentProject;
            _parsingState.Restore();

            // The validation rules file is back in .codegen/; the registry still holds what the
            // rolled-back commands added, and would save it over the file on the next change.
            ValidationRegistry.ReloadFromDisk();

            // learned.json is back, but the store that writes it is a process-wide
            // singleton still holding what the rolled-back commands taught it. The
            // next Save() would put all of it back on disk.
            LearnedIntentStore.ReloadFromDisk();

            // The rephrase heuristic compares against the last command executed —
            // which, after a rollback, never happened.
            LearningCoordinator.ResetHistory();

            if (notRestored.Count > 0)
                throw new IOException("These files could not be put back: " + string.Join("; ", notRestored));
        }
        catch
        {
            _restoreFailed = true;
            try
            {
                File.WriteAllText(Path.Combine(SnapshotPath, KeptMarker),
                    $"Putting {_workspaceRoot} back failed. This copy of it was kept so it can be recovered by hand.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            throw;
        }
    }

    /// <summary>
    /// Deletes the captured copy — unless <see cref="Restore"/> failed, in which
    /// case it is kept so the user can recover by hand.
    /// </summary>
    public void Dispose()
    {
        if (_restoreFailed)
            return;

        TryDelete(SnapshotPath);
    }

    private static IEnumerable<string> EnumerateIncludedFiles(string directory) => IncludedFiles(directory);

    /// <summary>
    /// Every file under <paramref name="directory"/> outside the excluded folders — and outside
    /// <paramref name="alsoExclude"/>, folder names skipped at any depth.
    /// </summary>
    /// <remarks>Shared with the undo journal, so the two agree on what "the workspace" is.</remarks>
    internal static IEnumerable<string> IncludedFiles(string directory, IEnumerable<string>? alsoExclude = null)
    {
        var excluded = new HashSet<string>(ExcludedDirectories, StringComparer.OrdinalIgnoreCase);
        if (alsoExclude is not null)
            excluded.UnionWith(alsoExclude);

        return Walk(directory);

        IEnumerable<string> Walk(string current)
        {
            foreach (var file in Directory.EnumerateFiles(current))
                yield return file;

            foreach (var subdirectory in Directory.EnumerateDirectories(current))
            {
                if (excluded.Contains(Path.GetFileName(subdirectory)))
                    continue;

                foreach (var file in Walk(subdirectory))
                    yield return file;
            }
        }
    }

    // Every directory under the root, relative, outside the excluded folders.
    private static HashSet<string> IncludedDirectories(string root)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>([root]);

        while (pending.Count > 0)
            foreach (var directory in Directory.EnumerateDirectories(pending.Pop()))
            {
                if (ExcludedDirectories.Contains(Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase))
                    continue;
                result.Add(Path.GetRelativePath(root, directory));
                pending.Push(directory);
            }

        return result;
    }

    // Whether the file at destination holds the bytes of source: length first, then content.
    private static bool SameBytes(string source, string destination)
    {
        if (!File.Exists(destination))
            return false;

        if (new FileInfo(source).Length != new FileInfo(destination).Length)
            return false;

        return File.ReadAllBytes(source).AsSpan().SequenceEqual(File.ReadAllBytes(destination));
    }

    private static int Depth(string relative) =>
        relative.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // A leaked temp directory is not worth failing a batch over.
        }
    }
}
