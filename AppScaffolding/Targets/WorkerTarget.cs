using CodeGenerationEngine.Models;

namespace AppScaffolding.Targets;

/// <summary>A worker-service scaffold whose BackgroundService exercises the repositories.</summary>
public sealed class WorkerTarget : ITargetScaffolder
{
    public string Key => "worker";
    public string Description => "a background service that exercises the repositories";
    public DotNetProjectType ProjectType => DotNetProjectType.WorkerService;
    public string FolderSuffix => ".Worker";

    public IReadOnlyList<GeneratedFile> FrontEndFiles(string projectName, IReadOnlyList<string> entityNames) => new List<GeneratedFile>
    {
        new("", "Worker.cs", WorkerTemplate.Build(projectName, entityNames)),
        new("", "Program.cs", WorkerProgramTemplate.Source(projectName)),
    };
    // Default FixupProject no-op: our Worker.cs and Program.cs overwrite the template's.
}
