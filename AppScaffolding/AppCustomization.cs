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
    /// {app}" reading " - Shop" — so leading and trailing separator characters are trimmed off
    /// the result rather than the pattern being rejected.
    /// </remarks>
    public string TitleFor(string appName)
    {
        var title = TitlePattern
            .Replace(CompanyPlaceholder, Company)
            .Replace(AppPlaceholder, appName);

        return title.Trim().Trim('-', ':', '|').Trim();
    }
}
