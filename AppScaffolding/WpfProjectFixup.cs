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
