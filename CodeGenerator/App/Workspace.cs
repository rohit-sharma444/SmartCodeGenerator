using System.Text.Json;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.App;

/// <summary>What a re-root did, for the caller to report.</summary>
public sealed record WorkspaceLoad(
    bool Ok, string Root, int ProjectCount, string? SolutionPath, string? Problem);

/// <summary>
/// Where the tool is working: the root, the rules loaded from it, and the projects under it.
/// </summary>
/// <remarks>
/// This was four loose fields on <c>CodeGeneratorStarter</c> with the loading split between its
/// constructor and <c>ProjectManager</c>. It is one object now because the chat page can re-root at
/// runtime, and the REPL has to read whatever the chat left behind.
/// <para>
/// <see cref="HistoryPath"/> follows <see cref="Root"/> rather than being fixed at startup, which
/// needs no further plumbing: <c>Run</c> builds a fresh <c>CommandLineEditor</c> on every
/// iteration.
/// </para>
/// <para>
/// The projects live in <c>ProjectManager</c>, which is static. That is why re-rooting here moves
/// the console session too, and why the chat says so when it happens rather than pretending each
/// surface has its own.
/// </para>
/// </remarks>
public sealed class Workspace
{
    private const string HistoryFilename = ".codegen_history";

    public Workspace(string root, bool projectPathSet)
    {
        Root = root;
        ProjectPathSet = projectPathSet;
    }

    public string Root { get; private set; }

    /// <summary>Whether <c>PROJECTPATH</c> chose the root. Only used to word a notice.</summary>
    public bool ProjectPathSet { get; }

    /// <summary>
    /// The solution this workspace was loaded from, or null when it was loaded from a folder.
    /// </summary>
    /// <remarks>
    /// Kept rather than only returned, because what was loaded is a fact about the workspace and
    /// anything asking later has no other honest way to find out. The chat page's tree names its
    /// root node from this: it went looking for a solution file under the root once, and on a
    /// repository holding four of them it named whichever sorted first.
    /// </remarks>
    public string? SolutionPath { get; private set; }

    public List<PropertyRule> Rules { get; private set; } = [];

    public int ProjectCount { get; internal set; }

    public string HistoryPath => Path.Combine(Root, HistoryFilename);

    /// <summary>
    /// Re-roots at a solution file, a project file or a folder.
    /// </summary>
    /// <remarks>
    /// Does what startup does, in startup's order: register, drop what is outside, clear a
    /// selection that no longer applies, reload the rules, reinitialize the learned-intent store.
    /// Getting that order wrong leaves a registry holding two workspaces at once.
    /// </remarks>
    public WorkspaceLoad LoadFrom(string path)
    {
        string root;
        string? solution = null;
        IReadOnlyList<string> projects;

        try
        {
            var full = FullPathOf(path);

            if (SolutionFile.IsSolution(full))
            {
                if (!File.Exists(full))
                    return Failed($"There is no solution file at {full}.");

                var read = SolutionFile.Read(full);

                if (read.ProjectFiles.Count == 0)
                    return Failed($"{Path.GetFileName(full)} declares no C# projects.");

                solution = full;
                root = read.Root;
                projects = read.ProjectFiles;
            }
            else if (File.Exists(full)
                     && Path.GetExtension(full).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                root = Path.GetDirectoryName(full) ?? full;
                projects = [full];
            }
            else if (Directory.Exists(full))
            {
                root = full;
                projects = Directory.GetFiles(full, "*.csproj", SearchOption.AllDirectories);
            }
            else
            {
                return Failed($"There is nothing at {full}.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException)
        {
            return Failed(ex.Message);
        }

        Root = Path.TrimEndingDirectorySeparator(root);

        // Assigned on every load, including the one that clears it: a folder loaded after a
        // solution is a folder, and leaving the old path here would have the tree still naming a
        // solution nobody has open.
        SolutionPath = solution;

        foreach (var project in projects)
        {
            ProjectManager.Register(new ProjectMetaData
            {
                Success = true,
                ProjectPath = Path.GetDirectoryName(project) ?? Root,
                CsProjPath = project,
                ProjectName = Path.GetFileNameWithoutExtension(project),
                Message = "Loaded"
            });
        }

        // Order matters: drop the outsiders before clearing the selection, or a selection inside
        // the old root is still in the registry when the check runs.
        ProjectManager.RetainOnlyWithin(Root);
        ProjectManager.ClearCurrentProjectIfOutside(Root);

        LoadRules();
        LearnedIntentStore.Initialize(Root);

        ProjectCount = projects.Count;

        return new WorkspaceLoad(true, Root, ProjectCount, solution, null);
    }

    /// <summary>
    /// Reads rules.json, or rules.txt if there is no json, from the current root.
    /// </summary>
    /// <remarks>
    /// Lifted verbatim from the starter, including the fall-back to an empty list on a bad file:
    /// a malformed rules file must not stop the tool starting.
    /// </remarks>
    public void LoadRules()
    {
        var jsonPath = Path.Combine(Root, "rules.json");
        var textPath = Path.Combine(Root, "rules.txt");

        Rules = [];

        try
        {
            if (File.Exists(jsonPath))
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var loaded = JsonSerializer.Deserialize<List<PropertyRule>>(
                    File.ReadAllText(jsonPath), options);

                if (loaded != null)
                    Rules = loaded;

                Console.WriteLine($"Loaded {Rules.Count} property rules from rules.json");
            }
            else if (File.Exists(textPath))
            {
                var lines = File.ReadAllLines(textPath).Where(l => !string.IsNullOrWhiteSpace(l));

                Rules = [.. RuleParser.Parse(lines)];

                Console.WriteLine($"Parsed {Rules.Count} property rules from rules.txt");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load rules: {ex.Message}");
            Rules = [];
        }
    }

    /// <summary>
    /// Where a path given to <see cref="LoadFrom"/> is: unchanged when rooted, else the one under
    /// the current root, else the one under the current directory, else the one under the root.
    /// </summary>
    /// <remarks>
    /// A relative path was resolved against the process's current directory, which is wherever
    /// the console was started — the bin folder under Visual Studio, anywhere at all when
    /// PROJECTPATH chose the root — so "Api" typed on the chat page missed the workspace's Api
    /// folder. The workspace is what the user is looking at, so it comes first, as it does for
    /// data files (<c>DataSourcePath</c>) and scripts. The current directory is kept as a fallback
    /// so a relative path that loaded before still loads; a path found in neither is named under
    /// the root, which is the one the user meant. <c>DataSourcePath</c> itself is not used: it
    /// looks for a file, and this may be a folder.
    /// </remarks>
    private string FullPathOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || string.IsNullOrWhiteSpace(Root))
            return Path.GetFullPath(path);

        var inRoot = Path.GetFullPath(Path.Combine(Root, path));
        if (File.Exists(inRoot) || Directory.Exists(inRoot))
            return inRoot;

        var inCurrent = Path.GetFullPath(path);
        return File.Exists(inCurrent) || Directory.Exists(inCurrent) ? inCurrent : inRoot;
    }

    private WorkspaceLoad Failed(string problem)
        => new(false, Root, ProjectCount, null, problem);
}
