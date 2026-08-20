using System.Text.RegularExpressions;

namespace AppScaffolding;

/// <summary>
/// Reconciles the <c>Program.cs</c> that <c>dotnet new blazor</c> emits with the namespace the
/// generated code declares. Runs only against a project this scaffolder just created.
/// </summary>
/// <remarks>
/// <see cref="Targets.BlazorTarget"/> deliberately does not emit <c>Program.cs</c> — the template's
/// is correct and re-implementing the host would mean drifting from it on every SDK update. But the
/// template writes <c>using &lt;ProjectName&gt;.Components;</c>, a literal namespace based on the
/// project name at creation time, and a namespace prefix moves <c>App.razor</c> to
/// <c>&lt;Prefix&gt;.&lt;ProjectName&gt;.Components</c>. Nothing then resolves <c>App</c> in
/// <c>MapRazorComponents&lt;App&gt;()</c> and the whole project fails with CS0246.
/// <para>
/// So one line is repointed instead of the file being replaced. Like
/// <see cref="WpfProjectFixup.ReconcileNamespace"/> this runs on every scaffold and matches the
/// SHAPE of what is there rather than a remembered previous value, which a re-scaffold under a
/// changed prefix would not have.
/// </para>
/// </remarks>
public static class BlazorProjectFixup
{
    /// <summary>What only the template's own <c>Program.cs</c> contains, used to be sure this is
    /// the file the rewrite is meant for before touching any of its usings.</summary>
    private const string TemplateMarker = "MapRazorComponents<App>";

    private static readonly Regex ComponentsImport =
        new(@"^using\s+[\w.]+\.Components;", RegexOptions.Multiline);

    private const string BuildMarker = "var app = builder.Build();";
    private const string AntiforgeryMarker = "app.UseAntiforgery();";
    private const string AuthenticationMarker = "app.UseAuthentication();";
    private const string EndpointMarker = "app.MapRazorComponents<App>()";
    private const string RunMarker = "app.Run();";

    private const string CookieUsing = "using Microsoft.AspNetCore.Authentication.Cookies;";

    private const string Services = """
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

""";

    /// <summary>
    /// Adds cookie authentication to the template's <c>Program.cs</c>. Best-effort and idempotent.
    /// </summary>
    /// <remarks>
    /// Insertions rather than a replacement, for the reason <see cref="ReconcileNamespace"/>
    /// gives: the template's host is correct, and re-implementing it would mean drifting from it
    /// on every SDK update.
    /// <para>
    /// Middleware order is not a preference. <c>UseAuthentication</c> and <c>UseAuthorization</c>
    /// must run BEFORE <c>UseAntiforgery</c>, so the insertion point is that call — and when a
    /// host has none, the endpoint mapping instead, which every Blazor host has. Anchoring only
    /// on antiforgery would silently leave a host unauthenticated.
    /// </para>
    /// <para>
    /// <c>MapStaticAssets</c> is deliberately untouched: moving or dropping it breaks the static
    /// web asset manifest and the application serves no stylesheets at all.
    /// </para>
    /// </remarks>
    public static void EnableAuthentication(string projectFolder, string rootNamespace)
    {
        var path = Path.Combine(projectFolder, "Program.cs");

        try
        {
            if (!File.Exists(path))
                return;

            var text = File.ReadAllText(path);

            // Not a host this rewrite understands, or one it has already rewritten.
            if (!text.Contains(TemplateMarker, StringComparison.Ordinal)
                || text.Contains(AuthenticationMarker, StringComparison.Ordinal))
                return;

            var buildIndex = text.IndexOf(BuildMarker, StringComparison.Ordinal);

            var middlewareIndex = text.IndexOf(AntiforgeryMarker, StringComparison.Ordinal);

            if (middlewareIndex < 0)
                middlewareIndex = text.IndexOf(EndpointMarker, StringComparison.Ordinal);

            if (buildIndex < 0 || middlewareIndex < 0)
                return;

            // The later insertion first: inserting at the earlier index would shift this offset.
            text = text.Insert(
                middlewareIndex,
                AuthenticationMarker + "\napp.UseAuthorization();\n\n");

            text = text.Insert(buildIndex, Services);

            // Both usings, and the auth one is NOT optional: MapAuthEndpoints is an extension
            // method, so without its namespace Program.cs fails with CS1061 rather than CS0246,
            // which reads as a missing package. Found by building a generated app.
            var authUsing = $"using {rootNamespace}.Auth;";

            if (!text.Contains(authUsing, StringComparison.Ordinal))
                text = authUsing + "\n" + text;

            if (!text.Contains(CookieUsing, StringComparison.Ordinal))
                text = CookieUsing + "\n" + text;

            // Mapped beside the component endpoints, so a POST to /auth/login is matched by the
            // endpoint rather than swallowed by the router.
            if (text.Contains(RunMarker, StringComparison.Ordinal))
                text = text.Replace(
                    RunMarker, "app.MapAuthEndpoints();\n\n" + RunMarker);

            File.WriteAllText(path, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leave it; the build error that follows is clearer than a crash here.
        }
    }

    /// <summary>
    /// Repoints <c>Program.cs</c>'s components import at <paramref name="rootNamespace"/>.
    /// Best-effort and idempotent.
    /// </summary>
    public static void ReconcileNamespace(string projectFolder, string rootNamespace)
    {
        var path = Path.Combine(projectFolder, "Program.cs");
        var wanted = $"using {rootNamespace}.Components;";

        try
        {
            if (!File.Exists(path))
                return;

            var original = File.ReadAllText(path);

            // Already right, or not the file this rewrite understands. Either way, leave it alone.
            if (original.Contains(wanted, StringComparison.Ordinal)
                || !original.Contains(TemplateMarker, StringComparison.Ordinal))
                return;

            // Only the first match, and only one whose namespace ends in `.Components`: a hand-added
            // `using SomeLibrary.Components;` in a project the user has since edited is none of this
            // method's business, and the template's own import is the first line of the file.
            var updated = ComponentsImport.Replace(original, wanted, 1);

            if (!string.Equals(original, updated, StringComparison.Ordinal))
                File.WriteAllText(path, updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leave it; the build error that follows is clearer than a crash here.
        }
    }
}
