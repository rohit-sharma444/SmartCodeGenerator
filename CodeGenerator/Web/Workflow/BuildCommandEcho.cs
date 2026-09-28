using AppScaffolding;
using DataIngestion;

namespace CodeGenerator.Web.Workflow;

/// <summary>
/// The `build app` line that would produce what the page is holding.
/// </summary>
/// <remarks>
/// Eight answered questions and no way to repeat them: a cancelled run, or a second project like
/// the first, meant clicking the whole interview again, and nothing the workflow produced could go
/// into a script. This is the same run as a line of text.
/// <para>
/// Written to be pasted, so quoting follows the parsers rather than taste: a Windows path is
/// quoted because <c>InlineDataSource</c> treats everything after the format as the location, and
/// the option words go last because <c>InlineCustomization</c> lifts them off the end. The tests
/// hold this honest by parsing what it writes and comparing the answers back — a line that reads
/// well and parses differently is worse than no line at all.
/// </para>
/// <para>
/// The application type is deliberately a second line. The `build app:` grammar has no type on it:
/// the console chooses one at the review prompt with `approve &lt;target&gt;`, which is exactly what
/// the page's Application Type stage answers.
/// </para>
/// </remarks>
internal static class BuildCommandEcho
{
    /// <summary>The line, or lines, that repeat this run.</summary>
    public static IReadOnlyList<string> For(BuildSession session)
    {
        var line = $"build app: {session.Description.Trim()}";

        if (session.Source is { } source)
            // Redacted, because for SQL the location IS a connection string and this line is
            // both shown on the review page and printed to the console when the run ends.
        {
            line += $" from {source.Kind.ToString().ToLowerInvariant()} "
                  + $"\"{ConnectionSecret.Redact(source.Location)}\"";

            // The tables that were ticked. Left out, a pasted line read EVERY table - a different
            // model. Straight after the location, because `tables` is a trailing keyword of it.
            if (source.SqlTables is { Count: > 0 } tables)
            {
                var names = string.Join(",", tables);

                line += names.Any(char.IsWhiteSpace) ? $" tables \"{names}\"" : $" tables {names}";
            }

            // Outermost of the location's trailing keywords, as InlineDataSource reads them. The
            // console has no way to tick some findings and not others, so any finding applied is
            // said as `normalize` - the nearer of the two lines that can be typed.
            if (session.NormalizeAll || session.Normalized.Count > 0)
                line += $" {InlineDataSource.NormalizeKeyword}";
        }

        line += Options(session.Customization);

        var lines = new List<string> { line };

        // `approve` is the review prompt's command, so it is only worth showing once a type has
        // actually been chosen — and it is how the console answers the Application Type stage.
        // With the persistence it was generated with: `approve <type> sqlite` is how the console
        // says it, and leaving it off replayed a SQLite app as JSON.
        if (session.Target is not null)
            lines.Add(session.Generated?.Persistence == PersistenceKind.Sqlite
                ? $"approve {session.Target.Key} sqlite"
                : $"approve {session.Target.Key}");

        return lines;
    }

    /// <summary>
    /// Only what differs from the default, in the order the parser strips them.
    /// </summary>
    /// <remarks>
    /// Echoing every option would produce a line nobody would type and bury the two that matter.
    /// A default answered explicitly is not a decision the user made.
    /// </remarks>
    private static string Options(AppCustomization customization)
    {
        var options = string.Empty;

        // First, which is LAST to be applied: the parser reads right to left, and a company
        // switches the title onto the company pattern unless a title is already there - so a
        // title put back to "{app}" came back as the company pattern. Written whenever it differs
        // from what the line would otherwise give, which with a company is the company pattern.
        var impliedTitle = customization.Company.Length > 0
            ? AppCustomization.CompanyTitlePattern
            : AppCustomization.AppPlaceholder;

        if (customization.TitlePattern != impliedTitle)
            options += $" title {Quoted(customization.TitlePattern)}";

        if (customization.Company.Length > 0)
            options += $" company {Quoted(customization.Company)}";

        if (customization.NamespacePrefix.Length > 0)
            options += $" namespace {customization.NamespacePrefix}";

        if (customization.Theme != AppTheme.Default)
            options += $" theme {customization.Theme.ToString().ToLowerInvariant()}";

        if (customization.Accent is { Length: > 0 } accent)
            options += $" accent {accent}";

        // Quoted: a path with a space in it otherwise left all but its last word in the
        // description.
        if (customization.IconPath is { Length: > 0 } icon)
            options += $" icon {Quoted(icon)}";

        if (customization.Layout is { } layout)
            options += $" layout {LayoutWord(layout)}";

        if (customization.EntryTemplate is { } entryTemplate)
            options += $" entry-template {AppScaffolding.Screens.EntryTemplateCatalog.KeyFor(entryTemplate)}";

        // Quoted for the same reason: "Order, Invoice" is two words.
        if (customization.Transactions.Length > 0)
            options += $" transactions {Quoted(customization.Transactions)}";

        if (customization.Masters.Length > 0)
            options += $" masters {Quoted(customization.Masters)}";

        if (!customization.Validation)
            options += " novalidation";

        if (customization.Logging)
            options += " logging";

        if (!customization.SampleSeedData)
            options += " noseed";

        if (!customization.Login)
            options += " nologin";

        return options;
    }

    /// <summary>
    /// <paramref name="value"/> as one quoted token, a quote inside it doubled — the escape
    /// <c>InlineCustomization</c> reads back. A company with a quote in it broke the line before.
    /// </summary>
    private static string Quoted(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    /// <summary>
    /// The word the parser accepts, which is not always the enum's own name —
    /// <c>MasterDetail</c> is typed <c>master-detail</c>.
    /// </summary>
    private static string LayoutWord(AppLayout layout)
        => layout == AppLayout.MasterDetail ? "master-detail" : layout.ToString().ToLowerInvariant();
}
