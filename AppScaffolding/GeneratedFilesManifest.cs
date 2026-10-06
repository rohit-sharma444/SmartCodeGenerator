using System.Security.Cryptography;

namespace AppScaffolding;

/// <summary>
/// The files the scaffolder wrote into one project folder, each with a hash of what was written.
/// </summary>
/// <remarks>
/// Overwriting an app replaces its generated sources, but until this existed it only wrote the
/// files the new model produces: a class renamed or removed on Review left its
/// <c>Models/&lt;Old&gt;.cs</c>, <c>Data/&lt;Old&gt;Repository.cs</c> and validator halves behind.
/// After a rename to a name that could not compile, no later overwrite could ever build again,
/// because nothing removed the bad files.
/// <para>
/// A folder holds user files too, and nothing in a file's name says who wrote it — so the record
/// is kept at generation time rather than guessed from patterns. A file is removed only if it is
/// in the record AND still holds exactly what was written: a file the user has changed since is
/// theirs, and is kept and reported. A file the scaffolder never wrote is never touched.
/// </para>
/// <para>
/// Kept under <c>.codegen/</c> inside the project folder, which the .NET SDK leaves out of every
/// default item glob (dot-folders are excluded), so the record is never compiled, copied or
/// published. An app generated before the record existed has none, and nothing is removed from
/// it.
/// </para>
/// </remarks>
public static class GeneratedFilesManifest
{
    public const string FolderName = ".codegen";

    public const string FileName = "generated-files.txt";

    /// <summary>Where the record for <paramref name="projectFolder"/> lives.</summary>
    public static string PathFor(string projectFolder)
        => Path.Combine(projectFolder, FolderName, FileName);

    /// <summary>
    /// Removes what an earlier run generated and this run did not, then records this run's files.
    /// </summary>
    /// <param name="projectFolder">The project folder the record belongs to.</param>
    /// <param name="writtenNow">Every file this run wrote into the folder.</param>
    /// <param name="keptNow">
    /// Files this run deliberately left as they were and still owns — the write-once validator
    /// stubs of classes the model still has. Carried forward under their original hash.
    /// </param>
    public static SweepResult Sweep(
        string projectFolder, IEnumerable<string> writtenNow, IEnumerable<string> keptNow)
    {
        var previous = Read(projectFolder);
        var now = Hashes(projectFolder, writtenNow);

        foreach (var kept in keptNow)
        {
            var key = Key(projectFolder, kept);
            if (key is not null && !now.ContainsKey(key) && previous.TryGetValue(key, out var hash))
                now[key] = hash;
        }

        var removed = new List<string>();
        var changed = new List<string>();

        foreach (var (key, hash) in previous)
        {
            if (now.ContainsKey(key))
                continue;

            var path = Inside(projectFolder, key);
            if (path is null || !File.Exists(path))
                continue;

            if (!string.Equals(HashOf(path), hash, StringComparison.Ordinal))
            {
                changed.Add(key);
                continue;
            }

            try
            {
                File.Delete(path);
                removed.Add(key);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left in the record, so the next overwrite tries again.
                now[key] = hash;
            }
        }

        Write(projectFolder, now);

        return new SweepResult(removed, changed);
    }

    /// <summary>
    /// Adds <paramref name="written"/> to the record without removing anything — for a run that
    /// wrote only some of an app (an incremental add, or a generate that could not write every
    /// file, where a missing file says nothing about what the model no longer has).
    /// </summary>
    public static void Record(string projectFolder, IEnumerable<string> written)
    {
        var record = Read(projectFolder);

        foreach (var (key, hash) in Hashes(projectFolder, written))
            record[key] = hash;

        Write(projectFolder, record);
    }

    /// <summary>The record as relative path to hash. Empty when there is none, or it is unreadable.</summary>
    public static Dictionary<string, string> Read(string projectFolder)
    {
        var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var path = PathFor(projectFolder);

        try
        {
            if (!File.Exists(path))
                return record;

            foreach (var line in File.ReadAllLines(path))
            {
                var tab = line.IndexOf('\t');
                if (tab <= 0 || tab == line.Length - 1)
                    continue;

                record[line[(tab + 1)..]] = line[..tab];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable record removes nothing, which is the safe way to be wrong.
            record.Clear();
        }

        return record;
    }

    private static Dictionary<string, string> Hashes(string projectFolder, IEnumerable<string> files)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var key = Key(projectFolder, file);
            if (key is null || !File.Exists(file))
                continue;

            try
            {
                hashes[key] = HashOf(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not recorded, so never removed.
            }
        }

        return hashes;
    }

    private static void Write(string projectFolder, Dictionary<string, string> record)
    {
        try
        {
            var path = PathFor(projectFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            File.WriteAllLines(
                path,
                record.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(e => $"{e.Value}\t{e.Key}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort, like every other write here: without a record, the next overwrite
            // removes nothing rather than the wrong thing.
        }
    }

    /// <summary>
    /// <paramref name="file"/> relative to the project folder with forward slashes, or null when
    /// it is not inside it (the test project is a sibling and keeps a record of its own).
    /// </summary>
    private static string? Key(string projectFolder, string file)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(projectFolder), Path.GetFullPath(file));

        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return null;

        return relative.Replace('\\', '/');
    }

    /// <summary>
    /// The full path a recorded key names, or null when it would leave the project folder — a
    /// hand-edited record must not be able to aim a delete anywhere else.
    /// </summary>
    private static string? Inside(string projectFolder, string key)
    {
        var root = Path.GetFullPath(projectFolder);
        var path = Path.GetFullPath(Path.Combine(root, key));

        return Key(root, path) is null ? null : path;
    }

    private static string HashOf(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}

/// <summary>What <see cref="GeneratedFilesManifest.Sweep"/> did, as paths relative to the project
/// folder.</summary>
/// <param name="Removed">Files an earlier run generated that this model no longer produces.</param>
/// <param name="Changed">
/// Files an earlier run generated that this model no longer produces, kept because they were
/// changed after they were written.
/// </param>
public sealed record SweepResult(IReadOnlyList<string> Removed, IReadOnlyList<string> Changed)
{
    /// <summary>One sentence for each kind of thing done, for the generate report. Empty when the
    /// sweep did nothing.</summary>
    public IReadOnlyList<string> Sentences(string projectName)
    {
        var lines = new List<string>();

        if (Removed.Count > 0)
            lines.Add($"Removed {Removed.Count} file(s) from {projectName} that an earlier generate "
                + $"wrote for classes no longer in the model: {string.Join(", ", Removed)}");

        if (Changed.Count > 0)
            lines.Add($"Kept {string.Join(", ", Changed)} in {projectName}: generated for classes no "
                + "longer in the model, but changed since, so remove them by hand if they are not wanted");

        return lines;
    }
}
