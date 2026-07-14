using CodeGenerationEngine.Core;
using CodeGenerationEngine.Factory;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace AppScaffolding;

/// <summary>Generates a WPF project and one C# class file per domain entity.</summary>
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
    /// Creates a WPF project named <paramref name="appName"/> under
    /// <paramref name="workspaceRoot"/> (reusing it if it already exists) and writes a
    /// foldered, runnable CRUD app over the entities in <paramref name="model"/>.
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
                ProjectType = DotNetProjectType.Wpf,
                Framework = "net10.0"
            });

            if (!created.Success)
                return new ScaffoldResult(
                    false, outputFolder, Array.Empty<string>(),
                    $"Project creation failed: {created.Message}");

            // Only ever touches files this scaffolder just created: the reuse path
            // above skips it, so a project the user has edited is left alone.
            WpfProjectFixup.Apply(outputFolder);
        }

        var modelsFolder = Path.Combine(outputFolder, "Models");
        var dataFolder = Path.Combine(outputFolder, "Data");
        var viewModelsFolder = Path.Combine(outputFolder, "ViewModels");
        var viewsFolder = Path.Combine(outputFolder, "Views");

        var written = new List<string>(
            WriteClasses(modelsFolder, classes, $"{appName}.Models"));
        var expected = classes.Count;

        var entityNames = classes.Select(c => c.Name).ToList();

        var files = new (string Folder, string Name, string Source)[]
        {
            (dataFolder, "JsonStore.cs", JsonStoreTemplate.Source($"{appName}.Data")),
            (dataFolder, "RelationshipResolver.cs", RelationshipResolverTemplate.Source(appName)),
            (viewModelsFolder, "ObservableObject.cs", ViewModelTemplates.ObservableObject(appName)),
            (viewModelsFolder, "RelayCommand.cs", ViewModelTemplates.RelayCommand(appName)),
            (viewModelsFolder, "IEntityViewModel.cs", ViewModelTemplates.EntityViewModelInterface(appName)),
            (viewModelsFolder, "EntityViewModel.cs", ViewModelTemplates.EntityViewModel(appName)),
            (viewModelsFolder, "MainViewModel.cs", MainViewModelTemplate.Build(appName, entityNames)),
            (viewsFolder, "MainWindow.xaml", MainWindowTemplate.Xaml(appName)),
            (viewsFolder, "MainWindow.xaml.cs", MainWindowTemplate.CodeBehind(appName)),
        };

        foreach (var (folder, name, source) in files)
        {
            expected++;
            var path = WriteSource(folder, name, source);
            if (path is not null)
                written.Add(path);
        }

        var allWritten = written.Count == expected;

        var message = allWritten
            ? $"Generated {classes.Count} class(es) + a runnable WPF CRUD app."
            : $"Generated {written.Count} of {expected} file(s); " +
              "some files could not be written.";

        return new ScaffoldResult(allWritten, outputFolder, written, message);
    }
}
