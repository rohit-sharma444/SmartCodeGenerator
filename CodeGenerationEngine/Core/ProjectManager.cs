using CodeGenerationEngine.Helper;
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
            Output.Error($"Failed to save registry: {ex.Message}");
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

            // Intentionally do NOT auto-select a current project here. Restoring
            // a stale selection from a previous session silently redirects saves
            // into an unexpected project. Selection must be an explicit user act.
        }
        catch (Exception ex)
        {
            Output.Error($"Failed to load registry: {ex.Message}");
        }
    }

    /// <summary>
    /// Drops the currently selected project when it lives outside the given
    /// workspace. A selection persisted from a previous session must not carry
    /// over and redirect saves out of the workspace the user is now working in.
    /// </summary>
    /// <summary>
    /// Drops registered projects that live outside the given workspace. The
    /// registry file (ProjectManager.dat) is global, so without this a project
    /// created in one workspace leaks into every other workspace's 'list
    /// projects' / 'use project' and can redirect file operations to a stale
    /// path.
    /// </summary>
    public static void RetainOnlyWithin(string workspaceRoot)
    {
        _projects.RemoveAll(p =>
            !PathHelper.IsWithin(p.ProjectPath, workspaceRoot) &&
            !PathHelper.IsWithin(p.CsProjPath, workspaceRoot));

        ClearCurrentProjectIfOutside(workspaceRoot);
    }

    public static void ClearCurrentProjectIfOutside(string workspaceRoot)
    {
        if (CurrentProject == null)
            return;

        if (!PathHelper.IsWithin(CurrentProject.ProjectPath, workspaceRoot) &&
            !PathHelper.IsWithin(CurrentProject.CsProjPath, workspaceRoot))
        {
            CurrentProject = null;
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

    public static bool UseProject(string projectRef)
    {
        var value = (projectRef ?? string.Empty).Trim();
        bool success;

        if (int.TryParse(value, out var index))
            success = ProjectManager.Select(index);
        else
            success = ProjectManager.Select(value);

        if (!success)
        {
            Output.Warning("Project not found.");
            return false;
        }

        Output.Success($"Current project: " +
            $"{Path.GetFileNameWithoutExtension(ProjectManager.CurrentProject!.ProjectPath)}");

        return true;
    }

    public static bool ShowCurrentProject()
    {
        if (ProjectManager.CurrentProject == null)
        {
            Output.Warning("No project selected.");
            return false;
        }

        Console.WriteLine(Path.GetFileNameWithoutExtension(ProjectManager.CurrentProject.ProjectPath));
        return true;
    }

    public static bool ListClasses(string? workspaceRoot = null)
    {
        var project = ProjectManager.CurrentProject;

        // When a project is selected, list its classes. Otherwise fall back to
        // the whole workspace so "list classes" is still useful without first
        // running "use project X".
        string folder;
        string scopeLabel;

        if (project != null)
        {
            folder = ProjectFolderOf(project);
            scopeLabel = Path.GetFileNameWithoutExtension(project.ProjectPath);
        }
        else if (!string.IsNullOrWhiteSpace(workspaceRoot) && Directory.Exists(workspaceRoot))
        {
            folder = workspaceRoot;
            scopeLabel = "workspace";
        }
        else
        {
            Output.Warning("No project selected. Use 'use project <name>' first.");
            return false;
        }

        var classNames = ClassNamesIn(folder);

        if (classNames.Count == 0)
        {
            Console.WriteLine("No classes found.");
            return false;
        }

        Console.WriteLine();
        Console.WriteLine($"Classes in {scopeLabel}");

        Console.WriteLine("--------------------------------");

        foreach (var name in classNames)
        {
            Console.WriteLine(name);
        }

        Console.WriteLine();
        return true;
    }

    // Distinct, alphabetically ordered class (file) names under a folder,
    // excluding build output and deleted-class backups. Same-named files across
    // projects collapse to a single entry so the listing has no duplicates.
    //
    // Public because the chat page's project tree draws the same list this
    // command prints. Two enumerations of "the classes in a project" that could
    // disagree with each other would be worse than one that is merely awkward
    // to reach.
    public static IReadOnlyList<string> ClassNamesIn(string folder)
        => Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(x =>
                !x.Contains("\\bin\\") && !x.Contains("\\obj\\") &&
                !x.Contains("DeletedClasses", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    // ProjectPath is normally the project directory, but persisted/registered
    // entries occasionally hold the .csproj file path - handle both.
    public static string ProjectFolderOf(ProjectMetaData project)
        => File.Exists(project.ProjectPath)
            ? Path.GetDirectoryName(project.ProjectPath)!
            : project.ProjectPath;

    // The classes of one project, as data. Nothing is printed: this is what
    // ListClassesForProject prints from, and what the chat page's tree draws,
    // so the two cannot fall out of step. An unreadable or missing project
    // folder yields nothing rather than throwing - a registry entry can outlive
    // the folder it names, and a tree is not the place to discover that.
    public static IReadOnlyList<string> ClassesOf(ProjectMetaData project)
    {
        var folder = ProjectFolderOf(project);

        return string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)
            ? []
            : ClassNamesIn(folder);
    }

    public static bool ListClassesInProject(string projectName)
    {
        projectName = (projectName ?? string.Empty).Trim();

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
        var projectFolder = ProjectFolderOf(project);

        if (string.IsNullOrWhiteSpace(projectFolder) || !Directory.Exists(projectFolder))
            return false;

        var classNames = ClassesOf(project);

        Console.WriteLine();
        Console.WriteLine($"Classes in {Path.GetFileNameWithoutExtension(project.ProjectPath)}");

        Console.WriteLine("--------------------------------");

        foreach (var name in classNames)
        {
            Console.WriteLine(name);
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
            Output.Warning($"Warning: Failed to initialize existing projects: {ex.Message}");
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
                Output.Warning($"Warning: Invalid PROJECTPATH '{projectPath}': {ex.Message}");
            }
        }

        return Path.GetFullPath(Directory.GetCurrentDirectory());
    }

}