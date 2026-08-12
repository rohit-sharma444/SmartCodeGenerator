namespace AppScaffolding;

/// <summary>
/// Emits <c>Views/Theme.xaml</c>: the palette as a ResourceDictionary of brushes.
/// </summary>
/// <remarks>
/// A separate dictionary rather than brushes written into MainWindow.xaml, so that window stays
/// the static, entity-agnostic file it has always been and an unthemed app keeps it byte for
/// byte. <c>WpfProjectFixup</c> merges this into App.xaml, which it already rewrites.
/// </remarks>
public static class ThemeXamlTemplate
{
    public const string FileName = "Theme.xaml";

    public static string Source(ThemePalette palette) => $"""
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="AppBackgroundBrush" Color="{palette.Background}" />
    <SolidColorBrush x:Key="AppSurfaceBrush" Color="{palette.Surface}" />
    <SolidColorBrush x:Key="AppForegroundBrush" Color="{palette.Foreground}" />
    <SolidColorBrush x:Key="AppGridLineBrush" Color="{palette.GridLine}" />
    <SolidColorBrush x:Key="AppAccentBrush" Color="{palette.Accent}" />
    <SolidColorBrush x:Key="AppAccentForegroundBrush" Color="{palette.AccentForeground}" />
</ResourceDictionary>
""";
}
