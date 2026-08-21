using AppScaffolding.Auth;
using AppScaffolding.Layouts;
using AppScaffolding.Layouts.WinForms;
using CodeGenerationEngine.Models;

namespace AppScaffolding.Targets;

/// <summary>A runnable Windows Forms CRUD app over the discovered model.</summary>
/// <remarks>
/// Shares its whole presentation layer with <see cref="WpfTarget"/> — see
/// <see cref="DesktopViewModelFiles"/>. Only the view and the RelayCommand variant differ.
/// </remarks>
public sealed class WinFormsTarget : ITargetScaffolder
{
    public string Key => "winforms";
    public string Description => "a runnable Windows Forms CRUD app";
    public bool HasScreens => true;

    public DotNetProjectType ProjectType => DotNetProjectType.WinForms;
    public string FolderSuffix => ".WinForms";

    public IReadOnlyList<GeneratedFile> FrontEndFiles(
        string projectName, IReadOnlyList<string> entityNames)
        => FrontEndFiles(projectName, entityNames, AppCustomization.Default);

    /// <summary>
    /// The business shell needs the entities' roles, which names alone cannot carry, so this
    /// target implements the context overload rather than taking the default.
    /// </summary>
    public IReadOnlyList<GeneratedFile> FrontEndFiles(FrontEndContext context)
        => FrontEndFiles(
            context.ProjectName, context.EntityNames, context.Customization, context.Roles);

    public IReadOnlyList<GeneratedFile> FrontEndFiles(
        string projectName, IReadOnlyList<string> entityNames, AppCustomization customization)
        => FrontEndFiles(projectName, entityNames, customization, roles: null);

    private IReadOnlyList<GeneratedFile> FrontEndFiles(
        string projectName, IReadOnlyList<string> entityNames, AppCustomization customization,
        Screens.EntityRoleMap? roles)
    {
        // Roles reach the view models only for the business shell; the four older layouts
        // bind to EntityViewModel and must go on emitting exactly what they always did.
        var screenRoles = customization.EffectiveLayout == AppLayout.Business
            ? roles ?? Screens.EntityRoleMap.Empty
            : null;

        // The namespace the emitted code declares; projectName still names the app in its title.
        var ns = customization.RootNamespaceFor(projectName);

        var files = DesktopViewModelFiles.Build(
            ns, entityNames, ViewModelTemplates.PortableRelayCommand(ns),
            customization.Validation, customization.SampleSeedData, customization.Login,
            screenRoles);

        var palette = ThemePalette.Resolve(customization.Theme, customization.Accent);

        files.AddRange(
            WinFormsLayouts.Resolve(customization.EffectiveLayout).ViewFiles(
                new LayoutContext(ns, projectName, customization, palette)));

        if (customization.Login)
        {
            files.Add(new("Views", "LoginForm.cs",
                WinFormsLoginTemplate.Source(
                    new LayoutContext(ns, projectName, customization, palette))));
        }

        files.Add(new("", "Program.cs", WinFormsProgramTemplate.Source(ns, customization.Login)));

        return files;
    }

    public void FixupProject(string outputFolder) => WinFormsProjectFixup.Apply(outputFolder);
}
