namespace AppScaffolding.Layouts;

/// <summary>
/// One shape of a generated desktop front end. A target holds a registry of these and asks the one
/// the customization named for its view files.
/// </summary>
/// <remarks>
/// Deliberately shaped like <see cref="Targets.ITargetScaffolder"/>: a stable lower-case
/// <see cref="Key"/>, a human <see cref="Description"/>, and one method producing files. The
/// shared view-model files are emitted by the target before this runs, and no layout may change
/// them — a layout is a view concern only.
/// </remarks>
public interface IAppLayout
{
    /// <summary>Stable lower-case key, matching <see cref="AppLayoutCatalog"/>.</summary>
    string Key { get; }

    /// <summary>Human phrase for the wizard prompt and the help catalogue.</summary>
    string Description { get; }

    /// <summary>The view files this layout emits, folder-relative to the project root.</summary>
    IReadOnlyList<GeneratedFile> ViewFiles(LayoutContext context);
}
