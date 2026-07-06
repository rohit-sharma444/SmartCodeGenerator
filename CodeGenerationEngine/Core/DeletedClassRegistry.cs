using System.Text.Json;
using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Registry for tracking deleted classes during the refactoring process.
/// </summary>
public static class DeletedClassRegistry
{
    private const string RegistryFileName = "deleted-classes.json";

    public static void Register(
        string workspaceRoot,
        string className,
        string originalPath)
    {
        var registryPath = Path.Combine(
            workspaceRoot,
            RegistryFileName);

        var items = Load(workspaceRoot);

        items.Add(new DeletedClassInfo
        {
            ClassName = className,
            OriginalPath = originalPath,
            DeletedAt = DateTime.UtcNow
        });

        Save(registryPath, items);
    }

    public static List<DeletedClassInfo> Get(
        string workspaceRoot,
        string className)
    {
        return Load(workspaceRoot)
            .Where(x => x.ClassName.Equals(
                className,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.DeletedAt)
            .ToList();
    }

    public static void Remove(
        string workspaceRoot,
        string className)
    {
        var registryPath = Path.Combine(
            workspaceRoot,
            RegistryFileName);

        var items = Load(workspaceRoot);

        items.RemoveAll(x =>
            x.ClassName.Equals(
                className,
                StringComparison.OrdinalIgnoreCase));

        Save(registryPath, items);
    }

    private static List<DeletedClassInfo> Load(string workspaceRoot)
    {
        var registryPath = Path.Combine(
            workspaceRoot,
            RegistryFileName);

        if (!File.Exists(registryPath))
            return [];

        var json = File.ReadAllText(registryPath);

        return JsonSerializer.Deserialize<List<DeletedClassInfo>>(json)
               ?? [];
    }

    private static void Save(string path, List<DeletedClassInfo> items)
    {
        var json = JsonSerializer.Serialize(items,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        File.WriteAllText(path, json);
    }

    public static List<DeletedClassInfo> GetAll(
        string workspaceRoot)
    {
        return [.. Load(workspaceRoot).OrderByDescending(x => x.DeletedAt)];
    }

    public static DeletedClassInfo? GetLast(
        string workspaceRoot)
    {
        return Load(workspaceRoot)
            .OrderByDescending(x => x.DeletedAt)
            .FirstOrDefault();
    }
}