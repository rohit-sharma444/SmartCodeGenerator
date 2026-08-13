namespace AppScaffolding.Layouts;

/// <summary>
/// Everything a layout needs to emit its views.
/// </summary>
/// <remarks>
/// A record rather than four parameters because all four derive from the same two inputs — the
/// project name and the customization — and <see cref="Palette"/> in particular would otherwise be
/// re-resolved inside every layout. <see cref="RootNamespace"/> and <see cref="ProjectName"/> are
/// both present and are NOT interchangeable: emitted code declares the root namespace (which may
/// carry a company prefix), while the window title names the project.
/// </remarks>
public sealed record LayoutContext(
    string RootNamespace,
    string ProjectName,
    AppCustomization Customization,
    ThemePalette? Palette)
{
    /// <summary>Whether the layout should bind to the theme brushes.</summary>
    public bool Themed => Palette is not null;

    /// <summary>The window/form caption, XML-escaped.</summary>
    public string TitleAttribute => Customization.TitleAttributeFor(ProjectName);

    /// <summary>The icon file inside the assets folder, or null when the app was given none.</summary>
    public string? IconFileName => Customization.IconFileName;
}
