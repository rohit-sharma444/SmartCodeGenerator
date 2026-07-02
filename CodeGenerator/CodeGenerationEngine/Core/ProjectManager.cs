using CodeGenerationEngine.Models;
using System.Text.Json;

namespace CodeGenerationEngine.Core;

/// <summary>
/// 
/// </summary>
public static class ProjectManager
{
    private static readonly List<ProjectMetaData> _projects = [];

    private static string _workspaceRoot = string.Empty;

    public static ProjectMetaData? CurrentProject { get; set; }

    private static readonly string RegistryFile =
    Path.Combine(AppContext.BaseDirectory, "ProjectManager.dat");

    public static void Register(ProjectMetaData project)
    {
        if (project == null || !project.Success)
            return;

        var existing = _projects.FirstOrDefault(x =>
            string.Equals(x.ProjectPath, project.ProjectPath,
                StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.ProjectName = project.ProjectName;
            existing.ProjectPath = project.ProjectPath;
            existing.CsProjPath = project.CsProjPath;
            existing.Message = project.Message;
            existing.Success = project.Success;

            if (CurrentProject?.ProjectPath == existing.ProjectPath)
                CurrentProject = existing;
            return;
        }
        _projects.Add(project);
        CurrentProject ??= project;
    }

    public static IReadOnlyList<ProjectMetaData> Projects => _projects;

    public static bool Select(string projectName)
    {
        var project = _projects.FirstOrDefault(x =>
            string.Equals(x.ProjectName, projectName,
            StringComparison.OrdinalIgnoreCase));

        if (project == null)
            return false;

        CurrentProject = project;
        return true;
    }

    public static bool Select(int index)
    {
        if (index < 1 || index > _projects.Count)
            return false;

        CurrentProject = _projects[index - 1];
        return true;
    }

    public static void SaveProjects()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                _projects,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            File.WriteAllText(RegistryFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save registry: {ex.Message}");
        }
    }

    public static void LoadProject()
    {
        try
        {
            if (!File.Exists(RegistryFile))
                return;

            var json = File.ReadAllText(RegistryFile);

            var projects = JsonSerializer.Deserialize<List<ProjectMetaData>>(json);

            if (projects == null)
                return;

            _projects.Clear();
            _projects.AddRange(projects);

            CurrentProject = GetProjects().FirstOrDefault();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load registry: {ex.Message}");
        }
    }

    public static bool ListProjects()
    {
        if (!Projects.Any())
        {
            Console.WriteLine("No projects found.");
            return false;
        }

        Console.WriteLine("Available Projects:");
        Console.WriteLine();

        for (int i = 0; i < Projects.Count; i++)
        {
            var project = Projects[i];

            if(string.IsNullOrWhiteSpace(project.ProjectName))
                continue;

            string projectName = !string.IsNullOrWhiteSpace(project.ProjectName)
                    ? project.ProjectName : Path.GetFileName(project.ProjectPath);

            Console.WriteLine($"{projectName}");
        }
        return true;
    }

    public static IReadOnlyList<ProjectMetaData> GetProjects()
    {
        return [.. _projects
        .Where(p =>
                (!string.IsNullOrWhiteSpace(p.ProjectPath) &&
                (Directory.Exists(p.ProjectPath) ||
                 File.Exists(p.ProjectPath)))            ||
                (!string.IsNullOrWhiteSpace(p.CsProjPath) &&
                 File.Exists(p.CsProjPath)))
        .OrderBy(p => p.ProjectName)];
    }

    public static bool UseProject(string input)
    {
        var value = input["use project ".Length..].Trim();
        bool success;

        if (int.TryParse(value, out var index))
            success = ProjectManager.Select(index);
        else
            success = ProjectManager.Select(value);

        if (!success)
        {
            Console.WriteLine("Project not found.");
            return false;
        }

        Console.WriteLine($"Current project: " +
            $"{Path.GetFileNameWithoutExtension(ProjectManager.CurrentProject!.ProjectPath)}");

        return true;
    }

    public static bool ShowCurrentProject()
    {
        if (ProjectManager.CurrentProject == null)
        {
            Console.WriteLine("No project selected.");
            return false;
        }

        Console.WriteLine(Path.GetFileNameWithoutExtension(ProjectManager.CurrentProject.ProjectPath));
        return true;
    }

    public static bool ListClasses()
    {
        var project = ProjectManager.CurrentProject;

        if (project == null)
        {
            Console.WriteLine("No project selected.");
            return false;
        }

        var projectFolder = Path.GetDirectoryName(project.ProjectPath);

        if (string.IsNullOrWhiteSpace(projectFolder))
            return false;

        var classFiles = Directory.GetFiles(projectFolder, "*.cs",
                SearchOption.AllDirectories).Where(x =>
                !x.Contains("\\bin\\") && !x.Contains("\\obj\\") &&
                !x.Contains("DeletedClasses", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (classFiles.Count == 0)
        {
            Console.WriteLine("No classes found.");
            return false;
        }

        Console.WriteLine();
        Console.WriteLine($"Classes in {Path.GetFileNameWithoutExtension(project.ProjectPath)}");

        Console.WriteLine("--------------------------------");

        foreach (var file in classFiles)
        {
            Console.WriteLine(Path.GetFileNameWithoutExtension(file));
        }

        Console.WriteLine();
        return true;
    }

    public static bool ListClassesInProject(string input)
    {
        var projectName = input["list classes in ".Length..].Trim();

        var project = ProjectManager.Projects.FirstOrDefault(x =>
                      Path.GetFileNameWithoutExtension(
                      x.ProjectPath).Equals(projectName,
                      StringComparison.OrdinalIgnoreCase));

        if (project == null)
        {
            Console.WriteLine($"Project '{projectName}' not found.");
            return false;
        }

        return ListClassesForProject(project);
    }

    private static bool ListClassesForProject(ProjectMetaData project)
    {
        var projectFolder = Path.GetDirectoryName(project.ProjectPath);

        if (string.IsNullOrWhiteSpace(projectFolder))
            return false;

        var classFiles = Directory.GetFiles(projectFolder, "*.cs",
                SearchOption.AllDirectories)
            .Where(x => !x.Contains("\\bin\\") &&
                !x.Contains("\\obj\\") &&
                !x.Contains("DeletedClasses",
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        Console.WriteLine();
        Console.WriteLine($"Classes in {Path.GetFileNameWithoutExtension(project.ProjectPath)}");

        Console.WriteLine("--------------------------------");

        foreach (var file in classFiles)
        {
            Console.WriteLine(Path.GetFileNameWithoutExtension(file));
        }

        Console.WriteLine();
        return true;
    }

    public static void InitializeExistingProjects()
    {
        try
        {
            _workspaceRoot = ResolveWorkspaceRoot();

            var projects = Directory.GetFiles(_workspaceRoot, "*.csproj",
                SearchOption.AllDirectories);

            foreach (var project in projects)
            {
                var projectDir = Path.GetDirectoryName(project);
                ProjectManager.Register(new ProjectMetaData
                {
                    Success = true,
                    ProjectPath = projectDir ?? Path.GetDirectoryName(_workspaceRoot) ?? _workspaceRoot,
                    CsProjPath = project,
                    ProjectName = Path.GetFileNameWithoutExtension(project),
                    Message = "Loaded"
                });
            }

            if (ProjectManager.CurrentProject != null)
            {
                Console.WriteLine($"Current Project: " +
                    $"{Path.GetFileNameWithoutExtension(ProjectManager.CurrentProject.ProjectPath)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to initialize existing projects: {ex.Message}");
        }
    }

    private static string ResolveWorkspaceRoot()
    {
        var projectPath = Environment.GetEnvironmentVariable("PROJECTPATH");

        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            try
            {
                return Path.GetFullPath(projectPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Invalid PROJECTPATH '{projectPath}': {ex.Message}");
            }
        }

        return Path.GetFullPath(Directory.GetCurrentDirectory());
    }

}