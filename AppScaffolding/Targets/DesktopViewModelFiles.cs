namespace AppScaffolding.Targets;

/// <summary>
/// The view-model files both desktop targets emit. Nothing here is WPF-specific: these types use
/// only <c>System.ComponentModel</c> and <c>System.Windows.Input.ICommand</c>, which every .NET
/// app has, and <c>RelationshipResolver</c> is pure reflection over <c>IEntityViewModel</c>.
/// </summary>
/// <remarks>
/// <c>relayCommandSource</c> is the one thing that genuinely differs: WPF's
/// <c>RelayCommand</c> requeries through <c>CommandManager</c> (PresentationCore), WinForms' does
/// not. Both emit the same class into the same file, so <c>EntityViewModel</c> is shared unchanged.
/// </remarks>
public static class DesktopViewModelFiles
{
    /// <summary>A fresh mutable list, so a target can append its own views to it. With
    /// <paramref name="validation"/> false the view models resolve no validator, because the
    /// Validation folder they would resolve it from is not generated either.</summary>
    public static List<GeneratedFile> Build(
        string rootNamespace, IReadOnlyList<string> entityNames, string relayCommandSource,
        bool validation = true, bool seed = true) =>
    [
        new("Data", "RelationshipResolver.cs", RelationshipResolverTemplate.Source(rootNamespace)),
        new("ViewModels", "ObservableObject.cs", ViewModelTemplates.ObservableObject(rootNamespace)),
        new("ViewModels", "RelayCommand.cs", relayCommandSource),
        new("ViewModels", "IEntityViewModel.cs", ViewModelTemplates.EntityViewModelInterface(rootNamespace)),
        new("ViewModels", "EntityViewModel.cs",
            ViewModelTemplates.EntityViewModel(rootNamespace, validation)),
        new("ViewModels", "MainViewModel.cs",
            MainViewModelTemplate.Build(rootNamespace, entityNames, seed)),
    ];
}
