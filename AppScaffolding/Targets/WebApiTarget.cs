using CodeGenerationEngine.Models;

namespace AppScaffolding.Targets;

/// <summary>An ASP.NET Core minimal API exposing CRUD endpoints over the discovered model.</summary>
public sealed class WebApiTarget : ITargetScaffolder
{
    public string Key => "webapi";
    public string Description => "a Web API with CRUD endpoints";
    public DotNetProjectType ProjectType => DotNetProjectType.WebApi;
    public string FolderSuffix => ".Api";

    public IReadOnlyList<GeneratedFile> FrontEndFiles(string projectName, IReadOnlyList<string> entityNames)
        => FrontEndFiles(projectName, entityNames, AppCustomization.Default);

    public IReadOnlyList<GeneratedFile> FrontEndFiles(
        string projectName, IReadOnlyList<string> entityNames, AppCustomization customization) => new List<GeneratedFile>
    {
        new("", "Program.cs", WebApiProgramTemplate.Build(
            projectName, entityNames, customization.TitleLiteralFor(projectName),
            customization.SampleSeedData)),
    };
    // Default FixupProject no-op: our Program.cs overwrites the template's; net10 webapi
    // emits no WeatherForecast.cs to remove.
}
