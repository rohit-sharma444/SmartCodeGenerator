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
    {
        // Shared with WinFormsTarget. The WPF variant of RelayCommand requeries through
        // CommandManager, which is what re-enables Delete when the grid selection changes.
        var files = DesktopViewModelFiles.Build(
            projectName, entityNames, ViewModelTemplates.RelayCommand(projectName));

        files.Add(new("Views", "MainWindow.xaml", MainWindowTemplate.Xaml(projectName)));
        files.Add(new("Views", "MainWindow.xaml.cs", MainWindowTemplate.CodeBehind(projectName)));

        return files;
    }

    public void FixupProject(string outputFolder) => WpfProjectFixup.Apply(outputFolder);
}
