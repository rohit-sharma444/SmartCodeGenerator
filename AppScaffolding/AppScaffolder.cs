using CodeGenerationEngine.Core;
using CodeGenerationEngine.Factory;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace AppScaffolding;

/// <summary>Generates a .NET project and one C# class file per domain entity.</summary>
public sealed class AppScaffolder
{
    /// <summary>
    /// Writes one <c>&lt;Name&gt;.cs</c> per class into <paramref name="outputFolder"/>.
    /// Best-effort: a file that cannot be written is skipped and the rest continue.
    /// Returns the paths successfully written.
    /// </summary>
    public IReadOnlyList<string> WriteClasses(
        string outputFolder, IReadOnlyList<ClassDefinition> classes)
    {
        Directory.CreateDirectory(outputFolder);

        var written = new List<string>();
        foreach (var definition in classes)
        {
            try
            {
                var source = RoslynClassGenerator.Generate(definition);
                var path = Path.Combine(outputFolder, definition.Name + ".cs");
                File.WriteAllText(path, source);
                written.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Skip this file; remaining classes are still written.
            }
        }

        return written;
    }

    /// <summary>
    /// Creates a console project named <paramref name="appName"/> under
    /// <paramref name="workspaceRoot"/> (reusing it if it already exists) and
    /// writes one class per entity in <paramref name="model"/>.
    /// </summary>
    public ScaffoldResult Scaffold(
        CandidateDomainModel model, string appName, string workspaceRoot)
    {
        var classes = DomainModelMapper.ToClassDefinitions(model);
        var outputFolder = Path.Combine(workspaceRoot, appName);
        var csprojPath = Path.Combine(outputFolder, appName + ".csproj");

        if (!File.Exists(csprojPath))
        {
            var created = DotNetProjectFactory.CreateProject(new ProjectRequest
            {
                ProjectName = appName,
                OutputFolder = outputFolder,
                ProjectType = DotNetProjectType.Console,
                Framework = "net10.0"
            });

            if (!created.Success)
                return new ScaffoldResult(
                    false, outputFolder, Array.Empty<string>(),
                    $"Project creation failed: {created.Message}");
        }

        var written = WriteClasses(outputFolder, classes);
        bool allWritten = written.Count == classes.Count;

        var message = allWritten
            ? $"Generated {written.Count} class(es)."
            : $"Generated {written.Count} of {classes.Count} class(es); " +
              "some files could not be written.";

        return new ScaffoldResult(allWritten, outputFolder, written, message);
    }
}
