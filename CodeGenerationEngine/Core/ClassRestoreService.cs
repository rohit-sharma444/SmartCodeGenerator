using static System.Net.WebRequestMethods;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Service responsible for restoring deleted class files back to their original location.
/// </summary>
public static class ClassRestoreService
{
    public static (bool Success, string Message) RestoreClass(
        string workspaceRoot,
        string className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return (false, "Class name not provided.");

        if (!Directory.Exists(workspaceRoot))
            return (false, "Workspace not found.");

        var deletedEntries = DeletedClassRegistry.Get(
            workspaceRoot,
            className);

        if (deletedEntries.Count == 0)
            return (false, "Restore metadata not found.");

        var errors = new List<string>();

        foreach (var entry in deletedEntries)
        {
            try
            {
                var deletedFiles = Directory
                    .GetFiles(workspaceRoot, "*.cs", SearchOption.AllDirectories)
                    .Where(f =>
                    f.Contains(
                    "DeletedClasses",
                    StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileNameWithoutExtension(f)
                    .StartsWith(
                    className + "_",
                    StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var deletedFile = deletedFiles.FirstOrDefault();

                if (string.IsNullOrWhiteSpace(deletedFile))
                {
                    errors.Add(
                        $"Deleted file not found for '{entry.OriginalPath}'.");
                    continue;
                }

                var targetDirectory =
                    Path.GetDirectoryName(entry.OriginalPath);

                if (!string.IsNullOrWhiteSpace(targetDirectory))
                    Directory.CreateDirectory(targetDirectory);

                System.IO.File.Move(deletedFile, entry.OriginalPath, true);

                var projectDir =
                    ProjectFileHelper.FindProjectDirectory(entry.OriginalPath);

                if (!string.IsNullOrWhiteSpace(projectDir))
                    CsProjUpdater.AddCompileEntry(projectDir, entry.OriginalPath);
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
        }

        if (errors.Count > 0)
            return (false, string.Join(Environment.NewLine, errors));

        DeletedClassRegistry.Remove(workspaceRoot, className);

        return (true, $"Class '{className}' restored successfully.");
    }
}