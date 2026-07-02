using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

public static class ProjectLocator
{
    public static ProjectLocation Resolve(ParsedCommand? parsed,
        ProjectMetaData? currentProject, string workspaceRoot)
    {
        string? csproj = null;
        string? directory = null;

        if (!string.IsNullOrWhiteSpace(parsed?.OutputFolder) &&
            Directory.Exists(parsed.OutputFolder))
        {
            directory = Path.GetFullPath(parsed.OutputFolder);
            csproj = Directory.GetFiles(directory, "*.csproj")
                              .FirstOrDefault();
        }

        if (directory == null &&
            ProjectManager.CurrentProject?.Success == true)
        {
            return FromProjectPath(ProjectManager.CurrentProject.ProjectPath);
        }

        if (directory == null &&
            currentProject?.Success == true)
        {
            return FromProjectPath(                currentProject.ProjectPath);
        }

        if (directory == null &&
            !string.IsNullOrWhiteSpace(parsed?.ProjectName))
        {
            var match = Directory
                .GetFiles(workspaceRoot,
                    $"{parsed.ProjectName}.csproj",
                    SearchOption.AllDirectories)
                .FirstOrDefault();

            if (match != null)
            {
                csproj = match;
                directory = Path.GetDirectoryName(match);
            }
        }

        if (directory == null)
        {
            csproj = FindNearestProjectFile(workspaceRoot);
            directory = csproj != null
                ? Path.GetDirectoryName(csproj)!
                : Path.Combine(workspaceRoot, "Generated");
        }

        Directory.CreateDirectory(directory);

        return new ProjectLocation
        {
            Directory = directory,
            ProjectFile = csproj
        };
    }

    private static ProjectLocation FromProjectPath(string path)
    {
        if (File.Exists(path))
        {
            return new ProjectLocation
            {
                Directory = Path.GetDirectoryName(path)!,
                ProjectFile = path
            };
        }

        return new ProjectLocation
        {
            Directory = path,
            ProjectFile = Directory.GetFiles(path, "*.csproj")
                                   .FirstOrDefault()
        };
    }

    private static string? FindNearestProjectFile(string startDir)
    {
        var dir = new DirectoryInfo(startDir);

        while (dir != null)
        {
            var file = dir.GetFiles("*.csproj")
                          .FirstOrDefault();

            if (file != null)
                return file.FullName;

            dir = dir.Parent;
        }

        return null;
    }
}