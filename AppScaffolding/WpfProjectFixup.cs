using System.Text.RegularExpressions;

namespace AppScaffolding;

/// <summary>
/// Reconciles the files `dotnet new wpf` emits with the foldered layout this
/// scaffolder writes. Runs only against a project this scaffolder just created.
/// </summary>
public static class WpfProjectFixup
{
    private const string TemplateStartupUri = "StartupUri=\"MainWindow.xaml\"";
    private const string FolderedStartupUri = "StartupUri=\"Views/MainWindow.xaml\"";

    /// <summary>
    /// Deletes the template's root window and repoints <c>App.xaml</c> at
    /// <c>Views/MainWindow.xaml</c>. Best-effort and idempotent.
    /// </summary>
    public static void Apply(string projectFolder)
    {
        // The template's MainWindow declares the same x:Class as the one written
        // into Views/, so it must be removed, not merely shadowed.
        Delete(Path.Combine(projectFolder, "MainWindow.xaml"));
        Delete(Path.Combine(projectFolder, "MainWindow.xaml.cs"));

        RepointStartupUri(Path.Combine(projectFolder, "App.xaml"));
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leave it; the build error that follows is clearer than a crash here.
        }
    }

    private const string MergeMarker = "Views/Theme.xaml";

    /// <summary>
    /// Merges <c>Views/Theme.xaml</c> into <c>App.xaml</c>'s resources when the scaffolder wrote
    /// one. Best-effort and idempotent, like the StartupUri repoint above it.
    /// </summary>
    /// <remarks>
    /// Called after the front-end files are written, not from <see cref="Apply"/>: the theme file
    /// does not exist yet when the project is first created.
    /// </remarks>
    public static void MergeTheme(string projectFolder)
    {
        try
        {
            var appXamlPath = Path.Combine(projectFolder, "App.xaml");

            if (!File.Exists(appXamlPath)
                || !File.Exists(Path.Combine(projectFolder, "Views", ThemeXamlTemplate.FileName)))
                return;

            var xaml = File.ReadAllText(appXamlPath);

            if (xaml.Contains(MergeMarker, StringComparison.Ordinal))
                return;

            const string open = "<Application.Resources>";
            var index = xaml.IndexOf(open, StringComparison.Ordinal);

            if (index < 0)
                return;

            var merged =
                "\n            <ResourceDictionary>\n"
                + "                <ResourceDictionary.MergedDictionaries>\n"
                + $"                    <ResourceDictionary Source=\"{MergeMarker}\" />\n"
                + "                </ResourceDictionary.MergedDictionaries>\n"
                + "            </ResourceDictionary>";

            File.WriteAllText(appXamlPath, xaml.Insert(index + open.Length, merged));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void RepointStartupUri(string appXamlPath)
    {
        try
        {
            if (!File.Exists(appXamlPath))
                return;

            var xaml = File.ReadAllText(appXamlPath);
            if (!xaml.Contains(TemplateStartupUri))
                return;

            // Rewrite the template's own markup rather than regenerate it, so its
            // x:Class and xmlns declarations survive untouched.
            File.WriteAllText(appXamlPath, xaml.Replace(TemplateStartupUri, FolderedStartupUri));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Repoints <c>App.xaml</c>'s <c>x:Class</c> and <c>xmlns:local</c>, and its code-behind's
    /// <c>namespace</c>, at <paramref name="rootNamespace"/>. Best-effort and idempotent.
    /// </summary>
    /// <remarks>
    /// These two files come from <c>dotnet new wpf</c>, which named them after the project, and
    /// neither is deleted nor overwritten by <see cref="Apply"/>. With a namespace prefix the
    /// generated views declare a different namespace, and <c>x:Class</c> naming the old one does
    /// not compile.
    /// <para>
    /// Called on every scaffold rather than only at creation — see
    /// <see cref="MergeTheme"/> for the same reasoning — so it must work from whatever the files
    /// currently say. It matches the SHAPE (<c>x:Class="….App"</c>, <c>xmlns:local="clr-namespace:…"</c>,
    /// <c>namespace …;</c>) and never a remembered previous value, which a re-scaffold under a
    /// changed prefix would not have.
    /// </para>
    /// <para>
    /// The <c>clr-namespace</c> match is anchored to the <c>xmlns:local</c> attribute the template
    /// itself wrote, not to any bare <c>clr-namespace:</c> occurrence: a hand-added converter or
    /// resource xmlns (e.g. <c>xmlns:conv="clr-namespace:Shop.Converters"</c>) is a routine WPF
    /// addition between scaffolds, and this rewrite has no business touching it.
    /// </para>
    /// </remarks>
    public static void ReconcileNamespace(string projectFolder, string rootNamespace)
    {
        Rewrite(
            Path.Combine(projectFolder, "App.xaml"),
            xaml => Regex.Replace(
                Regex.Replace(
                    xaml,
                    "x:Class=\"[^\"]*\\.App\"",
                    $"x:Class=\"{rootNamespace}.App\""),
                "xmlns:local=\"clr-namespace:[^\"]*\"",
                $"xmlns:local=\"clr-namespace:{rootNamespace}\""));

        // Both namespace forms: net10's template emits a file-scoped declaration, but a block one
        // must not be corrupted if some template version emits it.
        Rewrite(
            Path.Combine(projectFolder, "App.xaml.cs"),
            code => Regex.Replace(
                code,
                @"^namespace\s+[^;{\s]+",
                $"namespace {rootNamespace}",
                RegexOptions.Multiline));
    }

    /// <summary>Best-effort read-transform-write. A file that cannot be read or written is left
    /// alone: the build error that follows is clearer than a crash here.</summary>
    private static void Rewrite(string path, Func<string, string> transform)
    {
        try
        {
            if (!File.Exists(path))
                return;

            var original = File.ReadAllText(path);
            var updated = transform(original);

            if (!string.Equals(original, updated, StringComparison.Ordinal))
                File.WriteAllText(path, updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
