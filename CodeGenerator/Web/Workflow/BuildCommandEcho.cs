using AppScaffolding;

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
/// the page's App type stage answers.
/// </para>
/// </remarks>
internal static class BuildCommandEcho
{
    /// <summary>The line, or lines, that repeat this run.</summary>
    public static IReadOnlyList<string> For(BuildSession session)
    {
        var line = $"build app: {session.Description.Trim()}";

        if (session.Source is { } source)
            line += $" from {source.Kind.ToString().ToLowerInvariant()} \"{source.Location}\"";

        line += Options(session.Customization);

        var lines = new List<string> { line };

        // `approve` is the review prompt's command, so it is only worth showing once a type has
        // actually been chosen — and it is how the console answers the App type stage.
        if (session.Target is not null)
            lines.Add($"approve {session.Target.Key}");

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

        if (customization.Company.Length > 0)
            options += $" company \"{customization.Company}\"";

        if (customization.NamespacePrefix.Length > 0)
            options += $" namespace {customization.NamespacePrefix}";

        if (customization.Theme != AppTheme.Default)
            options += $" theme {customization.Theme.ToString().ToLowerInvariant()}";

        if (customization.Accent is { Length: > 0 } accent)
            options += $" accent {accent}";

        if (customization.IconPath is { Length: > 0 } icon)
            options += $" icon {icon}";

        if (customization.Layout is { } layout)
            options += $" layout {LayoutWord(layout)}";

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
    /// The word the parser accepts, which is not always the enum's own name —
    /// <c>MasterDetail</c> is typed <c>master-detail</c>.
    /// </summary>
    private static string LayoutWord(AppLayout layout)
        => layout == AppLayout.MasterDetail ? "master-detail" : layout.ToString().ToLowerInvariant();
}
