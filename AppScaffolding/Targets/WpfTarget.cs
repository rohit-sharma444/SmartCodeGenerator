using AppScaffolding.Auth;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.Wpf;
using CodeGenerationEngine.Models;

namespace AppScaffolding.Targets;

/// <summary>The original output target: a runnable WPF CRUD app. Its file set and the
/// WpfProjectFixup are exactly what AppScaffolder produced before targets existed.</summary>
public sealed class WpfTarget : ITargetScaffolder
{
    public string Key => "wpf";
    public string Description => "a runnable WPF CRUD app";
    public bool HasScreens => true;

    public DotNetProjectType ProjectType => DotNetProjectType.Wpf;
    public string FolderSuffix => "";

    public IReadOnlyList<GeneratedFile> FrontEndFiles(string projectName, IReadOnlyList<string> entityNames)
        => FrontEndFiles(projectName, entityNames, AppCustomization.Default);

    public IReadOnlyList<GeneratedFile> FrontEndFiles(
        string projectName, IReadOnlyList<string> entityNames, AppCustomization customization)
    {
        // The namespace the emitted code declares; projectName still names the app in its title.
        var ns = customization.RootNamespaceFor(projectName);

        // Shared with WinFormsTarget. The WPF variant of RelayCommand requeries through
        // CommandManager, which is what re-enables Delete when the grid selection changes.
        var files = DesktopViewModelFiles.Build(
            ns, entityNames, ViewModelTemplates.RelayCommand(ns),
            customization.Validation, customization.SampleSeedData, customization.Login);

        var palette = ThemePalette.Resolve(customization.Theme, customization.Accent);

        // Theming stays out here rather than inside the layouts: it is orthogonal to shape, and
        // four layouts would otherwise each reimplement the merge into App.xaml.
        files.AddRange(
            WpfLayouts.Resolve(customization.EffectiveLayout).ViewFiles(
                new LayoutContext(ns, projectName, customization, palette)));

        if (palette is not null)
            files.Add(new("Views", ThemeXamlTemplate.FileName, ThemeXamlTemplate.Source(palette)));

        // App.xaml.cs is emitted, replacing the template's: the sign-in has to happen before
        // any window opens, and StartupUri opens one before OnStartup finishes. The matching
        // StartupUri removal is WpfProjectFixup.RemoveStartupUri, called by the scaffolder.
        if (customization.Login)
        {
            var login = new LayoutContext(ns, projectName, customization, palette);

            files.Add(new("Views", "LoginWindow.xaml", WpfLoginTemplate.Xaml(login)));
            files.Add(new("Views", "LoginWindow.xaml.cs", WpfLoginTemplate.CodeBehind(ns)));
            files.Add(new("", "App.xaml.cs", WpfLoginTemplate.AppCodeBehind(ns)));
        }

        return files;
    }

    public void FixupProject(string outputFolder) => WpfProjectFixup.Apply(outputFolder);
}
