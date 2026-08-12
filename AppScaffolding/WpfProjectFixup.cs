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
}
