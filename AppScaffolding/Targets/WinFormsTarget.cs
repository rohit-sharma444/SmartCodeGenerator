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
        var files = DesktopViewModelFiles.Build(
            projectName, entityNames, ViewModelTemplates.PortableRelayCommand(projectName));

        var palette = ThemePalette.Resolve(customization.Theme, customization.Accent);

        files.Add(new("Views", "MainForm.cs",
            WinFormsMainFormTemplate.Source(
                projectName, customization.TitleLiteralFor(projectName), palette,
                customization.IconFileName)));
        files.Add(new("", "Program.cs", WinFormsProgramTemplate.Source(projectName)));

        return files;
    }

    public void FixupProject(string outputFolder) => WinFormsProjectFixup.Apply(outputFolder);
}
