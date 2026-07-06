namespace CodeGenerationEngine.Core;

/// <summary>
/// Service responsible for deleting class files from the workspace.
/// </summary>
public static class ClassDeletionService
{
    public static (bool Success, string Message) DeleteClass(
        string workspaceRoot,
        string className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return (false, "Class name not provided.");

        if (!Directory.Exists(workspaceRoot))
            return (false, "Workspace does not exist.");

        var files = Directory.GetFiles(
                workspaceRoot,
                $"{className}.cs",
                SearchOption.AllDirectories)
            .Where(f => !f.Contains(
                "DeletedClasses",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (files.Length == 0)
            return (false, $"Class '{className}' not found.");

        var errors = new List<string>();

        foreach (var file in files)
        {
            try
            {
                var originalPath = file;

                var projectDir = ProjectFileHelper.FindProjectDirectory(file);

                if (string.IsNullOrWhiteSpace(projectDir))
                {
                    errors.Add(
                        $"Unable to locate project for '{file}'.");
                    continue;
                }

                var deletedFolder =
                    Path.Combine(projectDir, "DeletedClasses");

                Directory.CreateDirectory(deletedFolder);

                var destinationFile = Path.Combine(
                    deletedFolder,
                    $"{Path.GetFileNameWithoutExtension(file)}_" +
                    $"{DateTime.UtcNow:yyyyMMddHHmmssfff}.cs");

                // Remove from project first
                CsProjUpdater.RemoveCompileEntry(
                    projectDir,
                    className);

                // Move file
                File.Move(
                    originalPath,
                    destinationFile);

                // Store ORIGINAL path
                DeletedClassRegistry.Register(
                    workspaceRoot,
                    className,
                    originalPath);
            }
            catch (Exception ex)
            {
                errors.Add(
                    $"{file}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            return (
                false,
                "Delete completed with errors:" +
                Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    errors));
        }

        return (
            true,
            $"Class '{className}' deleted successfully.");
    }
}