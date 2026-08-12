using CodeGenerationEngine.Models;

namespace AppScaffolding.Targets;

/// <summary>The original output target: a runnable WPF CRUD app. Its file set and the
/// WpfProjectFixup are exactly what AppScaffolder produced before targets existed.</summary>
public sealed class WpfTarget : ITargetScaffolder
{
    public string Key => "wpf";
    public string Description => "a runnable WPF CRUD app";
    public DotNetProjectType ProjectType => DotNetProjectType.Wpf;
    public string FolderSuffix => "";

    public IReadOnlyList<GeneratedFile> FrontEndFiles(string projectName, IReadOnlyList<string> entityNames)
        => FrontEndFiles(projectName, entityNames, AppCustomization.Default);

    public IReadOnlyList<GeneratedFile> FrontEndFiles(
        string projectName, IReadOnlyList<string> entityNames, AppCustomization customization)
    {
        // Shared with WinFormsTarget. The WPF variant of RelayCommand requeries through
        // CommandManager, which is what re-enables Delete when the grid selection changes.
        var files = DesktopViewModelFiles.Build(
            projectName, entityNames, ViewModelTemplates.RelayCommand(projectName),
            customization.Validation);

        var palette = ThemePalette.Resolve(customization.Theme, customization.Accent);

        files.Add(new("Views", "MainWindow.xaml",
            MainWindowTemplate.Xaml(
                projectName, customization.TitleAttributeFor(projectName), palette is not null,
                customization.IconFileName)));
        files.Add(new("Views", "MainWindow.xaml.cs", MainWindowTemplate.CodeBehind(projectName)));

        if (palette is not null)
            files.Add(new("Views", ThemeXamlTemplate.FileName, ThemeXamlTemplate.Source(palette)));

        return files;
    }

    public void FixupProject(string outputFolder) => WpfProjectFixup.Apply(outputFolder);
}
