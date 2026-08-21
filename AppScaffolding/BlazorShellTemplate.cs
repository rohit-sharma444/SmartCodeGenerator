using AppScaffolding.Screens;

namespace AppScaffolding;

/// <summary>
/// The parts of the <c>dotnet new blazor</c> shell that have to know about the discovered classes:
/// the navigation menu and the home page.
/// </summary>
/// <remarks>
/// Both are overwritten rather than added to. The template ships a nav menu pointing at its own
/// Counter and Weather samples, and a home page saying "Hello, world!" — leaving either in place
/// would give a generated app dead links and no way to reach its own pages.
/// <para>
/// Both are grouped by role, which is the web's half of the business shell: the rail says which
/// entities are reference data and which are the work, and the home page is a dashboard of counts
/// under the same headings rather than a bare list of links.
/// </para>
/// </remarks>
public static class BlazorShellTemplate
{
    /// <summary>Source of <c>Components/Layout/NavMenu.razor</c> for an app whose entities are all
    /// masters.</summary>
    public static string NavMenu(string title, IReadOnlyList<string> classNames)
        => NavMenu(title, classNames, login: false, EntityRoleMap.Empty);

    /// <summary>
    /// Source of <c>Components/Layout/NavMenu.razor</c>. With <paramref name="login"/> the menu
    /// carries the signed-in user and a sign-out button.
    /// </summary>
    /// <remarks>
    /// Sign-out is a POST, not a link. A GET sign-out can be fired by any image tag on any page
    /// the visitor happens to open, which signs people out at random.
    /// </remarks>
    public static string NavMenu(
        string title, IReadOnlyList<string> classNames, bool login, EntityRoleMap roles) => $$"""
{{NavUsings(login)}}<div class="top-row ps-3 navbar navbar-dark">
    <div class="container-fluid">
        <a class="navbar-brand" href="">{{title}}</a>
    </div>
</div>

<input type="checkbox" title="Navigation menu" class="navbar-toggler" />

<div class="nav-scrollable" onclick="document.querySelector('.navbar-toggler').click()">
    <nav class="nav flex-column">
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="" Match="NavLinkMatch.All">
                <span aria-hidden="true"></span> Home
            </NavLink>
        </div>
{{NavGroups(classNames, roles)}}{{AdminNav(login)}}    </nav>
</div>
{{SignOut(login)}}
""";

    /// <summary>
    /// What AuthorizeView needs imported. The template's _Imports.razor has no authorization
    /// namespace in it, and rewriting that file would mean reconciling the project name it
    /// names, so each file that authorizes brings its own using.
    /// </summary>
    private static string NavUsings(bool login)
        => login ? "@using Microsoft.AspNetCore.Components.Authorization\r\n\r\n" : string.Empty;

    /// <summary>
    /// The Users link, shown only to an administrator.
    /// </summary>
    /// <remarks>
    /// Hiding the link is not the permission — the page carries [Authorize(Roles = "Admin")]
    /// for that, because a URL can be typed. This only keeps the nav honest about what the
    /// signed-in user can reach.
    /// </remarks>
    private static string AdminNav(bool login)
        => login
            ? """
        <AuthorizeView Roles="Admin">
            <Authorized>
                <div class="nav-item px-3 pt-3 text-uppercase small text-body-secondary">Admin</div>
                <div class="nav-item px-3">
                    <NavLink class="nav-link" href="users">
                        <span aria-hidden="true"></span> Users
                    </NavLink>
                </div>
            </Authorized>
        </AuthorizeView>

"""
            : string.Empty;

    /// <summary>The signed-in strip, or nothing at all for an app with no login.</summary>
    private static string SignOut(bool login)
        => login
            ? """

<AuthorizeView>
    <Authorized>
        <div class="nav-scrollable px-3 py-2">
            <span class="text-body-secondary small">@context.User.Identity?.Name</span>
            <form method="post" action="/auth/logout">
                <AntiforgeryToken />
                <button type="submit" class="btn btn-link nav-link p-0">Sign out</button>
            </form>
        </div>
    </Authorized>
</AuthorizeView>
"""
            : string.Empty;

    /// <summary>
    /// The rail, grouped the way the desktop rail is grouped.
    /// </summary>
    /// <remarks>
    /// Masters first and transactions second, matching <c>ShellViewModel.Groups</c> — the two rails
    /// are the same rail on different stacks, and a user moving between them should not have to
    /// re-learn the order.
    /// </remarks>
    private static string NavGroups(IReadOnlyList<string> classNames, EntityRoleMap roles)
        => string.Concat(
            NavGroup("Masters", Named(classNames, roles, EntityRole.Master)),
            NavGroup("Transactions", Named(classNames, roles, EntityRole.Transaction)));

    /// <summary>The names with this role, in the order they were discovered.</summary>
    /// <remarks>
    /// Filtered from the caller's list rather than read out of the map, so an entity the classifier
    /// never saw still appears — as a master, which is what <c>RoleOf</c> answers for it.
    /// </remarks>
    private static IReadOnlyList<string> Named(
        IReadOnlyList<string> classNames, EntityRoleMap roles, EntityRole role)
        => [.. classNames.Where(name => roles.RoleOf(name) == role)];

    /// <summary>A heading with nothing under it is noise, so an empty group is not emitted at
    /// all.</summary>
    private static string NavGroup(string title, IReadOnlyList<string> names)
        => names.Count == 0
            ? string.Empty
            : $"        <div class=\"nav-item px-3 pt-3 text-uppercase small text-body-secondary\">"
              + $"{title}</div>{Environment.NewLine}"
              + NavItems(names);

    private static string NavItems(IReadOnlyList<string> classNames)
        => string.Concat(classNames.Select(name => $$"""
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="{{BlazorPageTemplate.Route(name)}}">
                <span aria-hidden="true"></span> {{name}}
            </NavLink>
        </div>

"""));

    /// <summary>Source of <c>Components/Pages/Home.razor</c> for an app whose entities are all
    /// masters and which seeds nothing.</summary>
    public static string Home(string title, IReadOnlyList<string> classNames)
        => Home(title, classNames, login: false, EntityRoleMap.Empty, "Acme", seed: false);

    /// <summary>
    /// Source of <c>Components/Pages/Home.razor</c>: the dashboard.
    /// </summary>
    /// <remarks>
    /// A count per entity under the same headings the rail uses, which is what makes the home page
    /// worth arriving at rather than a second copy of the navigation. The counts are read through
    /// <c>StoreFactory</c>, the same seam the screens read, so the tile and the list it links to
    /// cannot disagree.
    /// <para>
    /// With <paramref name="login"/> it refuses an unauthenticated visitor like every other page.
    /// The home page needs the attribute as much as the entity pages do, and is the easiest to
    /// forget: it names every class the application holds, so left open it hands an anonymous
    /// visitor the shape of the whole model. Found by requesting "/" on a running app after every
    /// page test passed.
    /// </para>
    /// </remarks>
    public static string Home(
        string title, IReadOnlyList<string> classNames, bool login, EntityRoleMap roles,
        string rootNamespace, bool seed) => $$"""
@page "/"{{HomeAuthorize(login)}}
@using {{rootNamespace}}.Data
@using {{rootNamespace}}.Models

<PageTitle>{{title}}</PageTitle>

<h1>{{title}}</h1>

<p>{{Summary(classNames)}}</p>

@foreach (var group in Groups)
{
    <h2 class="h5 mt-4">@group.Title</h2>

    <div class="d-flex flex-wrap gap-3">
        @foreach (var tile in group.Tiles)
        {
            <a class="card text-decoration-none p-3" style="min-width: 9rem" href="@tile.Route">
                <span class="fs-3">@Count(tile.Name)</span>
                <span class="text-body-secondary">@tile.Name</span>
            </a>
        }
    </div>
}

@code {
    private sealed record Tile(string Name, string Route);

    private sealed record TileGroup(string Title, IReadOnlyList<Tile> Tiles);

    /// <summary>Transactions first: on a business screen they are the work, and the masters are
    /// what the work refers to.</summary>
    private static readonly TileGroup[] Groups =
    [
{{TileGroups(classNames, roles)}}    ];

    private readonly Dictionary<string, int> _counts = new();

    protected override void OnInitialized()
    {
{{HomeSeeding(seed)}}{{Counts(classNames, roles)}}    }

    private int Count(string name) => _counts.TryGetValue(name, out var count) ? count : 0;
}
""";

    /// <summary>The authorization attribute and the namespace it needs, or nothing for an app
    /// with no login.</summary>
    private static string HomeAuthorize(bool login)
        => login
            ? "\r\n@using Microsoft.AspNetCore.Authorization\r\n@attribute [Authorize]"
            : string.Empty;

    private static string Summary(IReadOnlyList<string> classNames)
        => classNames.Count switch
        {
            0 => "This application has no classes yet.",
            1 => "One kind of record.",
            _ => $"{classNames.Count} kinds of record.",
        };

    /// <summary>The tile groups, transactions first, with an empty group left out.</summary>
    private static string TileGroups(IReadOnlyList<string> classNames, EntityRoleMap roles)
        => string.Concat(
            TileGroup("Transactions", Named(classNames, roles, EntityRole.Transaction)),
            TileGroup("Masters", Named(classNames, roles, EntityRole.Master)));

    private static string TileGroup(string title, IReadOnlyList<string> names)
        => names.Count == 0
            ? string.Empty
            : $"        new(\"{title}\", ["
              + string.Join(", ", names.Select(name => $"new(\"{name}\", \"{BlazorPageTemplate.Route(name)}\")"))
              + $"]),{Environment.NewLine}";

    /// <summary>
    /// One count per entity, read through the store seam.
    /// </summary>
    /// <remarks>
    /// A line per entity because the type has to be named: <c>StoreFactory.Create&lt;T&gt;()</c> is
    /// generic, and this page holds names rather than types.
    /// </remarks>
    private static string Counts(IReadOnlyList<string> classNames, EntityRoleMap roles)
        => string.Concat(
            classNames
                .Where(name => roles.RoleOf(name) != EntityRole.DocumentLine)
                .Select(name =>
                    $"        _counts[\"{name}\"] = StoreFactory.Create<{name}>().GetAll().Count;"
                    + Environment.NewLine));

    /// <summary>The seeding call, so the first tile shows the sample data rather than zero.</summary>
    private static string HomeSeeding(bool seed)
    {
        var once = BlazorSeedTemplate.OnceCall(seed);

        return once.Length == 0 ? string.Empty : $"        {once}{Environment.NewLine}{Environment.NewLine}";
    }
}
