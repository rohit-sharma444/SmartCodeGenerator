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
    public static string NavMenu(string title, IReadOnlyList<string> classNames) => $$"""
<div class="top-row ps-3 navbar navbar-dark">
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
{{NavItems(classNames)}}    </nav>
</div>
""";

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
