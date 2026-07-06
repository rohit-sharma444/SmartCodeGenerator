using CodeGenerationEngine.Helper;
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

        // A selected project only wins if it actually lives inside the
        // specified workspace. Otherwise a stale selection persisted from a
        // previous session would silently redirect saves out of the workspace.
        if (directory == null &&
            ProjectManager.CurrentProject?.Success == true &&
            IsWithinWorkspace(ProjectManager.CurrentProject.ProjectPath, workspaceRoot))
        {
            return FromProjectPath(ProjectManager.CurrentProject.ProjectPath);
        }

        if (directory == null &&
            currentProject?.Success == true &&
            IsWithinWorkspace(currentProject.ProjectPath, workspaceRoot))
        {
            return FromProjectPath(currentProject.ProjectPath);
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
            // Stay inside the specified workspace: look for a project file
            // *within* it (downward), never walk up into ancestor directories
            // which would save the class outside the workspace entirely.
            csproj = FindProjectFileWithinWorkspace(workspaceRoot);
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

    private static bool IsWithinWorkspace(string? candidatePath, string workspaceRoot)
        => PathHelper.IsWithin(candidatePath, workspaceRoot);

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

    /// <summary>
    /// Returns a project file sitting directly in the workspace root, if any.
    /// <para>
    /// Only the root is considered on purpose: when the workspace is itself a
    /// single project directory the class is saved next to that project, but a
    /// multi-project workspace does NOT get an arbitrary sub-project picked for
    /// it. In that case resolution falls back to a predictable
    /// <c>&lt;workspace&gt;\Generated</c> folder, so a class the user did not
    /// route to a specific project is always easy to find.
    /// </para>
    /// </summary>
    private static string? FindProjectFileWithinWorkspace(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) ||
            !Directory.Exists(workspaceRoot))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(workspaceRoot, "*.csproj", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
    }
}