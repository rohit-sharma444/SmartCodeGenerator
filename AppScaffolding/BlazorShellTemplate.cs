namespace AppScaffolding;

/// <summary>
/// The parts of the <c>dotnet new blazor</c> shell that have to know about the discovered classes:
/// the navigation menu and the home page.
/// </summary>
/// <remarks>
/// Both are overwritten rather than added to. The template ships a nav menu pointing at its own
/// Counter and Weather samples, and a home page saying "Hello, world!" — leaving either in place
/// would give a generated app dead links and no way to reach its own pages.
/// </remarks>
public static class BlazorShellTemplate
{
    /// <summary>Source of <c>Components/Layout/NavMenu.razor</c>.</summary>
    public static string NavMenu(string title, IReadOnlyList<string> classNames)
        => NavMenu(title, classNames, login: false);

    /// <summary>
    /// Source of <c>Components/Layout/NavMenu.razor</c>. With <paramref name="login"/> the menu
    /// carries the signed-in user and a sign-out button.
    /// </summary>
    /// <remarks>
    /// Sign-out is a POST, not a link. A GET sign-out can be fired by any image tag on any page
    /// the visitor happens to open, which signs people out at random.
    /// </remarks>
    public static string NavMenu(string title, IReadOnlyList<string> classNames, bool login) => $$"""
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
{{NavItems(classNames)}}{{AdminNav(login)}}    </nav>
</div>
{{SignOut(login)}}
""";

    /// <summary>
    /// What AuthorizeView needs imported. The template's _Imports.razor has no authorization
    /// namespace in it, and rewriting that file would mean reconciling the project name it
    /// names, so each file that authorizes brings its own using.
    /// </summary>
    private static string NavUsings(bool login)
        => login ? "@using Microsoft.AspNetCore.Components.Authorization\n\n" : string.Empty;

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

    private static string NavItems(IReadOnlyList<string> classNames)
        => string.Concat(classNames.Select(name => $$"""
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="{{name.ToLowerInvariant()}}">
                <span aria-hidden="true"></span> {{name}}
            </NavLink>
        </div>

"""));

    /// <summary>Source of <c>Components/Pages/Home.razor</c>.</summary>
    /// <remarks>
    /// Lists the classes as links as well as the nav menu doing so: the nav is a narrow column that
    /// collapses on a small screen, and a first-time visitor should not have to find it to discover
    /// what the application contains.
    /// </remarks>
    public static string Home(string title, IReadOnlyList<string> classNames) => $$"""
@page "/"

<PageTitle>{{title}}</PageTitle>

<h1>{{title}}</h1>

<p>{{Summary(classNames)}}</p>

<ul>
{{HomeItems(classNames)}}</ul>
""";

    private static string Summary(IReadOnlyList<string> classNames)
        => classNames.Count switch
        {
            0 => "This application has no classes yet.",
            1 => "One kind of record, listed below.",
            _ => $"{classNames.Count} kinds of record, listed below.",
        };

    private static string HomeItems(IReadOnlyList<string> classNames)
        => string.Concat(classNames.Select(name =>
            $"    <li><a href=\"{name.ToLowerInvariant()}\">{name}</a></li>" + Environment.NewLine));
}
