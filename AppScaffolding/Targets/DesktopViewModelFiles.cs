namespace AppScaffolding.Targets;

/// <summary>
/// The view-model files both desktop targets emit. Nothing here is WPF-specific: these types use
/// only <c>System.ComponentModel</c> and <c>System.Windows.Input.ICommand</c>, which every .NET
/// app has, and <c>RelationshipResolver</c> is pure reflection over <c>IEntityViewModel</c>.
/// </summary>
/// <remarks>
/// <paramref name="relayCommandSource"/> is the one thing that genuinely differs: WPF's
/// <c>RelayCommand</c> requeries through <c>CommandManager</c> (PresentationCore), WinForms' does
/// not. Both emit the same class into the same file, so <c>EntityViewModel</c> is shared unchanged.
/// </remarks>
public static class DesktopViewModelFiles
{
    /// <summary>A fresh mutable list, so a target can append its own views to it.</summary>
    public static List<GeneratedFile> Build(
        string projectName, IReadOnlyList<string> entityNames, string relayCommandSource) =>
    [
        new("Data", "RelationshipResolver.cs", RelationshipResolverTemplate.Source(projectName)),
        new("ViewModels", "ObservableObject.cs", ViewModelTemplates.ObservableObject(projectName)),
        new("ViewModels", "RelayCommand.cs", relayCommandSource),
        new("ViewModels", "IEntityViewModel.cs", ViewModelTemplates.EntityViewModelInterface(projectName)),
        new("ViewModels", "EntityViewModel.cs", ViewModelTemplates.EntityViewModel(projectName)),
        new("ViewModels", "MainViewModel.cs", MainViewModelTemplate.Build(projectName, entityNames)),
    ];
}
