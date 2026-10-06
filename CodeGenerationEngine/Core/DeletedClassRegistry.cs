using System.Text.Json;
using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

/// <summary>
/// The deleted classes of a workspace, in <c>deleted-classes.json</c> at its root.
/// </summary>
/// <remarks>
/// One entry per deleted copy, holding where each of its files was and where its copy is kept, so a
/// restore puts back exactly that copy. Entries written before the kept path was recorded have no
/// <see cref="DeletedClassInfo.DeletedPath"/>; <see cref="ClassRestoreService"/> finds their file by
/// name. Times are stored in UTC and shown in local time.
/// </remarks>
public static class DeletedClassRegistry
{
    private const string RegistryFileName = "deleted-classes.json";

    public static void Register(string workspaceRoot, DeletedClassInfo entry)
    {
        var items = Load(workspaceRoot);
        items.Add(entry);
        Save(workspaceRoot, items);
    }

    public static void Register(string workspaceRoot, string className, string originalPath) =>
        Register(workspaceRoot, new DeletedClassInfo
        {
            ClassName = className,
            OriginalPath = originalPath,
            DeletedAt = DateTime.UtcNow,
        });

    /// <summary>The deleted copies of a class, newest first.</summary>
    public static List<DeletedClassInfo> Get(string workspaceRoot, string className) =>
        [.. Load(workspaceRoot)
            .Where(x => x.ClassName.Equals(className, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.DeletedAt)];

    /// <summary>Removes every deleted copy of a class from the registry.</summary>
    public static void Remove(string workspaceRoot, string className)
    {
        var items = Load(workspaceRoot);
        items.RemoveAll(x => x.ClassName.Equals(className, StringComparison.OrdinalIgnoreCase));
        Save(workspaceRoot, items);
    }

    /// <summary>Removes the one entry that describes <paramref name="entry"/>'s copy.</summary>
    public static void Remove(string workspaceRoot, DeletedClassInfo entry)
    {
        var items = Load(workspaceRoot);
        var index = items.FindIndex(x => Same(x, entry));
        if (index < 0)
            return;

        items.RemoveAt(index);
        Save(workspaceRoot, items);
    }

    public static List<DeletedClassInfo> GetAll(string workspaceRoot) =>
        [.. Load(workspaceRoot).OrderByDescending(x => x.DeletedAt)];

    public static DeletedClassInfo? GetLast(string workspaceRoot) =>
        Load(workspaceRoot).OrderByDescending(x => x.DeletedAt).FirstOrDefault();

    private static bool Same(DeletedClassInfo a, DeletedClassInfo b) =>
        a.ClassName.Equals(b.ClassName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.OriginalPath, b.OriginalPath, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.DeletedPath, b.DeletedPath, StringComparison.OrdinalIgnoreCase)
        && a.DeletedAt.ToUniversalTime() == b.DeletedAt.ToUniversalTime();

    private static List<DeletedClassInfo> Load(string workspaceRoot)
    {
        var registryPath = Path.Combine(workspaceRoot, RegistryFileName);
        if (!File.Exists(registryPath))
            return [];

        var items = JsonSerializer.Deserialize<List<DeletedClassInfo>>(File.ReadAllText(registryPath)) ?? [];

        // Always written from UtcNow; an entry read back without its zone is UTC as well.
        foreach (var item in items)
        {
            item.DeletedAt = item.DeletedAt.Kind switch
            {
                DateTimeKind.Unspecified => DateTime.SpecifyKind(item.DeletedAt, DateTimeKind.Utc),
                DateTimeKind.Local => item.DeletedAt.ToUniversalTime(),
                _ => item.DeletedAt,
            };
            item.Parts ??= [];
        }

        return items;
    }

    private static void Save(string workspaceRoot, List<DeletedClassInfo> items) =>
        File.WriteAllText(
            Path.Combine(workspaceRoot, RegistryFileName),
            JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
}
