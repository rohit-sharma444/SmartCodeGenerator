using AppScaffolding.Auth;
using AppScaffolding.Layouts;
using AppScaffolding.Screens;
using CodeGenerationEngine.Models;

namespace AppScaffolding.Targets;

/// <summary>A Blazor Server web application with a page per discovered class.</summary>
/// <remarks>
/// Distinct from <see cref="WebApiTarget"/>, which exposes endpoints and no interface. This is the
/// browser equivalent of the desktop targets: the same list-edit-save surface, reached with a URL
/// instead of a window.
/// <para>
/// <c>dotnet new blazor</c> on .NET 10 already registers interactive server components and the
/// interactive render mode, so nothing here has to rewrite <c>Program.cs</c> — the pages only need
/// to declare <c>@rendermode InteractiveServer</c>. Checked against the template rather than assumed;
/// on earlier SDKs interactivity was opt-in and this target would need more.
/// </para>
/// </remarks>
public sealed class BlazorTarget : ITargetScaffolder
{
    /// <summary>Where the template puts its pages, and so where ours go.</summary>
    private const string PagesFolder = "Components/Pages";

    /// <summary>The two sample pages <c>dotnet new blazor</c> emits, which a generated app has no use for.</summary>
    private static readonly string[] TemplateSamples = ["Counter.razor", "Weather.razor"];

    public string Key => "blazor";

    public string Description => "a Blazor Server web app with a page per class";

    public bool HasScreens => true;

    public DotNetProjectType ProjectType => DotNetProjectType.BlazorServer;

    public string FolderSuffix => ".Web";

    public IReadOnlyList<GeneratedFile> FrontEndFiles(
        string projectName, IReadOnlyList<string> entityNames)
        => FrontEndFiles(projectName, entityNames, AppCustomization.Default);

    /// <summary>
    /// The web screens need the entities' roles, which names alone cannot carry, so this target
    /// implements the context overload rather than taking the default.
    /// </summary>
    public IReadOnlyList<GeneratedFile> FrontEndFiles(FrontEndContext context)
        => FrontEndFiles(
            context.ProjectName, context.EntityNames, context.Customization, context.Roles);

    public IReadOnlyList<GeneratedFile> FrontEndFiles(
        string projectName, IReadOnlyList<string> entityNames, AppCustomization customization)
        => FrontEndFiles(projectName, entityNames, customization, EntityRoleMap.Empty);

    /// <summary>
    /// Every file this target emits.
    /// </summary>
    /// <remarks>
    /// The screens are not behind a layout switch as they are on the desktop: a layout shapes a
    /// window, `build app` tells the user outright that naming one is ignored for this target, and
    /// the web has only ever had one shape. So the roles decide which screen a page renders, and
    /// every generated web app gets these screens.
    /// </remarks>
    private IReadOnlyList<GeneratedFile> FrontEndFiles(
        string projectName, IReadOnlyList<string> entityNames, AppCustomization customization,
        EntityRoleMap roles)
    {
        // The namespace the emitted code declares; projectName still names the app in its title.
        var ns = customization.RootNamespaceFor(projectName);
        var title = customization.TitleLiteralFor(projectName);

        // A document line gets no page at all: it is edited inside its parent, which is the whole
        // point of the third role. Everything below works from these names, so the rail, the
        // dashboard and the routes cannot disagree about which entities the app has.
        var pageable = entityNames
            .Where(name => roles.RoleOf(name) != EntityRole.DocumentLine)
            .ToList();

        var files = new List<GeneratedFile>
        {
            new("Components/Layout", "NavMenu.razor",
                BlazorShellTemplate.NavMenu(title, pageable, customization.Login, roles)),

            new(PagesFolder, "Home.razor",
                BlazorShellTemplate.Home(
                    title, pageable, customization.Login, roles, ns, customization.SampleSeedData)),

            // The three screens every page renders one of. Generic, so these are the whole of the
            // find-open-save surface however many entities the model has.
            new(BlazorScreenTemplates.Folder, "MasterScreen.razor",
                BlazorScreenTemplates.MasterScreen(ns, customization.Validation, customization.Login)),

            new(BlazorScreenTemplates.Folder, "TransactionScreen.razor",
                BlazorScreenTemplates.TransactionScreen(ns, customization.Validation, customization.Login)),

            new(BlazorScreenTemplates.Folder, "RecordEditor.razor",
                BlazorScreenTemplates.RecordEditor(ns, customization.Validation)),

            new("Screens", "ScreenFields.cs", BlazorScreenTemplates.Fields(ns)),

            // The lines of a transaction, which the editor holds and the page names. The same file
            // the desktop targets emit: one set of semantics, three views.
            new("Screens", "LineSet.cs", LineSetTemplate.Source(ns)),
        };

        // Two pages each: the list, and the editor behind /<entity>/<id> and /<entity>/new.
        //
        // "<Entity>Page.razor", not "<Entity>.razor": a Razor file compiles to a class named after
        // it, so Book.razor would produce Components.Pages.Book and SHADOW Models.Book -- inside the
        // page the bare name Book then means the component, and nothing compiles. Found by building a
        // generated app; markup assertions cannot see it. The route stays "/book".
        files.AddRange(pageable.SelectMany(name => new[]
        {
            new GeneratedFile(
                PagesFolder, BlazorPageTemplate.ListFileName(name),
                BlazorPageTemplate.ListPage(
                    ns, name, customization.SampleSeedData, customization.Login, roles.RoleOf(name))),

            new GeneratedFile(
                PagesFolder, BlazorPageTemplate.EditorFileName(name),
                BlazorPageTemplate.EditorPage(
                    ns, name, customization.SampleSeedData, customization.Login,
                    roles.LinesOf(name))),
        }));

        // The login page, the endpoints that set the cookie, and a router that actually reads
        // the [Authorize] attributes above -- the template's RouteView does not.
        if (customization.Login)
        {
            var login = new LayoutContext(ns, projectName, customization, Palette: null);

            files.Add(new(PagesFolder, "Login.razor", BlazorLoginTemplate.LoginPage(login)));
            files.Add(new("Components", "Routes.razor", BlazorLoginTemplate.Routes(ns)));
            files.Add(new("Auth", "AuthEndpoints.cs", BlazorLoginTemplate.Endpoints(ns)));
            files.Add(new(PagesFolder, "UsersPage.razor", BlazorLoginTemplate.UsersPage(ns)));
        }

        // The desktop targets seed from their main view model and the console and Web API from their
        // own Program.cs. This target does not emit Program.cs -- dotnet new blazor writes a good one,
        // and replacing it would mean re-implementing the host -- so the first page to load seeds
        // instead, through a gate that keeps it to once per process.
        var seedGate = BlazorSeedTemplate.Source(ns, customization.SampleSeedData);

        if (seedGate is not null)
        {
            files.Add(new GeneratedFile("Data", "AppSeed.cs", seedGate));
        }

        return files;
    }

    /// <summary>
    /// Removes the template's sample pages.
    /// </summary>
    /// <remarks>
    /// Best-effort, like the class library's Class1.cs removal: a leftover Counter page is untidy but
    /// harmless, and is not worth failing a generation the user has already answered questions for.
    /// The nav menu no longer links to them either way.
    /// </remarks>
    public void FixupProject(string outputFolder)
    {
        foreach (var sample in TemplateSamples)
        {
            var path = Path.Combine(outputFolder, "Components", "Pages", sample);

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
