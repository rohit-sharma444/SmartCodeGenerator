using AppScaffolding;

namespace CodeGenerator.App;

/// <summary>
/// The opt-in customization step: one gate, then nine prompts pre-filled from whatever the
/// caller already knows.
/// </summary>
/// <remarks>
/// Reads and writes only through the supplied reader and writer, never the console — the pattern
/// <c>BuildAppWizard</c> and <c>DataSourceInterview</c> established, and what makes the whole
/// flow drivable from a test.
/// <para>
/// An unparseable answer keeps the current value rather than re-prompting. Re-prompting would
/// be the stricter choice, but this step is optional decoration on a build the user has already
/// answered several questions for, and trapping them in a loop over a mistyped colour is worse
/// than ignoring it.
/// </para>
/// </remarks>
internal static class CustomizationWizard
{
    /// <summary>How a prompt shows, and clears, a value it has none of.</summary>
    private const string NoneLabel = "none";

    /// <summary>
    /// The customization to build with, or null if the user cancelled. Returns
    /// <paramref name="start"/> unchanged when they decline the gate — the gate decides whether to
    /// ASK, so declining keeps whatever the command line already said.
    /// </summary>
    public static AppCustomization? Run(
        TextReader reader, TextWriter writer, AppCustomization start)
    {
        writer.WriteLine();
        writer.Write("Customize appearance and features? [y/N]> ");

        var gate = reader.ReadLine();

        // EOF is the absence of an answer, not a "no": conflating them would generate an app
        // off a closed pipe.
        if (gate is null)
            return null;

        var answer = gate.Trim();

        if (IsCancel(answer))
            return null;

        if (!IsYes(answer))
            return start;

        writer.WriteLine();

        var company = Ask(reader, writer, "Company name", start.Company);
        if (company is null) return null;

        var theme = Ask(reader, writer, "Theme [light/dark]", ThemeLabel(start.Theme));
        if (theme is null) return null;

        var accent = Ask(reader, writer, "Accent colour (#rrggbb)", start.Accent ?? NoneLabel);
        if (accent is null) return null;

        var icon = Ask(reader, writer, "Icon path (.ico or .png)", start.IconPath ?? NoneLabel);
        if (icon is null) return null;

        // Naming a company but leaving the pattern at "{app}" would hide the company the user
        // just typed, so the company pattern is what a named company is offered.
        var suggestedPattern =
            company.Length > 0 && start.TitlePattern == AppCustomization.AppPlaceholder
                ? AppCustomization.CompanyTitlePattern
                : start.TitlePattern;

        var title = Ask(reader, writer, "Window title", suggestedPattern);
        if (title is null) return null;

        // A company just named is the likeliest prefix — the same courtesy as the title pattern
        // above. Only offered when nothing is set yet, and only when it reduces to something a
        // namespace may actually be called, so the run never refuses its own suggestion.
        var suggestedPrefix = start.NamespacePrefix.Length == 0 && company.Length > 0
            ? NamespacePrefix.FromCompany(company) ?? string.Empty
            : start.NamespacePrefix;

        var prefix = Ask(
            reader, writer, "Namespace prefix",
            suggestedPrefix.Length == 0 ? NoneLabel : suggestedPrefix);

        if (prefix is null) return null;

        var validation = AskYesNo(reader, writer, "Validation layer", start.Validation);
        if (validation is null) return null;

        var logging = AskYesNo(reader, writer, "Logging", start.Logging);
        if (logging is null) return null;

        var seed = AskYesNo(reader, writer, "Sample seed data", start.SampleSeedData);
        if (seed is null) return null;

        return start with
        {
            Company = company,
            Theme = ParseTheme(theme, start.Theme),
            Accent = ParseAccent(accent, start.Accent),
            IconPath = ParseOptional(icon, start.IconPath),
            TitlePattern = title.Length == 0 ? suggestedPattern : title,
            NamespacePrefix = ParsePrefix(prefix, suggestedPrefix),
            Validation = validation.Value,
            Logging = logging.Value,
            SampleSeedData = seed.Value,
        };
    }

    /// <summary>One prompt showing its current value. Blank keeps it; null means stdin closed.</summary>
    private static string? Ask(
        TextReader reader, TextWriter writer, string label, string current)
    {
        writer.Write($"  {label} [{current}]> ");

        var line = reader.ReadLine();

        if (line is null)
            return null;

        var text = line.Trim();

        return text.Length == 0 ? current : text;
    }

    /// <summary>A yes/no prompt. Null means stdin closed; an unrecognized answer keeps
    /// <paramref name="current"/>.</summary>
    private static bool? AskYesNo(
        TextReader reader, TextWriter writer, string label, bool current)
    {
        var answer = Ask(reader, writer, label, current ? "yes" : "no");

        if (answer is null)
            return null;

        if (IsYes(answer)) return true;
        if (IsNo(answer)) return false;

        return current;
    }

    private static string ThemeLabel(AppTheme theme)
        => theme == AppTheme.Default ? NoneLabel : theme.ToString().ToLowerInvariant();

    /// <summary>The named theme, or the current one when the answer names none. "none" is not a
    /// theme name, so it lands here too and clears nothing — <see cref="AppTheme.Default"/> is
    /// already "no theme".</summary>
    private static AppTheme ParseTheme(string value, AppTheme current)
        => Enum.TryParse<AppTheme>(value, ignoreCase: true, out var parsed) ? parsed : current;

    /// <summary>The typed accent, or the current one when it is unusable. "none" clears it.</summary>
    private static string? ParseAccent(string value, string? current)
    {
        if (string.Equals(value, NoneLabel, StringComparison.OrdinalIgnoreCase))
            return null;

        return ThemePalette.IsValidHexColour(value) ? value : current;
    }

    private static string? ParseOptional(string value, string? current)
        => string.Equals(value, NoneLabel, StringComparison.OrdinalIgnoreCase) ? null
           : value.Length == 0 ? current
           : value;

    /// <summary>The typed prefix, the offered one when the answer is unusable, or none.</summary>
    /// <remarks>
    /// An invalid answer keeps what was offered rather than re-prompting, as a malformed accent
    /// does — but unlike an accent, an invalid namespace could not be emitted at all, so this is
    /// the one prompt where keeping the previous value also protects the build.
    /// </remarks>
    private static string ParsePrefix(string value, string suggested)
    {
        if (string.Equals(value, NoneLabel, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return NamespacePrefix.IsValid(value) ? value : suggested;
    }

    private static bool IsYes(string text)
        => string.Equals(text, "y", StringComparison.OrdinalIgnoreCase)
           || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase);

    private static bool IsNo(string text)
        => string.Equals(text, "n", StringComparison.OrdinalIgnoreCase)
           || string.Equals(text, "no", StringComparison.OrdinalIgnoreCase);

    private static bool IsCancel(string text)
        => string.Equals(text, "c", StringComparison.OrdinalIgnoreCase)
           || string.Equals(text, "cancel", StringComparison.OrdinalIgnoreCase);
}
