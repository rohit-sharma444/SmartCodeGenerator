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
