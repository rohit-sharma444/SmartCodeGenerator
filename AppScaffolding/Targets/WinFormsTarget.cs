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
    public DotNetProjectType ProjectType => DotNetProjectType.WinForms;
    public string FolderSuffix => ".WinForms";

    public IReadOnlyList<GeneratedFile> FrontEndFiles(
        string projectName, IReadOnlyList<string> entityNames)
        => FrontEndFiles(projectName, entityNames, AppCustomization.Default);

    public IReadOnlyList<GeneratedFile> FrontEndFiles(
        string projectName, IReadOnlyList<string> entityNames, AppCustomization customization)
    {
        // The namespace the emitted code declares; projectName still names the app in its title.
        var ns = customization.RootNamespaceFor(projectName);

        var files = DesktopViewModelFiles.Build(
            ns, entityNames, ViewModelTemplates.PortableRelayCommand(ns),
            customization.Validation, customization.SampleSeedData);

        var palette = ThemePalette.Resolve(customization.Theme, customization.Accent);

        files.AddRange(
            WinFormsLayouts.Resolve(customization.EffectiveLayout).ViewFiles(
                new LayoutContext(ns, projectName, customization, palette)));

        files.Add(new("", "Program.cs", WinFormsProgramTemplate.Source(ns)));

        return files;
    }

    public void FixupProject(string outputFolder) => WinFormsProjectFixup.Apply(outputFolder);
}
