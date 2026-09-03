using AppScaffolding.Auth;
using AppScaffolding.Screens;

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
    /// <param name="roles">
    /// Supplied only for the business shell, whose screens differ per entity. Null everywhere
    /// else, and the four older layouts then emit exactly what they always did.
    /// </param>
    public static List<GeneratedFile> Build(
        string rootNamespace, IReadOnlyList<string> entityNames, string relayCommandSource,
        bool validation = true, bool seed = true, bool login = false,
        EntityRoleMap? roles = null)
    {
        var files = new List<GeneratedFile>
        {
            new("Screens", "EntityRole.cs", ScreenTemplates.RoleEnum(rootNamespace)),
            new("Data", "RelationshipResolver.cs", RelationshipResolverTemplate.Source(rootNamespace)),
            new("ViewModels", "ObservableObject.cs", ViewModelTemplates.ObservableObject(rootNamespace)),
            new("ViewModels", "RelayCommand.cs", relayCommandSource),
            new("ViewModels", "IEntityViewModel.cs", ViewModelTemplates.EntityViewModelInterface(rootNamespace)),
            new("ViewModels", "EntityViewModel.cs",
                ViewModelTemplates.EntityViewModel(rootNamespace, validation)),
            new("ViewModels", "MainViewModel.cs",
                MainViewModelTemplate.Build(rootNamespace, entityNames, seed, login, roles)),
        };

        // The business shell's screens. Emitted only for it: the four older layouts bind to
        // EntityViewModel and would carry five files nothing references.
        if (roles is not null)
        {
            // Beside EntityRole rather than among the view models: it is the same kind of thing --
            // what the model is, rather than how a window shows it -- and the web target emits it
            // into the same folder for the same screens.
            files.Add(new("Screens", "LineSet.cs", LineSetTemplate.Source(rootNamespace)));

            // Beside LineSet for the same reason: what the model is, rather than how a window
            // shows it. Both desktop layouts reflect over it; nothing else does.
            files.Add(new("Screens", "FieldGroups.cs", FieldGroupsTemplate.Source(rootNamespace)));

            files.Add(new("ViewModels", "RecordEditorViewModel.cs",
                RecordEditorTemplate.Source(rootNamespace, validation)));
            files.Add(new("ViewModels", "MasterScreenViewModel.cs",
                MasterScreenTemplate.Source(rootNamespace)));
            files.Add(new("ViewModels", "TransactionScreenViewModel.cs",
                TransactionScreenTemplate.Source(rootNamespace)));
            files.Add(new("ViewModels", "DashboardViewModel.cs",
                ShellTemplate.Dashboard(rootNamespace)));
            files.Add(new("ViewModels", "ShellViewModel.cs",
                ShellTemplate.Shell(rootNamespace)));
        }

        // Desktop-only, and emitted beside the view models rather than by AuthCoreFiles: a
        // static signed-in user is right for one process serving one person and wrong for a
        // server serving many.
        if (login)
        {
            files.Add(new("Auth", "Session.cs", SessionTemplate.Source(rootNamespace)));
            files.Add(new("ViewModels", "LoginViewModel.cs",
                LoginViewModelTemplate.Source(rootNamespace)));
            files.Add(new("ViewModels", "UsersViewModel.cs",
                UsersViewModelTemplate.Source(rootNamespace)));
        }

        return files;
    }
}
