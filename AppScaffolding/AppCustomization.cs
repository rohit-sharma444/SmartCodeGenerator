namespace AppScaffolding;

/// <summary>Which theme a generated desktop front end uses. <see cref="Default"/> means the
/// generator emits no theme at all, which is what every pre-customization app looked like.</summary>
public enum AppTheme
{
    Default,
    Light,
    Dark
}

/// <summary>
/// What a `build app` run asked the generated application to look like and contain.
/// </summary>
/// <remarks>
/// <see cref="Default"/> is deliberately neutral: it must reproduce the output the generator
/// produced before customization existed, because it is what every pre-existing caller passes.
/// The regression proof is the existing suite — if an old test has to change, this record is
/// not neutral.
/// </remarks>
public sealed record AppCustomization(
    string Company,
    AppTheme Theme,
    string? Accent,
    string TitlePattern,
    string? IconPath,
    bool Validation,
    bool Logging,
    bool SampleSeedData)
{
    /// <summary>The only two placeholders a title pattern may use.</summary>
    public const string CompanyPlaceholder = "{company}";
    public const string AppPlaceholder = "{app}";

    /// <summary>The title a customized app usually wants once a company is named. Plain hyphen,
    /// not an en dash: emitted source and XAML then carry no encoding dependency.</summary>
    public const string CompanyTitlePattern = "{company} - {app}";

    public static AppCustomization Default { get; } = new(
        Company: string.Empty,
        Theme: AppTheme.Default,
        Accent: null,
        TitlePattern: AppPlaceholder,
        IconPath: null,
        Validation: true,
        Logging: false,
        SampleSeedData: true);

    /// <summary>Whether anything company-derived should be emitted at all.</summary>
    public bool HasBranding => Company.Length > 0;

    /// <summary>
    /// The window/page title for <paramref name="appName"/>.
    /// </summary>
    /// <remarks>
    /// Substituting an empty company would leave the pattern's separator behind — "{company} -
    /// {app}" reading " - Shop". The collapse happens at the PATTERN level, before
    /// substitution: when there is no company, the "{company}" placeholder and the run of
    /// separator characters adjoining it are cut out of the pattern first. This is deliberately
    /// narrower than trimming the final result, which cannot distinguish a dangling separator
    /// from punctuation the caller typed on purpose — a pattern of "{app}:" must keep its colon
    /// whether or not a company is set.
    /// </remarks>
    public string TitleFor(string appName)
    {
        var pattern = Company.Length == 0 ? CollapseCompanyPlaceholder(TitlePattern) : TitlePattern;

        var title = pattern
            .Replace(CompanyPlaceholder, Company)
            .Replace(AppPlaceholder, appName);

        return title.Trim();
    }

    /// <summary>
    /// Cuts the "{company}" placeholder out of <paramref name="pattern"/> along with the
    /// separator run adjoining it, or returns <paramref name="pattern"/> unchanged when it has
    /// no company placeholder.
    /// </summary>
    /// <remarks>
    /// The run that follows the placeholder is the one that collapses — "{company} - {app}"
    /// loses " - {app}"'s leading punctuation, not "{app}"'s. But when the placeholder sits at
    /// the end of the pattern with nothing after it, there is no following run to take, so the
    /// preceding one collapses instead — "{app} - {company}" loses "{app}"'s trailing " - ".
    /// </remarks>
    private static string CollapseCompanyPlaceholder(string pattern)
    {
        var index = pattern.IndexOf(CompanyPlaceholder, StringComparison.Ordinal);
        if (index < 0)
            return pattern;

        var end = index + CompanyPlaceholder.Length;

        if (end < pattern.Length)
        {
            var runEnd = end;
            while (runEnd < pattern.Length && IsSeparatorChar(pattern[runEnd]))
                runEnd++;

            return pattern[..index] + pattern[runEnd..];
        }

        var runStart = index;
        while (runStart > 0 && IsSeparatorChar(pattern[runStart - 1]))
            runStart--;

        return pattern[..runStart];
    }

    /// <summary>A dangling-separator character: whitespace, or one of the punctuation marks a
    /// title pattern uses to join "{company}" and "{app}".</summary>
    private static bool IsSeparatorChar(char c)
        => char.IsWhiteSpace(c) || c is '-' or ':' or '|';
}
