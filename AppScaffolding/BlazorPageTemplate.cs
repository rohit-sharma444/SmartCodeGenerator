using AppScaffolding.Screens;

namespace AppScaffolding;

/// <summary>
/// Two Blazor pages per class: the list its role calls for, and the editor behind
/// <c>/&lt;entity&gt;/&lt;id&gt;</c>.
/// </summary>
/// <remarks>
/// The pages are deliberately thin. Everything they do is in the screen components, which are
/// generic and shared, so a page is a route, an authorization attribute and one element — the web's
/// version of "three templates, not N views". A model of twenty entities gets forty short pages
/// rather than twenty long ones.
/// <para>
/// <c>&lt;Entity&gt;Page.razor</c>, not <c>&lt;Entity&gt;.razor</c>: a Razor file compiles to a
/// class named after it, so <c>Book.razor</c> would produce <c>Components.Pages.Book</c> and SHADOW
/// <c>Models.Book</c> — inside the page the bare name <c>Book</c> then means the component, and
/// nothing compiles. Found by building a generated app; markup assertions cannot see it. The route
/// stays <c>/book</c>.
/// </para>
/// </remarks>
public static class BlazorPageTemplate
{
    /// <summary>The route segment an entity's pages live under.</summary>
    public static string Route(string className) => className.ToLowerInvariant();

    /// <summary>The file name of an entity's list page.</summary>
    public static string ListFileName(string className) => $"{className}Page.razor";

    /// <summary>The file name of an entity's editor page.</summary>
    public static string EditorFileName(string className) => $"{className}EditorPage.razor";

    /// <summary>
    /// Source of <c>Components/Pages/&lt;className&gt;Page.razor</c>: the list.
    /// </summary>
    /// <param name="rootNamespace">Namespace the generated code declares, prefix included.</param>
    /// <param name="className">The model class this page lists.</param>
    /// <param name="seed">
    /// Whether the app has sample data to seed. When it does the page asks
    /// <see cref="BlazorSeedTemplate"/>'s gate to run the seeder before the screen reads the store —
    /// this target emits no <c>Program.cs</c>, so there is no startup hook to do it from.
    /// </param>
    /// <param name="login">Whether the app has accounts, and so whether the page refuses an
    /// unauthenticated visitor.</param>
    /// <param name="role">Which screen the entity gets. A transaction gets the filtered, totalled
    /// list; anything else gets the plain one.</param>
    public static string ListPage(
        string rootNamespace, string className, bool seed, bool login, EntityRole role)
    {
        var screen = role == EntityRole.Transaction ? "TransactionScreen" : "MasterScreen";

        return $$"""
@page "/{{Route(className)}}"
@rendermode InteractiveServer{{Authorize(login)}}
@using {{rootNamespace}}.Components.Screens
@using {{rootNamespace}}.Models{{SeedUsing(rootNamespace, seed)}}

<PageTitle>{{className}}</PageTitle>

<{{screen}} TRecord="{{className}}" Title="{{className}}" Route="{{Route(className)}}" />
{{Seeding(seed)}}
""";
    }

    /// <summary>
    /// Source of <c>Components/Pages/&lt;className&gt;EditorPage.razor</c>: one record.
    /// </summary>
    /// <remarks>
    /// Two routes on one page, which is what makes New and Open the same screen: <c>/order/7</c>
    /// arrives with an id and <c>/order/new</c> arrives without one. A separate "new" page would be
    /// the same file twice.
    /// </remarks>
    /// <param name="lines">
    /// The line classes this record holds, if any. Named here because the page is the only place
    /// that knows both types — the editor component is generic over the record alone.
    /// </param>
    public static string EditorPage(
        string rootNamespace, string className, bool seed, bool login,
        IReadOnlyList<string> lines)
        => $$"""
@page "/{{Route(className)}}/{Id:int}"
@page "/{{Route(className)}}/new"
@rendermode InteractiveServer{{Authorize(login)}}
@using {{rootNamespace}}.Components.Screens
@using {{rootNamespace}}.Models
@using {{rootNamespace}}.Screens{{SeedUsing(rootNamespace, seed)}}

<PageTitle>{{className}}</PageTitle>

<RecordEditor TRecord="{{className}}" Id="@Id" Title="{{className}}" Route="{{Route(className)}}"{{LineSets(className, lines)}} />

@code {
    /// <summary>Null on the "new" route, which is how the editor knows which it is.</summary>
    [Parameter] public int? Id { get; set; }
{{SeedingMember(seed)}}}
""";

    /// <summary>
    /// The authorization attribute and the namespace it needs, or nothing at all for an app with no
    /// login.
    /// </summary>
    /// <remarks>
    /// The attribute alone decides nothing: it is honoured only because the emitted
    /// <c>Routes.razor</c> uses an AuthorizeRouteView. The template's plain RouteView ignores it,
    /// which is how an unauthorized page shipped once already.
    /// </remarks>
    private static string Authorize(bool login)
        => login
            ? "\r\n@using Microsoft.AspNetCore.Authorization\r\n@attribute [Authorize]"
            : string.Empty;

    /// <summary>
    /// The line sets, as a factory the editor calls once per record.
    /// </summary>
    /// <remarks>
    /// The foreign key is <c>&lt;Parent&gt;Id</c> by construction: the classifier only calls a
    /// class a line when it has exactly that property.
    /// </remarks>
    private static string LineSets(string className, IReadOnlyList<string> lines)
        => lines.Count == 0
            ? string.Empty
            : "\r\n              Lines=\"@(() => new ILineSet[] { "
              + string.Join(", ", lines.Select(line => $"new LineSet<{line}>(\"{line}\", \"{className}Id\")"))
              + " })\"";

    /// <summary>Where the seed gate lives, imported only by a page that calls it.</summary>
    private static string SeedUsing(string rootNamespace, bool seed)
        => seed ? $"\r\n@using {rootNamespace}.Data" : string.Empty;

    /// <summary>
    /// The page's seeding call, or nothing when the app has no sample data.
    /// </summary>
    /// <remarks>
    /// Every page seeds, not just the home page: a visitor can arrive at any URL, and a bookmarked
    /// <c>/order</c> should not show an empty list because nobody visited <c>/</c> first. The gate
    /// makes the redundancy free after the first call.
    /// <para>
    /// It runs in the PAGE rather than in the screen component, because a parent initialises before
    /// its children — so the store is seeded by the time the list reads it.
    /// </para>
    /// </remarks>
    private static string Seeding(bool seed)
    {
        var once = BlazorSeedTemplate.OnceCall(seed);

        return once.Length == 0
            ? string.Empty
            : $$"""

                @code {
                    protected override void OnInitialized() { {{once}} }
                }
                """;
    }

    /// <summary>The same call, for a page that already has a code block.</summary>
    private static string SeedingMember(bool seed)
    {
        var once = BlazorSeedTemplate.OnceCall(seed);

        return once.Length == 0
            ? string.Empty
            : $$"""

                    protected override void OnInitialized() { {{once}} }

                """;
    }
}
