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
/// <param name="Login">
/// Whether the application gates itself behind a login screen. True by default, unlike every other
/// setting here — an application with screens is expected to have accounts, and this is the one
/// place the record is deliberately NOT neutral. `nologin` turns it off, and that is the only
/// combination under which a named layout still reproduces the generator's pre-auth output byte
/// for byte.
/// </param>
/// <param name="Transactions">
/// Entity names the user declared to be transactions, comma-joined, overruling the heuristic.
/// A string rather than a collection because this record is compared by value across the suite
/// and a list member would break that silently.
/// </param>
/// <param name="Masters">As <paramref name="Transactions"/>, in the other direction. A name
/// given in both wins as a master, which is the safe answer.</param>
public sealed record AppCustomization(
    string Company,
    AppTheme Theme,
    string? Accent,
    string TitlePattern,
    string? IconPath,
    bool Validation,
    bool Logging,
    bool SampleSeedData,
    bool Login,
    string Transactions,
    string Masters,
    string NamespacePrefix,
    AppLayout? Layout)
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
        SampleSeedData: true,
        Login: true,
        Transactions: string.Empty,
        Masters: string.Empty,
        NamespacePrefix: string.Empty,
        Layout: null);

    /// <summary>The project folder a supplied icon is copied into.</summary>
    public const string AssetsFolder = "Assets";

    /// <summary>Whether anything company-derived should be emitted at all.</summary>
    public bool HasBranding => Company.Length > 0;

    /// <summary>
    /// The layout to emit: whatever was named, or <see cref="AppLayout.Tabs"/> when nothing was.
    /// </summary>
    /// <remarks>
    /// <see cref="Layout"/> is nullable because "not mentioned" and "tabs" are different answers
    /// to <c>BuildAppCommand.Merge</c>, which reads a setting as typed-inline when it differs from
    /// <see cref="Default"/>. The emit path never cares about that distinction, and uses this.
    /// </remarks>
    public AppLayout EffectiveLayout => Layout ?? AppLayout.Tabs;

    /// <summary>
    /// The name the supplied icon is copied to inside <see cref="AssetsFolder"/>, or null when
    /// there is no usable icon.
    /// </summary>
    /// <remarks>
    /// The stem is always "app" and only the extension comes from the user, so no emitted project
    /// file, XAML attribute or string literal ever carries a file name someone typed — a name
    /// with a quote or an ampersand in it would otherwise need escaping in three places.
    /// <para>
    /// An extensionless path yields null: it cannot be an <c>ApplicationIcon</c>, and neither
    /// front end has a way to name it.
    /// </para>
    /// </remarks>
    public string? IconFileName
    {
        get
        {
            if (IconPath is null)
                return null;

            var extension = Path.GetExtension(IconPath).ToLowerInvariant();

            return extension.Length == 0 ? null : "app" + extension;
        }
    }

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

    /// <summary>The title as a C# string-literal body — a quote or backslash in a company name
    /// would otherwise break out of the literal it is emitted into.</summary>
    /// <remarks>
    /// Backslash is escaped before quote, so the backslash that escaping a quote inserts is
    /// never itself doubled by a later backslash pass. A raw newline or carriage return is
    /// escaped to the two-character sequence <c>\n</c> / <c>\r</c> last, rather than stripped, so
    /// the title survives intact — left raw, either would land inside the literal as an actual
    /// source-level line break, which is CS1010, "Newline in constant".
    /// </remarks>
    public string TitleLiteralFor(string appName)
        => TitleFor(appName)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");

    /// <summary>The title as an XML attribute value.</summary>
    public string TitleAttributeFor(string appName)
        => TitleFor(appName)
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>
    /// The namespace root emitted code declares: the project name, under the prefix when there is
    /// one.
    /// </summary>
    /// <remarks>
    /// This is the whole mechanism. No template uses its name argument for anything but a
    /// namespace root, so passing this where the project name is passed today puts every emitted
    /// file under the prefix — while the project name goes on naming the folder, the csproj, the
    /// assembly and the SQLite environment override.
    /// </remarks>
    public string RootNamespaceFor(string projectName)
        => NamespacePrefix.Length == 0 ? projectName : $"{NamespacePrefix}.{projectName}";
}
