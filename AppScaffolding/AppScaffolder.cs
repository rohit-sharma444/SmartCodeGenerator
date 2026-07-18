using CodeGenerationEngine.Core;
using CodeGenerationEngine.Factory;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace AppScaffolding;

/// <summary>Generates the shared project core (Models + JsonStore + Repositories) for a domain
/// model and delegates the project type / front-end to an <see cref="ITargetScaffolder"/>.</summary>
public sealed class AppScaffolder
{
    /// <summary>
    /// Writes one <c>&lt;Name&gt;.cs</c> per class into <paramref name="outputFolder"/>,
    /// declaring <paramref name="namespaceName"/>. Best-effort: a file that cannot be
    /// written is skipped and the rest continue. Returns the paths successfully written.
    /// </summary>
    public IReadOnlyList<string> WriteClasses(
        string outputFolder, IReadOnlyList<ClassDefinition> classes, string namespaceName)
    {
        Directory.CreateDirectory(outputFolder);

        var written = new List<string>();
        foreach (var definition in classes)
        {
            var source = RoslynClassGenerator.Generate(definition, namespaceName);
            var path = WriteSource(outputFolder, definition.Name + ".cs", source);
            if (path is not null)
                written.Add(path);
        }

        return written;
    }

    /// <summary>
    /// Best-effort write of one source file into <paramref name="outputFolder"/>, creating
    /// the folder if needed. Returns the path on success, or null if it could not be written.
    /// </summary>
    private static string? WriteSource(string outputFolder, string fileName, string source)
    {
        try
        {
            Directory.CreateDirectory(outputFolder);
            var path = Path.Combine(outputFolder, fileName);
            File.WriteAllText(path, source);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a project named <c>&lt;appName&gt;&lt;target.FolderSuffix&gt;</c> under
    /// <paramref name="workspaceRoot"/> (reusing it if present) and writes the shared model +
    /// data core plus the front-end supplied by <paramref name="target"/>.
    /// </summary>
    public ScaffoldResult Scaffold(
        CandidateDomainModel model, string appName, string workspaceRoot, ITargetScaffolder target)
    {
        var classes = DomainModelMapper.ToClassDefinitions(model);

        var projectName = appName + target.FolderSuffix;
        var outputFolder = Path.Combine(workspaceRoot, projectName);
        var csprojPath = Path.Combine(outputFolder, projectName + ".csproj");

        if (!File.Exists(csprojPath))
        {
            var created = DotNetProjectFactory.CreateProject(new ProjectRequest
            {
                ProjectName = projectName,
                OutputFolder = outputFolder,
                ProjectType = target.ProjectType,
                Framework = "net10.0"
            });

            if (!created.Success)
                return new ScaffoldResult(
                    false, outputFolder, Array.Empty<string>(),
                    $"Project creation failed: {created.Message}");

            // Only ever touches files just created: the reuse path above skips it, so a
            // project the user has edited is left alone.
            target.FixupProject(outputFolder);
        }

        var modelsFolder = Path.Combine(outputFolder, "Models");
        var dataFolder = Path.Combine(outputFolder, "Data");

        var written = new List<string>(
            WriteClasses(modelsFolder, classes, $"{projectName}.Models"));
        var expected = classes.Count;

        var entityNames = classes.Select(c => c.Name).ToList();

        // Shared, target-agnostic core: JSON store + generic base repository + one typed
        // repository per entity.
        var files = new List<(string Folder, string Name, string Source)>
        {
            (dataFolder, "JsonStore.cs", JsonStoreTemplate.Source($"{projectName}.Data")),
            (dataFolder, "Repository.cs", RepositoryTemplate.Base(projectName)),
        };
        foreach (var name in entityNames)
            files.Add((dataFolder, $"{name}Repository.cs", RepositoryTemplate.Typed(projectName, name)));

        // Target-specific front-end / entry point.
        foreach (var file in target.FrontEndFiles(projectName, entityNames))
            files.Add((Path.Combine(outputFolder, file.RelativeFolder), file.Name, file.Source));

        foreach (var (folder, name, source) in files)
        {
            expected++;
            var path = WriteSource(folder, name, source);
            if (path is not null)
                written.Add(path);
        }

        var allWritten = written.Count == expected;

        var message = allWritten
            ? $"Generated {classes.Count} class(es) + {target.Description}."
            : $"Generated {written.Count} of {expected} file(s); " +
              "some files could not be written.";

        return new ScaffoldResult(allWritten, outputFolder, written, message);
    }

    /// <summary>Backward-compatible overload: scaffolds the WPF target (the historical default).</summary>
    public ScaffoldResult Scaffold(
        CandidateDomainModel model, string appName, string workspaceRoot)
        => Scaffold(model, appName, workspaceRoot, new Targets.WpfTarget());
}
