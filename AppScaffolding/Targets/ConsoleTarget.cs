using CodeGenerationEngine.Models;

namespace AppScaffolding.Targets;

/// <summary>A runnable console CRUD app over the discovered model.</summary>
public sealed class ConsoleTarget : ITargetScaffolder
{
    public string Key => "console";
    public string Description => "a runnable Console CRUD app";
    public DotNetProjectType ProjectType => DotNetProjectType.Console;
    public string FolderSuffix => ".Console";

    public IReadOnlyList<GeneratedFile> FrontEndFiles(string projectName, IReadOnlyList<string> entityNames)
        => FrontEndFiles(projectName, entityNames, AppCustomization.Default);

    public IReadOnlyList<GeneratedFile> FrontEndFiles(
        string projectName, IReadOnlyList<string> entityNames, AppCustomization customization) => new List<GeneratedFile>
    {
        new("", "EntityMenu.cs", ConsoleMenuTemplate.Source(projectName)),
        new("", "Program.cs", ConsoleProgramTemplate.Build(
            projectName, entityNames, customization.TitleLiteralFor(projectName))),
    };
    // Default FixupProject no-op: our Program.cs overwrites the template's Program.cs.
}
