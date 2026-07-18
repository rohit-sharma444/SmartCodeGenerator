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

    public IReadOnlyList<GeneratedFile> FrontEndFiles(string projectName, IReadOnlyList<string> entityNames) => new List<GeneratedFile>
    {
        // RelationshipResolver depends on IEntityViewModel, so it is WPF-only, not shared core.
        new("Data", "RelationshipResolver.cs", RelationshipResolverTemplate.Source(projectName)),
        new("ViewModels", "ObservableObject.cs", ViewModelTemplates.ObservableObject(projectName)),
        new("ViewModels", "RelayCommand.cs", ViewModelTemplates.RelayCommand(projectName)),
        new("ViewModels", "IEntityViewModel.cs", ViewModelTemplates.EntityViewModelInterface(projectName)),
        new("ViewModels", "EntityViewModel.cs", ViewModelTemplates.EntityViewModel(projectName)),
        new("ViewModels", "MainViewModel.cs", MainViewModelTemplate.Build(projectName, entityNames)),
        new("Views", "MainWindow.xaml", MainWindowTemplate.Xaml(projectName)),
        new("Views", "MainWindow.xaml.cs", MainWindowTemplate.CodeBehind(projectName)),
    };

    public void FixupProject(string outputFolder) => WpfProjectFixup.Apply(outputFolder);
}
