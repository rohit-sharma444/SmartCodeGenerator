namespace AppScaffolding;

/// <summary>The colours a themed front end paints with, as `#rrggbb` literals.</summary>
public sealed record ThemePalette(
    string Background,
    string Surface,
    string Foreground,
    string GridLine,
    string Accent,
    string AccentForeground)
{
    /// <summary>The accent used when a theme was chosen but no accent was.</summary>
    public const string DefaultAccent = "#2563EB";

    private static readonly ThemePalette Light = new(
        Background: "#FFFFFF",
        Surface: "#F3F4F6",
        Foreground: "#111827",
        GridLine: "#E5E7EB",
        Accent: DefaultAccent,
        AccentForeground: "#FFFFFF");

    private static readonly ThemePalette Dark = new(
        Background: "#1F2937",
        Surface: "#111827",
        Foreground: "#F9FAFB",
        GridLine: "#374151",
        Accent: DefaultAccent,
        AccentForeground: "#FFFFFF");

    /// <summary>
    /// The palette for <paramref name="theme"/> and <paramref name="accent"/>, or null when
    /// nothing was customized and the generator should emit no theme.
    /// </summary>
    /// <remarks>
    /// An accent with no theme is meaningful — "keep the normal look, but in our colour" — and
    /// resolves over the light base. An invalid accent is ignored rather than rejected, so a
    /// typo cannot fail a build the user has already answered ten questions for.
    /// </remarks>
    public static ThemePalette? Resolve(AppTheme theme, string? accent)
    {
        var usable = accent is not null && IsValidHexColour(accent) ? accent : null;

        if (theme == AppTheme.Default && usable is null)
            return null;

        var basePalette = theme == AppTheme.Dark ? Dark : Light;

        return usable is null ? basePalette : basePalette with { Accent = usable };
    }

    /// <summary>Whether <paramref name="value"/> is exactly `#rrggbb`.</summary>
    public static bool IsValidHexColour(string value)
        => value.Length == 7
           && value[0] == '#'
           && value[1..].All(Uri.IsHexDigit);
}
