using AppScaffolding.Auth;
using AppScaffolding.Screens;
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

    /// <summary>Best-effort insertion of one package reference into the app project file.
    /// Idempotent and silent on failure — a missing package surfaces later as a build error.</summary>
    private static void InjectPackage(string csprojPath, string id, string version)
    {
        try
        {
            if (!File.Exists(csprojPath))
                return;

            var updated = CsprojEditing.InjectPackageReference(
                File.ReadAllText(csprojPath), id, version);
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

    /// <summary>
    /// Copies the chosen icon to <c>Assets/app&lt;ext&gt;</c> and registers it in the project file.
    /// False when there was no icon or it could not be copied — in which case nothing downstream
    /// may name it.
    /// </summary>
    /// <remarks>
    /// A missing or unreadable source is ignored rather than reported: it is not worth failing a
    /// build the user has already answered ten questions for. The generated app then simply keeps
    /// the default icon, which is why the caller drops the icon from the customization the front
    /// end sees rather than emitting a reference to a file that is not there.
    /// </remarks>
    private static bool CopyIcon(
        AppCustomization customization, string outputFolder, string csprojPath,
        DotNetProjectType projectType)
    {
        var fileName = customization.IconFileName;

        if (customization.IconPath is null
            || fileName is null
            || !File.Exists(customization.IconPath))
            return false;

        try
        {
            var assets = Path.Combine(outputFolder, AppCustomization.AssetsFolder);

            Directory.CreateDirectory(assets);
            File.Copy(customization.IconPath, Path.Combine(assets, fileName), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        RegisterIcon(csprojPath, $"{AppCustomization.AssetsFolder}\\{fileName}", projectType);
        return true;
    }

    /// <summary>
    /// Best-effort registration of the copied icon in the app project file. Silent on failure: the
    /// project still builds, it just shows the default icon.
    /// </summary>
    /// <remarks>
    /// A <c>.ico</c> becomes the <c>ApplicationIcon</c>, the icon the built .exe carries; MSBuild
    /// rejects any other extension there. How the file itself is registered depends on how the
    /// front end reaches it: WPF resolves a <c>Window</c>'s relative <c>Icon</c> attribute through
    /// the pack URI, so the file has to be embedded as a <c>Resource</c>, while the WinForms form
    /// reads it off disk and needs it copied beside the binary instead. The other targets have no
    /// window, so for them the exe icon is the whole story.
    /// </remarks>
    private static void RegisterIcon(
        string csprojPath, string relativePath, DotNetProjectType projectType)
    {
        try
        {
            if (!File.Exists(csprojPath))
                return;

            var csproj = File.ReadAllText(csprojPath);

            if (relativePath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                csproj = CsprojEditing.InjectApplicationIcon(csproj, relativePath);

            csproj = projectType switch
            {
                DotNetProjectType.Wpf => CsprojEditing.InjectResource(csproj, relativePath),
                DotNetProjectType.WinForms => CsprojEditing.InjectContent(csproj, relativePath),
                _ => csproj,
            };

            File.WriteAllText(csprojPath, csproj);
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

    /// <summary>Best-effort root-namespace reconciliation for a project file. Silent on failure:
    /// a project whose RootNamespace could not be written still builds, because every generated
    /// file declares its namespace explicitly.</summary>
    /// <remarks>
    /// Written only when the text actually changes — like <see cref="WpfProjectFixup.Rewrite"/>
    /// and <see cref="ValidatorTemplate.ReconcileCustomStubNamespace"/> — because `dotnet new`
    /// writes the project file WITH a UTF-8 BOM and an unconditional
    /// <c>File.ReadAllText</c>/<c>File.WriteAllText</c> round trip silently writes it back without
    /// one, even when nothing else in the text changed. With no prefix that struck on every single
    /// scaffold, not just a re-scaffold, and falsified the invariant this whole feature is built
    /// against: a neutral customization must reproduce byte-for-byte what the generator already
    /// wrote.
    /// </remarks>
    private static void SetRootNamespace(string csprojPath, string? rootNamespace)
    {
        try
        {
            if (!File.Exists(csprojPath))
                return;

            var original = File.ReadAllText(csprojPath);
            var updated = CsprojEditing.SetRootNamespace(original, rootNamespace);

            if (!string.Equals(original, updated, StringComparison.Ordinal))
                File.WriteAllText(csprojPath, updated);
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

        // Keyed by the names the model has now: a class or field renamed on Review since the rows
        // were read would otherwise be generated with none of its data. Here rather than at each
        // caller, so the web, the console and an incremental add all get it.
        var importedData = request.ImportedData is { } rows
            ? ImportedDataRenames.Follow(rows, model)
            : null;

        // Through GeneratedProjectPath, not composed here, so the CLI's overwrite guard and this
        // writer cannot drift apart — see that type's remarks. Computed before the plan below,
        // not after: ScreenPlan needs the real root namespace to reserve its own outermost
        // segment against a same-named entity (issue #132), and a value re-derived later could
        // silently drift from the one templates actually get.
        var projectName = GeneratedProjectPath.ProjectName(appName, target);
        var rootNamespace = GeneratedProjectPath.RootNamespace(appName, target, request.Customization);

        // Prepended to generated C# only. XAML and the Data/Seed/*.json files go through the
        // same WriteSource, and a `//` banner would corrupt both.
        var header = FileHeader.For(request.Customization.Company, request.Year);

        // All of it in one call, because the build workflow's review panel shows this same split
        // before anything is written and the two must not be able to disagree. It settles, in
        // order: whether the auth core is emitted (it occupies "User", so a discovered entity can
        // only be renamed around it if that is known BEFORE the classes are built), the classes,
        // and the roles — classified here rather than at the point of use so the summary is
        // reported whether or not any screen reads them yet, making a misclassification visible
        // from the first build rather than from the first launch.
        var plan = ScreenPlan.For(model, request.Customization, target, rootNamespace);

        var auth = plan.Login;

        // Narrower than `auth`, and the two must not be confused: Session is a desktop-only static
        // that DesktopViewModelFiles alone emits, so a Blazor app has a login and no Session at
        // all. Repository and the two stores read the acting user from it, and naming it in a
        // Blazor app would emit code that cannot compile.
        var sessionAuth = auth && target.HasDesktopSession;

        var classes = plan.Classes;
        var classNames = plan.ClassNames;
        var roles = plan.Roles;

        var outputFolder = GeneratedProjectPath.Folder(workspaceRoot, appName, target);
        var csprojPath = GeneratedProjectPath.CsprojPath(workspaceRoot, appName, target);

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
            InjectPackage(csprojPath, EfCorePackageId, EfCorePackageVersion);

        // Not for the Web API: the ASP.NET Core framework reference already carries these
        // assemblies, and NuGet reports a reference to one of them as NU1510 — "likely
        // unnecessary" — on every build of the generated project.
        // Not for the worker either: Microsoft.Extensions.Hosting, which the worker project
        // already references, brings all three in transitively at whatever version it
        // resolves — and pinning LoggingTemplate's literal version alongside it is a
        // downgrade the moment Hosting moves ahead of that literal, which fails restore
        // with NU1605 under warnings-as-errors.
        if (request.Customization.Logging
            && target.ProjectType != DotNetProjectType.WebApi
            && target.ProjectType != DotNetProjectType.WorkerService)
        {
            foreach (var (id, version) in LoggingTemplate.Packages)
                InjectPackage(csprojPath, id, version);
        }

        if (request.Customization.HasBranding)
            InjectBranding(csprojPath, request.Customization.Company, request.Year);

        // Copied before the front end is built, not after it: the window or form has to name the
        // file, and an icon that could not be copied must leave nothing naming it behind.
        var customization = CopyIcon(
                request.Customization, outputFolder, csprojPath, target.ProjectType)
            ? request.Customization
            : request.Customization with { IconPath = null };

        // rootNamespace: computed above, before the plan. projectName goes on naming the folder,
        // the csproj, the assembly and the SQLite environment override — the prefix is a
        // namespace only.

        // Written on every run, not only at creation: a project re-scaffolded under a different
        // prefix must not keep pointing at the old namespace, and one re-scaffolded without a
        // prefix must lose the element entirely.
        SetRootNamespace(
            csprojPath, customization.EffectiveNamespacePrefix.Length == 0 ? null : rootNamespace);

        var modelsFolder = Path.Combine(outputFolder, "Models");
        var dataFolder = Path.Combine(outputFolder, "Data");
        var validationFolder = Path.Combine(outputFolder, "Validation");

        var written = new List<string>(
            WriteClasses(modelsFolder, classes, $"{rootNamespace}.Models", header));
        var expected = classes.Count;

        var entityNames = classes.Select(c => c.Name).ToList();

        // Projected through `classes`, so any review-loop edit is already accounted for.
        IReadOnlyList<string> unreadableSeedValues = [];
        var seedFiles = importedData is null
            ? Array.Empty<(string FileName, string Json)>()
            : SeedFileWriter.Files(classes, classNames, importedData, out unreadableSeedValues)
                .ToArray();

        var seededClassNames = seedFiles
            .Select(f => Path.GetFileNameWithoutExtension(f.FileName))
            .ToList();

        // Imported rows arrive THROUGH SeedData, so a data-driven build keeps it whatever the toggle
        // says: `noseed` suppresses the generator's own sample rows, not the user's own data. Folded
        // back into the customization so the front ends, which only see that, agree with the files.
        if (!customization.SampleSeedData && seedFiles.Length > 0)
            customization = customization with { SampleSeedData = true };

        // Shared, target-agnostic core: includes the IStore seam and StoreFactory, so every
        // target and persistence backend shares the same typed CRUD surface.
        var validation = customization.Validation;
        var seed = customization.SampleSeedData;
        var logging = customization.Logging;

        var files = new List<(string Folder, string Name, string Source)>
        {
            (dataFolder, "IStore.cs", StoreSeamTemplate.Source(rootNamespace)),
            (dataFolder, "StoreFactory.cs", StoreFactoryTemplate.Source(rootNamespace, persistence)),
            (dataFolder, "Repository.cs",
                RepositoryTemplate.Base(rootNamespace, validation, logging, sessionAuth)),
        };

        if (logging)
        {
            files.Add((Path.Combine(outputFolder, LoggingTemplate.FolderName),
                LoggingTemplate.FileName, LoggingTemplate.Source(rootNamespace)));
        }

        // Added here rather than in the initializer above, which cannot hold a conditional element,
        // and before SaveResult so a seeding app's files are written in the order they always were.
        if (seed)
        {
            files.Add((dataFolder, "SeedData.cs",
                SeedDataTemplate.Source(
                    rootNamespace,
                    SeedDataTemplate.OrderByDependency(classes),
                    SeedDataTemplate.ForeignKeyFieldNames(classes),
                    seededClassNames)));
        }

        files.Add((dataFolder, "SaveResult.cs", SaveResultTemplate.Source(rootNamespace)));

        // The auth core, for a target that has screens to gate. Emitted here rather than by
        // each target because all three UI targets receive identical files -- only the login
        // VIEW differs, and that stays with the target. `auth` was decided before the classes
        // were mapped, because it also reserves the name "User".
        if (auth)
        {
            foreach (var file in AuthCoreFiles.For(rootNamespace))
            {
                files.Add((
                    Path.Combine(outputFolder, file.RelativeFolder), file.Name, file.Source));
            }
        }

        // SaveResult above stays whatever the flag says: Repository still returns one, so the
        // console menu and the Web API's ValidationProblem are untouched by turning validation off.
        if (validation)
        {
            files.Add((validationFolder, "ValidationContext.cs",
                ValidationContextTemplate.Source(rootNamespace)));
            files.Add((validationFolder, "ValidationPipeline.cs",
                ValidationPipelineTemplate.Source(rootNamespace)));
            files.Add((validationFolder, "IValidator.cs",
                ValidatorTemplate.ValidatorInterface(rootNamespace)));
            files.Add((validationFolder, "IEntityLookup.cs",
                ValidatorTemplate.EntityLookupInterface(rootNamespace)));
            files.Add((validationFolder, "StoreEntityLookup.cs",
                ValidatorTemplate.StoreEntityLookup(rootNamespace)));
            files.Add((validationFolder, "ValidatorFactory.cs",
                ValidatorTemplate.Factory(rootNamespace, entityNames)));
        }
        else
        {
            // Permissions.cs (added below, unconditionally) names ValidationOperation and
            // IEntityLookup, so both have to survive novalidation even though nothing else in
            // this branch does. Security sits outside the flag on purpose — see PermissionsTemplate.
            files.Add((validationFolder, "ValidationContext.cs",
                ValidationContextTemplate.Source(rootNamespace)));
            files.Add((validationFolder, "IEntityLookup.cs",
                ValidatorTemplate.EntityLookupInterface(rootNamespace)));
        }

        // Emitted whether or not `validation` is true: turning validation off must not turn
        // permissions off. masterNames comes from the same role classification the review summary
        // and the screens use, so a screen and its permission cannot disagree.
        var masterNames = roles.Names(EntityRole.Master);
        files.Add((validationFolder, "Permissions.cs",
            PermissionsTemplate.Source(rootNamespace, masterNames, auth)));

        // Emitted for the same reason and beside the same file: task 3's handlers call
        // ActingUser regardless of the validation flag, so it is emitted regardless too. Passed
        // sessionAuth, not auth: a Blazor app has a login and no desktop Session at all, and
        // naming one here would emit code that does not compile — see ActingUserTemplate. auth
        // as well, because that Blazor app still has to be able to say who is acting.
        files.Add((validationFolder, "ActingUser.cs",
            ActingUserTemplate.Source(rootNamespace, sessionAuth, auth)));

        // Persistence backend behind the seam.
        if (persistence == PersistenceKind.Sqlite)
        {
            // Appended rather than mixed into entityNames, which also drives the class count,
            // the repositories and the validators -- none of which User takes part in.
            var contextEntities = auth
                ? entityNames.Append(AuthCoreFiles.EntityName).ToList()
                : entityNames;

            files.Add((dataFolder, "EfStore.cs", EfStoreTemplate.Source(rootNamespace, sessionAuth)));
            files.Add((dataFolder, "AppDbContext.cs",
                DbContextTemplate.Source(
                    projectName, contextEntities, EnvVarName(projectName), rootNamespace)));
        }
        else
        {
            files.Add((dataFolder, "JsonStore.cs",
                JsonStoreTemplate.Source($"{rootNamespace}.Data", sessionAuth)));
        }

        foreach (var name in entityNames)
            files.Add((dataFolder, $"{name}Repository.cs", RepositoryTemplate.Typed(rootNamespace, name)));

        // The regenerated half of each model's validator. Foreign-key targets come from
        // ForeignKeys.TargetsOf, so a lookup check is only ever emitted for a <Target>Id the
        // class actually declares.
        var classNameSet = ForeignKeys.ClassNameSet(classes);
        if (validation)
        {
            foreach (var definition in classes)
                files.Add((validationFolder, $"{definition.Name}Validator.Generated.cs",
                    ValidatorTemplate.Generated(
                        rootNamespace, definition, ForeignKeys.TargetsOf(definition, classNameSet),
                        referencesRequired: roles.RoleOf(definition.Name)
                            is EntityRole.Transaction or EntityRole.DocumentLine,
                        referencedBy: ForeignKeys.ReferencesTo(definition.Name, classes),
                        businessRules: definition.BusinessRules,
                        workflowRules: definition.WorkflowRules)));
        }

        // Target-specific front-end / entry point.
        foreach (var file in target.FrontEndFiles(
                     new FrontEndContext(projectName, entityNames, customization, roles, classes)))
            files.Add((Path.Combine(outputFolder, file.RelativeFolder), file.Name, file.Source));

        foreach (var (folder, name, source) in files)
        {
            expected++;
            var path = WriteSource(folder, name, source, header);
            if (path is not null)
                written.Add(path);
        }

        // After the files are written, not in FixupProject: Views/Theme.xaml does not exist yet
        // when the project is created, and the namespace has to be reconciled on every run rather
        // than only on the run that created the project.
        if (target.ProjectType == DotNetProjectType.Wpf)
        {
            WpfProjectFixup.MergeTheme(outputFolder);
            WpfProjectFixup.ReconcileNamespace(outputFolder, rootNamespace);

            // Only for an app with a login, whose emitted App.xaml.cs opens the first window
            // itself. Without a login the template's StartupUri is still what starts the app.
            if (auth)
                WpfProjectFixup.RemoveStartupUri(outputFolder);
        }

        // Same reason, different file: the Blazor template's Program.cs imports
        // `<ProjectName>.Components`, which a namespace prefix moves out from under it.
        if (target.ProjectType == DotNetProjectType.BlazorServer)
        {
            BlazorProjectFixup.ReconcileNamespace(outputFolder, rootNamespace);

            // The router has to run inside a circuit or the editor cannot ask before a rail click
            // throws away a half-typed record. Like the namespace, this lives in the shell the
            // template wrote, so the emitted files cannot switch it on themselves.
            BlazorProjectFixup.EnableInteractiveRouting(outputFolder);

            // Cookie authentication is the one thing the emitted files cannot switch on by
            // themselves: the services and the middleware live in the host the template wrote.
            if (auth)
                BlazorProjectFixup.EnableAuthentication(outputFolder, rootNamespace);
        }

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
        //
        // The fourth reconciliation point: an existing stub keeps its body but has its namespace
        // and app-rooted usings repointed on every run, the same as the app csproj, the test
        // csproj and (for WPF) App.xaml/App.xaml.cs -- otherwise a re-scaffold under a changed
        // prefix leaves it importing a namespace no generated file declares any more.
        //
        // The same write-once rule is why an app generated before the validation pipeline needs
        // TELLING: its stub keeps a hook the regenerated half no longer declares, and the pair
        // does not compile. Reported, never rewritten -- see ValidatorTemplate.HasOutdatedCustomHook.
        var outdatedStubs = new List<string>();

        // The stubs left as they were: still this generator's, so the record keeps them.
        var keptStubs = new List<string>();

        foreach (var definition in validation ? classes : Array.Empty<ClassDefinition>())
        {
            var fileName = $"{definition.Name}Validator.Custom.cs";
            var customPath = Path.Combine(validationFolder, fileName);

            if (File.Exists(customPath))
            {
                keptStubs.Add(customPath);

                ValidatorTemplate.ReconcileCustomStubNamespace(customPath, rootNamespace);

                if (ValidatorTemplate.HasOutdatedCustomHookAt(customPath))
                    outdatedStubs.Add(ValidatorTemplate.OutdatedCustomHookReport(
                        $"Validation/{fileName}", definition.Name));

                continue;
            }

            expected++;
            var writtenPath = WriteSource(
                validationFolder, fileName,
                ValidatorTemplate.CustomStub(rootNamespace, definition.Name), header);
            if (writtenPath is not null)
                written.Add(writtenPath);
        }

        var testWritten = new List<string>();
        var (testProjectPath, testProjectProblem) = ScaffoldTestProject(
            workspaceRoot, projectName, rootNamespace, classes, validation, seed, auth, sessionAuth,
            testWritten);

        var allWritten = written.Count == expected;

        // Overwriting replaces the generated sources, which includes removing the ones this model
        // no longer produces: a class renamed or removed on Review used to leave its files behind,
        // and after a rename to a name that could not compile, no overwrite could build again.
        // Only a complete run sweeps — a file this run failed to write says nothing about what
        // the model has, and must not cost the copy already on disk.
        var staleFiles = new List<string>();

        if (allWritten)
            staleFiles.AddRange(
                GeneratedFilesManifest.Sweep(outputFolder, written, keptStubs).Sentences(projectName));
        else
            GeneratedFilesManifest.Record(outputFolder, written);

        // What this app was generated with, so a later add writes its new classes the same way
        // rather than with whatever that run's answers happen to be.
        BuildSettingsRecord.Save(outputFolder, request with { Customization = customization });

        if (testProjectPath is not null)
        {
            var testFolder = Path.GetDirectoryName(testProjectPath)!;
            staleFiles.AddRange(
                GeneratedFilesManifest.Sweep(testFolder, testWritten, []).Sentences(
                    Path.GetFileName(testFolder)));
        }

        // A renamed entity is reported, never silent: the user described a class by one name and
        // the app declares it under another, so without this they go looking for something that
        // is not there.
        var renamed = classNames
            .Where(pair => !string.Equals(pair.Key, pair.Value, StringComparison.Ordinal))
            .Select(pair => $"{pair.Key} was renamed to {pair.Value}")
            .ToList();

        var renameNote = renamed.Count == 0 ? string.Empty : " " + string.Join("; ", renamed) + ".";

        // Appended to whichever message was chosen, and empty in every ordinary run: an app this
        // version of the tool generated has no outdated stub, so no existing message moves.
        var stubNote = outdatedStubs.Count == 0
            ? string.Empty
            : " " + string.Join("; ", outdatedStubs)
                  + ". These files are never overwritten, so the app will not compile until each "
                  + "is changed by hand.";

        var message = (allWritten
            ? $"Generated {classes.Count} class(es) + {target.Description}.{renameNote}"
            : $"Generated {written.Count} of {expected} file(s); " +
              "some files could not be written.") + stubNote;

        return new ScaffoldResult(
            allWritten, outputFolder, written, message, testProjectPath,
            ScreenSummary.Describe(roles))
        {
            OutdatedValidatorStubs = outdatedStubs,
            TestProjectProblem = testProjectProblem,
            UnreadableSeedValues = unreadableSeedValues,
            StaleFiles = staleFiles,
        };
    }

    /// <summary>
    /// Creates a sibling <c>&lt;appProject&gt;.Tests</c> xUnit project referencing the app project,
    /// with generated CRUD + seed tests. Best-effort: leaves the app itself intact if the test
    /// project could not be made, and returns a null path with one sentence saying why. The test
    /// project's TFM mirrors the app's, so a WPF app's tests target net10.0-windows and resolve
    /// the reference.
    /// </summary>
    /// <remarks>
    /// The problem is null only when there is a test project, or none was due (no classes). It
    /// used to be no sentence at all: `dotnet new xunit` rejected net10.0-windows, this returned
    /// null, and every WPF and WinForms app came out with no tests, an empty <c>.Tests</c>
    /// folder, and nothing on the screen to say so.
    /// </remarks>
    private static (string? Path, string? Problem) ScaffoldTestProject(
        string workspaceRoot, string appProjectName, string appRootNamespace,
        IReadOnlyList<ClassDefinition> classes, bool validation, bool seed, bool auth,
        bool sessionAuth, List<string> written)
    {
        var entityNames = classes.Select(c => c.Name).ToList();

        if (entityNames.Count == 0)
            return (null, null);

        var testProjectName = appProjectName + ".Tests";
        var testFolder = Path.Combine(workspaceRoot, testProjectName);
        var testCsproj = Path.Combine(testFolder, testProjectName + ".csproj");
        var appCsproj = Path.Combine("..", appProjectName, appProjectName + ".csproj");

        var framework = AppProjectFramework(workspaceRoot, appProjectName);

        if (!File.Exists(testCsproj))
        {
            var folderExisted = Directory.Exists(testFolder);

            // The template's -f lists only the plain frameworks, so net10.0-windows is asked for
            // as net10.0 and put back into the project file below.
            var created = DotNetProjectFactory.CreateProject(new ProjectRequest
            {
                ProjectName = testProjectName,
                OutputFolder = testFolder,
                ProjectType = DotNetProjectType.XUnitTest,
                Framework = TestProjectTemplates.TemplateFramework(framework)
            });

            if (!created.Success)
            {
                // The factory creates the folder before `dotnet new` runs, so a refusal leaves it
                // empty. Removed only when this run made it: an existing folder is not ours.
                if (!folderExisted)
                {
                    try { Directory.Delete(testFolder, recursive: true); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }

                return (null, NoTestProject($"`dotnet new xunit` failed: {OneLine(created.Message)}"));
            }

            // dotnet new xunit emits a UnitTest1.cs placeholder; replace it with our tests.
            var placeholder = Path.Combine(testFolder, "UnitTest1.cs");
            try { if (File.Exists(placeholder)) File.Delete(placeholder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

            try
            {
                var csproj = TestProjectTemplates.InjectProjectReference(
                    File.ReadAllText(testCsproj), appCsproj);

                File.WriteAllText(testCsproj, CsprojEditing.SetTargetFramework(csproj, framework));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (null, NoTestProject($"its project file could not be edited: {ex.Message}"));
            }
        }

        SetRootNamespace(
            testCsproj,
            appRootNamespace == appProjectName ? null : appRootNamespace + ".Tests");

        var roundTripPath = WriteSource(testFolder, "RepositoryRoundTripTests.cs",
            TestProjectTemplates.RoundTripTests(appRootNamespace, entityNames));

        // No seed test without SeedData: it would name a class the app never generated.
        var seedPath = seed
            ? WriteSource(testFolder, "SeedDataTests.cs",
                TestProjectTemplates.SeedTests(appRootNamespace, entityNames[0]))
            : null;
        var parallelizationPath = WriteSource(testFolder, "TestParallelization.cs",
            TestProjectTemplates.DisableParallelization());
        var supportPath = WriteSource(testFolder, "TestSupport.cs",
            TestProjectTemplates.TestSupport(appRootNamespace, auth, sessionAuth, seed, classes));

        var incomplete =
            $"The test project at {testFolder} is incomplete: some of its files could not be written.";

        if (roundTripPath is null || (seed && seedPath is null) || parallelizationPath is null
            || supportPath is null)
            return (null, incomplete);

        written.Add(roundTripPath);
        written.Add(parallelizationPath);
        written.Add(supportPath);
        if (seedPath is not null)
            written.Add(seedPath);

        // One validator test file per model, so the generated suite covers the generated rules —
        // and none at all when there are no validators to cover, since a test naming a validator
        // the app never generated would not compile.
        foreach (var definition in validation ? classes : Array.Empty<ClassDefinition>())
        {
            var required = definition.Properties.FirstOrDefault(p =>
                p.Type == "string"
                && p.Rules.Any(r => string.Equals(r.Name, "required", StringComparison.OrdinalIgnoreCase)));

            var testPath = WriteSource(testFolder, $"{definition.Name}ValidatorTests.cs",
                TestProjectTemplates.ValidatorTests(appRootNamespace, definition.Name, required?.Name));

            if (testPath is null)
                return (null, incomplete);

            written.Add(testPath);
        }

        return (testCsproj, null);
    }

    /// <summary>The sentence for a test project that was never made; the app is unaffected.</summary>
    private static string NoTestProject(string reason)
        => $"No test project was generated — {reason}. The app itself was written as normal.";

    /// <summary><paramref name="text"/> — `dotnet new`'s stderr, several lines — as one line,
    /// short enough to sit on the console beside everything else the run printed.</summary>
    private static string OneLine(string text)
    {
        var joined = string.Join(" ", text
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0));

        return joined.Length <= 300 ? joined : joined[..300] + "…";
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
