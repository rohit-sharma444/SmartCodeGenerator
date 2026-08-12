using CodeGenerationEngine.Core;
using CodeGenerationEngine.Factory;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace AppScaffolding;

/// <summary>Generates the shared project core (Models + JsonStore + Repositories) for a domain
/// model and delegates the project type / front-end to an <see cref="ITargetScaffolder"/>.</summary>
public sealed class AppScaffolder
{
    private const string EfCorePackageId = "Microsoft.EntityFrameworkCore.Sqlite";
    private const string EfCorePackageVersion = "10.0.10";

    /// <summary>
    /// Writes one <c>&lt;Name&gt;.cs</c> per class into <paramref name="outputFolder"/>,
    /// declaring <paramref name="namespaceName"/>. Best-effort: a file that cannot be
    /// written is skipped and the rest continue. Returns the paths successfully written.
    /// </summary>
    public IReadOnlyList<string> WriteClasses(
        string outputFolder, IReadOnlyList<ClassDefinition> classes, string namespaceName,
        string header = "")
    {
        Directory.CreateDirectory(outputFolder);

        var written = new List<string>();
        foreach (var definition in classes)
        {
            var source = RoslynClassGenerator.Generate(definition, namespaceName);
            var path = WriteSource(outputFolder, definition.Name + ".cs", source, header);
            if (path is not null)
                written.Add(path);
        }

        return written;
    }

    /// <summary>
    /// Best-effort write of one source file into <paramref name="outputFolder"/>, creating
    /// the folder if needed. Returns the path on success, or null if it could not be written.
    /// </summary>
    /// <remarks>
    /// <paramref name="header"/> is prepended only for a <c>.cs</c> file: this same method also
    /// writes <c>MainWindow.xaml</c> and <c>Data/Seed/*.json</c>, and a <c>//</c> banner would
    /// corrupt both.
    /// </remarks>
    private static string? WriteSource(
        string outputFolder, string fileName, string source, string header = "")
    {
        try
        {
            Directory.CreateDirectory(outputFolder);
            var path = Path.Combine(outputFolder, fileName);

            var content = header.Length > 0
                          && fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                ? header + source
                : source;

            File.WriteAllText(path, content);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Best-effort insertion of the EF Core SQLite package into the app project file.
    /// Idempotent and silent on failure — a missing package surfaces later as a build error.</summary>
    private static void InjectEfCorePackage(string csprojPath)
    {
        try
        {
            if (!File.Exists(csprojPath))
                return;

            var updated = CsprojEditing.InjectPackageReference(
                File.ReadAllText(csprojPath), EfCorePackageId, EfCorePackageVersion);
            File.WriteAllText(csprojPath, updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Best-effort branding of the app project file. Silent on failure: a project that
    /// could not be branded still builds and runs.</summary>
    private static void InjectBranding(string csprojPath, string company, int year)
    {
        try
        {
            if (!File.Exists(csprojPath))
                return;

            File.WriteAllText(
                csprojPath,
                CsprojEditing.InjectProperties(File.ReadAllText(csprojPath), company, year));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Best-effort registration of the seed folder as copied-to-output content. Silent on
    /// failure: the app still builds, it just starts with sample rows instead of the imported ones.</summary>
    private static void InjectSeedContent(string csprojPath)
    {
        try
        {
            if (!File.Exists(csprojPath))
                return;

            File.WriteAllText(
                csprojPath, CsprojEditing.InjectSeedContent(File.ReadAllText(csprojPath)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Environment-variable name the generated AppDbContext reads for a connection-string
    /// override: the upper-cased project name with non-alphanumerics as underscores, plus "_DB".</summary>
    private static string EnvVarName(string projectName)
    {
        var chars = projectName.ToUpperInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_');
        return new string(chars.ToArray()) + "_DB";
    }

    /// <summary>
    /// Creates a project named <c>&lt;appName&gt;&lt;target.FolderSuffix&gt;</c> under
    /// <paramref name="workspaceRoot"/> (reusing it if present) and writes the shared model +
    /// data core plus the front-end supplied by <paramref name="target"/>.
    /// </summary>
    public ScaffoldResult Scaffold(
        CandidateDomainModel model, string appName, string workspaceRoot, ITargetScaffolder target)
        => Scaffold(model, appName, workspaceRoot, target, PersistenceKind.Json);

    /// <summary>As <see cref="Scaffold(CandidateDomainModel,string,string,ITargetScaffolder)"/>, but
    /// persisting through the chosen <paramref name="persistence"/> backend. SQLite emits an EF Core
    /// Data core (EfStore + AppDbContext) in place of JsonStore and adds the EF Core package.</summary>
    public ScaffoldResult Scaffold(
        CandidateDomainModel model, string appName, string workspaceRoot, ITargetScaffolder target,
        PersistenceKind persistence)
        => Scaffold(model, appName, workspaceRoot, target, persistence, importedData: null);

    /// <summary>
    /// As the five-argument overload, additionally seeding the generated app from
    /// <paramref name="importedData"/> — the rows the user's own data source contributed.
    /// </summary>
    public ScaffoldResult Scaffold(
        CandidateDomainModel model, string appName, string workspaceRoot, ITargetScaffolder target,
        PersistenceKind persistence, ImportedData? importedData)
        => Scaffold(new ScaffoldRequest(
            model, appName, workspaceRoot, target, persistence, importedData,
            AppCustomization.Default, DateTime.Now.Year));

    /// <summary>
    /// Creates a project named <c>&lt;AppName&gt;&lt;Target.FolderSuffix&gt;</c> under
    /// <see cref="ScaffoldRequest.WorkspaceRoot"/> (reusing it if present) and writes the shared
    /// model + data core plus the front end supplied by the target, shaped by
    /// <see cref="ScaffoldRequest.Customization"/>.
    /// </summary>
    public ScaffoldResult Scaffold(ScaffoldRequest request)
    {
        var model = request.Model;
        var appName = request.AppName;
        var workspaceRoot = request.WorkspaceRoot;
        var target = request.Target;
        var persistence = request.Persistence;
        var importedData = request.ImportedData;

        // Prepended to generated C# only. XAML and the Data/Seed/*.json files go through the
        // same WriteSource, and a `//` banner would corrupt both.
        var header = FileHeader.For(request.Customization.Company, request.Year);

        var classes = DomainModelMapper.ToClassDefinitions(model, out var classNames);

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

        if (persistence == PersistenceKind.Sqlite)
            InjectEfCorePackage(csprojPath);

        if (request.Customization.HasBranding)
            InjectBranding(csprojPath, request.Customization.Company, request.Year);

        var modelsFolder = Path.Combine(outputFolder, "Models");
        var dataFolder = Path.Combine(outputFolder, "Data");
        var validationFolder = Path.Combine(outputFolder, "Validation");

        var written = new List<string>(
            WriteClasses(modelsFolder, classes, $"{projectName}.Models", header));
        var expected = classes.Count;

        var entityNames = classes.Select(c => c.Name).ToList();

        // Projected through `classes`, so any review-loop edit is already accounted for.
        var seedFiles = importedData is null
            ? Array.Empty<(string FileName, string Json)>()
            : SeedFileWriter.Files(classes, classNames, importedData).ToArray();

        var seededClassNames = seedFiles
            .Select(f => Path.GetFileNameWithoutExtension(f.FileName))
            .ToList();

        // Shared, target-agnostic core: includes the IStore seam and StoreFactory, so every
        // target and persistence backend shares the same typed CRUD surface.
        var files = new List<(string Folder, string Name, string Source)>
        {
            (dataFolder, "IStore.cs", StoreSeamTemplate.Source(projectName)),
            (dataFolder, "StoreFactory.cs", StoreFactoryTemplate.Source(projectName, persistence)),
            (dataFolder, "Repository.cs", RepositoryTemplate.Base(projectName)),
            (dataFolder, "SeedData.cs",
                SeedDataTemplate.Source(
                    projectName,
                    SeedDataTemplate.OrderByDependency(classes),
                    SeedDataTemplate.ForeignKeyFieldNames(classes),
                    seededClassNames)),
            (dataFolder, "SaveResult.cs", SaveResultTemplate.Source(projectName)),
            (validationFolder, "IValidator.cs", ValidatorTemplate.ValidatorInterface(projectName)),
            (validationFolder, "IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface(projectName)),
            (validationFolder, "StoreEntityLookup.cs", ValidatorTemplate.StoreEntityLookup(projectName)),
            (validationFolder, "ValidatorFactory.cs", ValidatorTemplate.Factory(projectName, entityNames)),
        };

        // Persistence backend behind the seam.
        if (persistence == PersistenceKind.Sqlite)
        {
            files.Add((dataFolder, "EfStore.cs", EfStoreTemplate.Source(projectName)));
            files.Add((dataFolder, "AppDbContext.cs",
                DbContextTemplate.Source(projectName, entityNames, EnvVarName(projectName))));
        }
        else
        {
            files.Add((dataFolder, "JsonStore.cs", JsonStoreTemplate.Source($"{projectName}.Data")));
        }

        foreach (var name in entityNames)
            files.Add((dataFolder, $"{name}Repository.cs", RepositoryTemplate.Typed(projectName, name)));

        // The regenerated half of each model's validator. Foreign-key targets come from
        // ForeignKeys.TargetsOf, so a lookup check is only ever emitted for a <Target>Id the
        // class actually declares.
        var classNameSet = ForeignKeys.ClassNameSet(classes);
        foreach (var definition in classes)
            files.Add((validationFolder, $"{definition.Name}Validator.Generated.cs",
                ValidatorTemplate.Generated(
                    projectName, definition, ForeignKeys.TargetsOf(definition, classNameSet))));

        // Target-specific front-end / entry point.
        foreach (var file in target.FrontEndFiles(projectName, entityNames, request.Customization))
            files.Add((Path.Combine(outputFolder, file.RelativeFolder), file.Name, file.Source));

        foreach (var (folder, name, source) in files)
        {
            expected++;
            var path = WriteSource(folder, name, source, header);
            if (path is not null)
                written.Add(path);
        }

        // After the files are written, not in FixupProject: Views/Theme.xaml does not exist yet
        // when the project is created.
        if (target.ProjectType == DotNetProjectType.Wpf)
            WpfProjectFixup.MergeTheme(outputFolder);

        // Data files, not source: written with the same best-effort WriteSource so a locked file
        // is reported through `expected` rather than throwing mid-scaffold.
        if (seedFiles.Length > 0)
        {
            var seedFolder = Path.Combine(dataFolder, SeedFileWriter.SeedFolderName);

            foreach (var (fileName, json) in seedFiles)
            {
                expected++;
                var path = WriteSource(seedFolder, fileName, json);
                if (path is not null)
                    written.Add(path);
            }

            InjectSeedContent(csprojPath);
        }

        // Written once, never overwritten: this is where the user's own rules live, and a rebuild
        // must not discard them. The .Generated.cs half above is refreshed every run.
        foreach (var definition in classes)
        {
            var fileName = $"{definition.Name}Validator.Custom.cs";
            if (File.Exists(Path.Combine(validationFolder, fileName)))
                continue;

            expected++;
            var customPath = WriteSource(
                validationFolder, fileName,
                ValidatorTemplate.CustomStub(projectName, definition.Name), header);
            if (customPath is not null)
                written.Add(customPath);
        }

        var testProjectPath = ScaffoldTestProject(workspaceRoot, projectName, classes);

        var allWritten = written.Count == expected;

        var message = allWritten
            ? $"Generated {classes.Count} class(es) + {target.Description}."
            : $"Generated {written.Count} of {expected} file(s); " +
              "some files could not be written.";

        return new ScaffoldResult(allWritten, outputFolder, written, message, testProjectPath);
    }

    /// <summary>
    /// Creates a sibling <c>&lt;appProject&gt;.Tests</c> xUnit project referencing the app project,
    /// with generated CRUD + seed tests. Best-effort: returns null (leaving the app itself intact)
    /// if the test project could not be created. The test project's TFM mirrors the app's, so a
    /// WPF app's tests target net10.0-windows and resolve the reference.
    /// </summary>
    private static string? ScaffoldTestProject(
        string workspaceRoot, string appProjectName, IReadOnlyList<ClassDefinition> classes)
    {
        var entityNames = classes.Select(c => c.Name).ToList();

        if (entityNames.Count == 0)
            return null;

        var testProjectName = appProjectName + ".Tests";
        var testFolder = Path.Combine(workspaceRoot, testProjectName);
        var testCsproj = Path.Combine(testFolder, testProjectName + ".csproj");
        var appCsproj = Path.Combine("..", appProjectName, appProjectName + ".csproj");

        var framework = AppProjectFramework(workspaceRoot, appProjectName);

        if (!File.Exists(testCsproj))
        {
            var created = DotNetProjectFactory.CreateProject(new ProjectRequest
            {
                ProjectName = testProjectName,
                OutputFolder = testFolder,
                ProjectType = DotNetProjectType.XUnitTest,
                Framework = framework
            });

            if (!created.Success)
                return null;

            // dotnet new xunit emits a UnitTest1.cs placeholder; replace it with our tests.
            var placeholder = Path.Combine(testFolder, "UnitTest1.cs");
            try { if (File.Exists(placeholder)) File.Delete(placeholder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

            try
            {
                File.WriteAllText(testCsproj,
                    TestProjectTemplates.InjectProjectReference(File.ReadAllText(testCsproj), appCsproj));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        var roundTripPath = WriteSource(testFolder, "RepositoryRoundTripTests.cs",
            TestProjectTemplates.RoundTripTests(appProjectName, entityNames));
        var seedPath = WriteSource(testFolder, "SeedDataTests.cs",
            TestProjectTemplates.SeedTests(appProjectName, entityNames[0]));
        var parallelizationPath = WriteSource(testFolder, "TestParallelization.cs",
            TestProjectTemplates.DisableParallelization());
        var supportPath = WriteSource(testFolder, "TestSupport.cs",
            TestProjectTemplates.TestSupport(appProjectName));

        if (roundTripPath is null || seedPath is null || parallelizationPath is null
            || supportPath is null)
            return null;

        // One validator test file per model, so the generated suite covers the generated rules.
        foreach (var definition in classes)
        {
            var required = definition.Properties.FirstOrDefault(p =>
                p.Type == "string"
                && p.Rules.Any(r => string.Equals(r.Name, "required", StringComparison.OrdinalIgnoreCase)));

            if (WriteSource(testFolder, $"{definition.Name}ValidatorTests.cs",
                    TestProjectTemplates.ValidatorTests(appProjectName, definition.Name, required?.Name)) is null)
                return null;
        }

        return testCsproj;
    }

    /// <summary>The app project's &lt;TargetFramework&gt;, so the test project matches it
    /// (net10.0-windows for WPF). Falls back to net10.0 if it cannot be read.</summary>
    private static string AppProjectFramework(string workspaceRoot, string appProjectName)
    {
        try
        {
            var csproj = File.ReadAllText(
                Path.Combine(workspaceRoot, appProjectName, appProjectName + ".csproj"));
            var open = csproj.IndexOf("<TargetFramework>", StringComparison.Ordinal);
            var close = csproj.IndexOf("</TargetFramework>", StringComparison.Ordinal);
            if (open >= 0 && close > open)
            {
                open += "<TargetFramework>".Length;
                return csproj.Substring(open, close - open).Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return "net10.0";
    }

    /// <summary>Backward-compatible overload: scaffolds the WPF target (the historical default).</summary>
    public ScaffoldResult Scaffold(
        CandidateDomainModel model, string appName, string workspaceRoot)
        => Scaffold(model, appName, workspaceRoot, new Targets.WpfTarget());
}
