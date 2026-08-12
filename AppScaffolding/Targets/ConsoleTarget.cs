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
        string projectName, IReadOnlyList<string> entityNames, AppCustomization customization)
    {
        // The namespace the emitted code declares; projectName still names the app in its title.
        var ns = customization.RootNamespaceFor(projectName);

        return new List<GeneratedFile>
        {
            new("", "EntityMenu.cs", ConsoleMenuTemplate.Source(ns)),
            new("", "Program.cs", ConsoleProgramTemplate.Build(
                projectName, entityNames, customization.TitleLiteralFor(projectName),
                customization.SampleSeedData, ns)),
        };
    }
    // Default FixupProject no-op: our Program.cs overwrites the template's Program.cs.
}
