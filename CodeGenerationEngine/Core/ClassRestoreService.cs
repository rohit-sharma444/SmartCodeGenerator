namespace CodeGenerationEngine.Core;

/// <summary>
/// Puts a deleted class's files back where they were.
/// </summary>
/// <remarks>
/// One deleted copy per restore, the newest first, and exactly that copy's own files. It used to
/// take, for every entry, the first <c>Name_*.cs</c> anywhere in the workspace and move it with
/// <c>overwrite: true</c>: a live class of the same name was silently replaced, a second deleted
/// copy was moved over the first and lost, both entries were removed, and with the class deleted
/// from two projects either could receive the other's file. Now a restore refuses while a live
/// class has the name, and never writes over a file.
/// </remarks>
public static class ClassRestoreService
{
    public static (bool Success, string Message) RestoreClass(string workspaceRoot, string className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return (false, "Class name not provided.");

        if (!Directory.Exists(workspaceRoot))
            return (false, "Workspace not found.");

        var entries = DeletedClassRegistry.Get(workspaceRoot, className);

        if (entries.Count == 0)
        {
            var live = ClassFiles.FileOf(workspaceRoot, className);
            return (false, live != null
                ? $"Class '{className}' already exists ({Path.GetRelativePath(workspaceRoot, live)}) and has no deleted copy to restore."
                : $"No deleted copy of class '{className}' to restore. 'list deleted classes' shows what can be restored.");
        }

        var entry = entries[0];
        var name = entry.ClassName;

        var kept = entry.DeletedPath;
        if (string.IsNullOrWhiteSpace(kept) || !File.Exists(kept))
            kept = LegacyCopy(workspaceRoot, entry);

        if (kept == null)
            return (false, $"The deleted copy of '{name}' from {entry.DeletedAt.ToLocalTime():g} is missing from DeletedClasses, "
                + $"so it cannot be restored. 'list deleted classes' shows the others.");

        // A live class of that name in the same project: putting the copy back would overwrite it
        // or declare the class twice.
        var projectDir = ProjectFileHelper.FindProjectDirectory(entry.OriginalPath)
            ?? Path.GetDirectoryName(entry.OriginalPath)!;
        var clash = Directory.Exists(projectDir) ? ClassFiles.FileOf(projectDir, name) : null;
        clash ??= File.Exists(entry.OriginalPath) ? entry.OriginalPath : null;
        if (clash != null)
            return (false, $"Class '{name}' already exists ({Path.GetRelativePath(workspaceRoot, clash)}), so the deleted copy "
                + $"was not restored. Rename the live class first (rename class {name} to <new name>) or delete it, "
                + $"then run restore class {name} again.");

        var moves = new List<(string From, string To)> { (kept, entry.OriginalPath) };
        foreach (var part in entry.Parts)
        {
            if (!File.Exists(part.DeletedPath))
                return (false, $"{Path.GetFileName(part.OriginalPath)}, a part of the deleted '{name}', is missing from DeletedClasses, "
                    + "so it was not restored.");
            if (File.Exists(part.OriginalPath))
                return (false, $"{Path.GetRelativePath(workspaceRoot, part.OriginalPath)} already exists, so the deleted copy of "
                    + $"'{name}' was not restored. Move that file away, then run restore class {name} again.");
            moves.Add((part.DeletedPath, part.OriginalPath));
        }

        try
        {
            foreach (var (from, to) in moves)
            {
                var folder = Path.GetDirectoryName(to);
                if (!string.IsNullOrWhiteSpace(folder))
                    Directory.CreateDirectory(folder);
                File.Move(from, to);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, ex.Message);
        }

        DeletedClassRegistry.Remove(workspaceRoot, entry);

        var older = entries.Count - 1;
        var restored = string.Join(", ", moves.Select(m => Path.GetRelativePath(workspaceRoot, m.To)));
        return (true, $"Class '{name}' restored: {restored}."
            + (older > 0
                ? $" {older} older deleted cop{(older == 1 ? "y" : "ies")} of '{name}' {(older == 1 ? "remains" : "remain")}; 'list deleted classes' shows {(older == 1 ? "it" : "them")}."
                : string.Empty));
    }

    // An entry written before the kept path was recorded: the newest Name_<stamp>.cs in its own
    // project's DeletedClasses folder that no other entry claims.
    private static string? LegacyCopy(string workspaceRoot, DeletedClassInfo entry)
    {
        var projectDir = ProjectFileHelper.FindProjectDirectory(entry.OriginalPath);
        var folder = projectDir == null ? null : Path.Combine(projectDir, "DeletedClasses");
        if (folder == null || !Directory.Exists(folder))
            return null;

        var claimed = DeletedClassRegistry.GetAll(workspaceRoot)
            .SelectMany(e => e.Parts.Select(p => p.DeletedPath).Append(e.DeletedPath))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Directory.GetFiles(folder, $"{entry.ClassName}_*.cs")
            .Concat(Directory.GetFiles(folder, $"{entry.ClassName}_*.cs{ClassDeletionService.KeptExtension}"))
            .Where(f => !claimed.Contains(Path.GetFullPath(f)))
            .OrderByDescending(f => Path.GetFileNameWithoutExtension(f), StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
