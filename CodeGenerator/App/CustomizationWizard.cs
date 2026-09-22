using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Screens;

namespace CodeGenerator.App;

/// <summary>
/// The opt-in customization step: one gate, then nine prompts pre-filled from whatever the
/// caller already knows — ten on a target with a window, which is also asked its layout.
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

    /// <summary>The question that decides whether the rest are asked at all.</summary>
    internal const string GateTitle = "Customize appearance and features?";

    /// <summary>
    /// The customization to build with, or null if the user cancelled. Returns
    /// <paramref name="start"/> unchanged when they decline the gate — the gate decides whether to
    /// ASK, so declining keeps whatever the command line already said.
    /// </summary>
    /// <param name="choose">
    /// Asks a question with a fixed set of answers, which is how every real run asks these — see
    /// <see cref="CustomizationMenus"/>. Null falls back to the typed prompts below, which is what
    /// a console that cannot paint a list gets, and what the tests drive.
    /// </param>
    public static AppCustomization? Run(
        TextReader reader, TextWriter writer, AppCustomization start,
        ITargetScaffolder? target = null,
        Func<string, IReadOnlyList<string>, int, int?>? choose = null)
    {
        if (choose is not null)
            return CustomizationMenus.Run(reader, writer, start, target, choose);

        writer.WriteLine();
        writer.Write($"{GateTitle} [y/n]> ");

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

        //writer.WriteLine();

        // First, and only for a target that has a window: layout is the one structural choice in a
        // step that is otherwise decoration, and asking a console build for a window shape would be
        // nonsense.
        var layout = start.Layout;
        var asksLayout = target is not null && AppLayouts.Supports(target.Key);

        // The accepted values are listed here rather than inside each label: inline they make the
        // labels differ by thirty characters, and every input caret then starts in its own column.
        if (asksLayout)
            writer.WriteLine($"  Layout: {AppLayoutCatalog.PromptOptions}");

        writer.WriteLine("  Theme: light/dark      Accent: #rrggbb      Icon: .ico or .png");
        writer.WriteLine($"  Enter keeps the value in brackets; \"{NoneLabel}\" clears an optional one.");

        var entryTemplate = start.EntryTemplate;

        if (asksLayout)
        {
            var offered = AppLayoutCatalog.KeyFor(layout ?? AppCustomization.Default.EffectiveLayout);

            var chosen = Ask(reader, writer, "Layout", offered);
            if (chosen is null) return null;

            // An unusable answer keeps the current value rather than re-prompting, as a malformed
            // accent does. Enter on a run with no remembered layout leaves it UNNAMED rather than
            // recording the default: the review summary then stays quiet for a user who customized
            // nothing, and a layout typed against a remembered dashboard still reads as the
            // deliberate override it is.
            if (AppLayoutCatalog.TryResolve(chosen, out var parsed)
                && (layout is not null || parsed != AppCustomization.Default.EffectiveLayout))
            {
                layout = parsed;
            }
        }

        // Beside the layout question, and gated on the layout this build will actually use: asking
        // about field arrangement when the window will not be the business shell has no effect and
        // would falsely imply one. Gated on the RESOLVED layout, not the offered one, so an
        // unusable layout answer (which keeps the previously offered layout) still asks or skips
        // correctly. Write the options line here (before the blank line) only when it will be used.
        var asksEntryTemplate = asksLayout && target is not null && EntryTemplates.Supports(target.Key)
            && (layout ?? AppCustomization.Default.EffectiveLayout) == AppLayout.Business;

        if (asksEntryTemplate)
            writer.WriteLine($"  Entry template: {EntryTemplateCatalog.PromptOptions}");

        writer.WriteLine();

        if (asksEntryTemplate)
        {
            var offeredTemplate =
                EntryTemplateCatalog.KeyFor(entryTemplate ?? AppCustomization.Default.EffectiveEntryTemplate);

            var chosenTemplate = Ask(reader, writer, "Entry template", offeredTemplate);
            if (chosenTemplate is null) return null;

            if (EntryTemplateCatalog.TryResolve(chosenTemplate, out var parsedTemplate)
                && (entryTemplate is not null || parsedTemplate != AppCustomization.Default.EffectiveEntryTemplate))
            {
                entryTemplate = parsedTemplate;
            }
        }

        // Asked beside the layout, because both are about the SHAPE of the app rather than its
        // appearance. Blank means "decide automatically", which is what NoneLabel already
        // means for the optional values below.
        var transactions = Ask(
            reader, writer, "Transactions",
            start.Transactions.Length == 0 ? NoneLabel : start.Transactions);
        if (transactions is null) return null;

        var masters = Ask(
            reader, writer, "Masters",
            start.Masters.Length == 0 ? NoneLabel : start.Masters);
        if (masters is null) return null;

        var company = Ask(reader, writer, "Company name", start.Company);
        if (company is null) return null;

        var theme = Ask(reader, writer, "Theme", ThemeLabel(start.Theme));
        if (theme is null) return null;

        var accent = Ask(reader, writer, "Accent colour", start.Accent ?? NoneLabel);
        if (accent is null) return null;

        var icon = Ask(reader, writer, "Icon path", start.IconPath ?? NoneLabel);
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
        // above, but narrower: a rewritten window caption has a small blast radius, while adopting
        // a prefix moves every namespace in the app and rewrites both csprojs, App.xaml and the
        // validator stubs on the next regeneration. So this is only offered for a company typed
        // FRESH in this run, not one merely echoed back by a blank answer — an existing user with a
        // saved company and no prefix must not silently adopt one just by pressing Enter through
        // the wizard. Also only when nothing is set yet, and only when it reduces to something a
        // namespace may actually be called, so the run never refuses its own suggestion.
        var suggestedPrefix = start.NamespacePrefix.Length == 0
                               && company.Length > 0
                               && company != start.Company
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

        // Phrased as a positive so the remembered answer in app-profile.json reads as the
        // question does; the inline flag is the negative one.
        var login = AskYesNo(reader, writer, "Require a login", start.Login);
        if (login is null) return null;

        return start with
        {
            Layout = layout,
            EntryTemplate = entryTemplate,
            Company = company,
            Theme = ParseTheme(theme, start.Theme),
            Accent = ParseAccent(accent, start.Accent),
            IconPath = ParseOptional(icon, start.IconPath),
            TitlePattern = title.Length == 0 ? suggestedPattern : title,
            NamespacePrefix = ParsePrefix(prefix, suggestedPrefix),
            Validation = validation.Value,
            Logging = logging.Value,
            SampleSeedData = seed.Value,
            Login = login.Value,
            Transactions = ParseOptional(transactions, start.Transactions) ?? string.Empty,
            Masters = ParseOptional(masters, start.Masters) ?? string.Empty,
        };
    }

    /// <summary>The column the labels are padded to, and the one the value brackets are.</summary>
    /// <remarks>
    /// Both are padded so every prompt's caret lands in the same column: unpadded, a label and a
    /// remembered value together shift the point where the user types by up to thirty characters
    /// down the block. <see cref="LabelColumn"/> clears the longest label ("Namespace prefix");
    /// <see cref="ValueColumn"/> clears <c>[{company} - {app}]</c>, the title a named company is
    /// offered and so the widest value a default run shows. A longer one — a full icon path someone
    /// typed — pushes its own caret right, and only its own.
    /// </remarks>
    private const int LabelColumn = 18;
    private const int ValueColumn = 20;

    /// <summary>One prompt showing its current value. Blank keeps it; null means stdin closed.</summary>
    private static string? Ask(
        TextReader reader, TextWriter writer, string label, string current)
    {
        var value = $"[{current}]";

        writer.Write($"  {label,-LabelColumn}{value,-ValueColumn}> ");

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
    internal static string? ParseAccent(string value, string? current)
    {
        if (string.Equals(value, NoneLabel, StringComparison.OrdinalIgnoreCase))
            return null;

        return ThemePalette.IsValidHexColour(value) ? value : current;
    }

    internal static string? ParseOptional(string value, string? current)
        => string.Equals(value, NoneLabel, StringComparison.OrdinalIgnoreCase) ? null
           : value.Length == 0 ? current
           : value;

    /// <summary>The typed prefix, the offered one when the answer is unusable, or none.</summary>
    /// <remarks>
    /// An invalid answer keeps what was offered rather than re-prompting, as a malformed accent
    /// does — but unlike an accent, an invalid namespace could not be emitted at all, so this is
    /// the one prompt where keeping the previous value also protects the build.
    /// </remarks>
    internal static string ParsePrefix(string value, string suggested)
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
