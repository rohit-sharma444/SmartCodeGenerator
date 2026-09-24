using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Screens;

namespace CodeGenerator.App;

/// <summary>
/// The customization step asked as drop-downs: one list per question, with the value the user
/// already has pre-selected.
/// </summary>
/// <remarks>
/// Same questions, same order and same parsing as the typed prompts in
/// <see cref="CustomizationWizard"/> — only the asking differs, which is why the parse rules are
/// shared rather than restated. Arrowing straight through must leave a remembered profile exactly
/// as it was, so every question pre-selects what it already holds.
/// <para>
/// The four questions whose answers are values rather than choices — a company, a colour, a path,
/// a namespace — offer keep / clear / type instead. Nothing becomes untypeable; the typing just
/// moves behind a row, so a user who is keeping what they have never touches the keyboard.
/// </para>
/// <para>
/// The chooser arrives as a delegate for the reason the application-type menu is one: it is the
/// step that needs raw key input, which is exactly what a test cannot supply.
/// </para>
/// </remarks>
internal static class CustomizationMenus
{
    /// <summary>How a value question offers to clear itself.</summary>
    private const string NoneRow = "None";

    /// <summary>What a value question was answered with.</summary>
    private enum ValueChoice
    {
        /// <summary>The row offering what is already there.</summary>
        Kept,

        /// <summary>Cleared.</summary>
        None,

        /// <summary>Typed at the prompt behind the last row.</summary>
        Typed
    }

    public static AppCustomization? Run(
        TextReader reader, TextWriter writer, AppCustomization start,
        ITargetScaffolder? target, Func<string, IReadOnlyList<string>, int, int?> choose)
    {
        // Declining is the pre-selected row: this step is optional decoration on a build the user
        // has already answered several questions for, which is what the typed prompt's blank line
        // meant too.
        var gate = choose(CustomizationWizard.GateTitle, ["Yes", "No"], 1);

        if (gate is null)
            return null;

        if (gate != 0)
            return start;

        var layout = start.Layout;

        // Only for a target that has a window: layout is the one structural choice in a step that
        // is otherwise decoration, and asking a console build for a window shape would be nonsense.
        if (target is not null && AppLayouts.Supports(target.Key))
        {
            var chosen = AskLayout(choose, layout);

            if (chosen is null)
                return null;

            // Taking the pre-selected row on a run with no remembered layout leaves it UNNAMED
            // rather than recording the default: the review summary then stays quiet for a user who
            // customized nothing, and a layout named against a remembered dashboard still reads as
            // the override it is.
            if (layout is not null || chosen != AppCustomization.Default.EffectiveLayout)
                layout = chosen;
        }

        var entryTemplate = start.EntryTemplate;

        if (target is not null && EntryTemplates.Supports(target.Key)
            && (layout ?? AppCustomization.Default.EffectiveLayout) == AppLayout.Business)
        {
            var chosenTemplate = AskEntryTemplate(choose, entryTemplate);

            if (chosenTemplate is null)
                return null;

            if (entryTemplate is not null || chosenTemplate != AppCustomization.Default.EffectiveEntryTemplate)
                entryTemplate = chosenTemplate;
        }

        // Beside the layout question: both are about the SHAPE of the app rather than its
        // appearance. "None" means decide automatically, which is what it means for every
        // other optional value here.
        var transactions = AskValue(
            reader, writer, choose, "Transactions", start.Transactions, "Keep", "Type names");

        if (transactions is null)
            return null;

        var masters = AskValue(
            reader, writer, choose, "Masters", start.Masters, "Keep", "Type names");

        if (masters is null)
            return null;

        var company = AskValue(
            reader, writer, choose, "Company name", start.Company, "Keep", "Type a name");

        if (company is null)
            return null;

        var companyName = company.Value.Choice switch
        {
            ValueChoice.Kept => start.Company,
            ValueChoice.None => string.Empty,
            _ => company.Value.Text,
        };

        var theme = choose("Theme", ["None", "Light", "Dark"], (int)start.Theme);

        if (theme is null)
            return null;

        var accent = AskValue(
            reader, writer, choose, "Accent colour", start.Accent, "Keep", "Type a colour");

        if (accent is null)
            return null;

        var icon = AskValue(reader, writer, choose, "Icon", start.IconPath, "Keep", "Type a path");

        if (icon is null)
            return null;

        // Naming a company but leaving the pattern at "{app}" would hide the company just typed, so
        // the company pattern is what a named company is offered.
        var suggestedPattern =
            companyName.Length > 0 && start.TitlePattern == AppCustomization.AppPlaceholder
                ? AppCustomization.CompanyTitlePattern
                : start.TitlePattern;

        var title = AskTitle(reader, writer, choose, suggestedPattern);

        if (title is null)
            return null;

        // A company just named is the likeliest prefix — the same courtesy as the title pattern,
        // but narrower: a rewritten caption has a small blast radius, while adopting a prefix moves
        // every namespace in the app and rewrites both csprojs, App.xaml and the validator stubs on
        // the next regeneration. So it is offered only for a company typed FRESH in this run, never
        // one merely kept, and only when nothing is set yet.
        var suggestedPrefix = start.NamespacePrefix.Length == 0
                              && companyName.Length > 0
                              && companyName != start.Company
            ? NamespacePrefix.FromCompany(companyName) ?? string.Empty
            : start.NamespacePrefix;

        var prefix = AskValue(
            reader, writer, choose, "Namespace prefix", suggestedPrefix,
            start.NamespacePrefix.Length == 0 ? "Use" : "Keep", "Type a prefix");

        if (prefix is null)
            return null;

        var validation = AskYesNo(choose, "Validation layer", start.Validation);

        if (validation is null)
            return null;

        var logging = AskYesNo(choose, "Logging", start.Logging);

        if (logging is null)
            return null;

        var seed = AskYesNo(choose, "Sample seed data", start.SampleSeedData);

        if (seed is null)
            return null;

        var login = AskYesNo(choose, "Require a login", start.Login);

        if (login is null)
            return null;

        return start with
        {
            Layout = layout,
            EntryTemplate = entryTemplate,
            Company = companyName,
            Theme = (AppTheme)theme.Value,
            Accent = accent.Value.Choice switch
            {
                ValueChoice.Kept => start.Accent,
                ValueChoice.None => null,

                // The same rule the typed prompt applies: an unusable colour keeps the one in
                // hand rather than trapping the user in a re-prompt over a mistyped answer.
                _ => CustomizationWizard.ParseAccent(accent.Value.Text, start.Accent),
            },
            IconPath = icon.Value.Choice switch
            {
                ValueChoice.Kept => start.IconPath,
                ValueChoice.None => null,
                _ => CustomizationWizard.ParseOptional(icon.Value.Text, start.IconPath),
            },
            TitlePattern = title,
            NamespacePrefix = prefix.Value.Choice switch
            {
                ValueChoice.Kept => suggestedPrefix,
                ValueChoice.None => string.Empty,

                // An invalid namespace could not be emitted at all, so this is the one place where
                // keeping the offered value also protects the build.
                _ => CustomizationWizard.ParsePrefix(prefix.Value.Text, suggestedPrefix),
            },
            Validation = validation.Value,
            Logging = logging.Value,
            SampleSeedData = seed.Value,
            Transactions = transactions.Value.Choice switch
            {
                ValueChoice.Kept => start.Transactions,
                ValueChoice.None => string.Empty,
                _ => transactions.Value.Text,
            },
            Masters = masters.Value.Choice switch
            {
                ValueChoice.Kept => start.Masters,
                ValueChoice.None => string.Empty,
                _ => masters.Value.Text,
            },
            Login = login.Value,
        };
    }

    /// <summary>The layout chosen, or null to cancel.</summary>
    private static AppLayout? AskLayout(
        Func<string, IReadOnlyList<string>, int, int?> choose, AppLayout? current)
    {
        var keys = AppLayoutCatalog.Keys;

        var labels = keys
            .Select(key =>
            {
                AppLayoutCatalog.TryResolve(key, out var layout);

                return $"{key} — {AppLayoutCatalog.Descriptions[layout]}";
            })
            .ToList();

        var offered = AppLayoutCatalog.KeyFor(current ?? AppCustomization.Default.EffectiveLayout);

        var row = choose("Layout", labels, IndexOf(keys, offered));

        if (row is null)
            return null;

        AppLayoutCatalog.TryResolve(keys[row.Value], out var chosen);

        return chosen;
    }

    /// <summary>The entry template chosen, or null to cancel.</summary>
    private static EntryTemplate? AskEntryTemplate(
        Func<string, IReadOnlyList<string>, int, int?> choose, EntryTemplate? current)
    {
        var keys = EntryTemplateCatalog.Keys;

        var labels = keys
            .Select(key =>
            {
                EntryTemplateCatalog.TryResolve(key, out var template);

                return $"{key} — {EntryTemplateCatalog.Descriptions[template]}";
            })
            .ToList();

        var offered = EntryTemplateCatalog.KeyFor(current ?? AppCustomization.Default.EffectiveEntryTemplate);

        var row = choose("Entry template", labels, IndexOf(keys, offered));

        if (row is null)
            return null;

        EntryTemplateCatalog.TryResolve(keys[row.Value], out var chosen);

        return chosen;
    }

    /// <summary>The title pattern chosen, or null to cancel.</summary>
    /// <remarks>
    /// The two patterns worth a row are the two the tool itself uses; anything else a user has is
    /// offered as what it is — the one they already have — and a pattern of their own is a row
    /// away.
    /// </remarks>
    private static string? AskTitle(
        TextReader reader, TextWriter writer,
        Func<string, IReadOnlyList<string>, int, int?> choose, string suggested)
    {
        List<string> patterns =
            [AppCustomization.AppPlaceholder, AppCustomization.CompanyTitlePattern];

        if (!patterns.Contains(suggested))
            patterns.Add(suggested);

        var labels = patterns.Select(TitleLabel).ToList();
        labels.Add("Type a pattern");

        var row = choose("Window title", labels, patterns.IndexOf(suggested));

        if (row is null)
            return null;

        if (row < patterns.Count)
            return patterns[row.Value];

        var typed = AskTyped(reader, writer, "Window title");

        // A blank answer at the prompt behind the row is not a title: it keeps what was offered,
        // exactly as a blank answer to the typed prompt always did.
        return typed is null ? null : typed.Length == 0 ? suggested : typed;
    }

    private static string TitleLabel(string pattern)
        => pattern == AppCustomization.AppPlaceholder
            ? $"Application name only  ({AppCustomization.AppPlaceholder})"
            : pattern == AppCustomization.CompanyTitlePattern
                ? $"Company and application  ({AppCustomization.CompanyTitlePattern})"
                : $"Keep {pattern}";

    /// <summary>
    /// A question whose answer is a value: keep what is there, clear it, or type one.
    /// </summary>
    /// <param name="offered">
    /// What the keep row holds. Empty means there is nothing to keep and the row is not shown.
    /// </param>
    /// <param name="keepVerb">
    /// "Keep" for a value the user already has, "Use" for one this run worked out for them — the
    /// difference matters at the namespace prefix, where the offered name was derived just now.
    /// </param>
    private static (ValueChoice Choice, string Text)? AskValue(
        TextReader reader, TextWriter writer,
        Func<string, IReadOnlyList<string>, int, int?> choose,
        string title, string? offered, string keepVerb, string typeRow)
    {
        var keeps = !string.IsNullOrEmpty(offered);

        var labels = new List<string>();

        if (keeps)
            labels.Add($"{keepVerb} {offered}");

        labels.Add(NoneRow);
        labels.Add(typeRow);

        var row = choose(title, labels, 0);

        if (row is null)
            return null;

        if (keeps && row == 0)
            return (ValueChoice.Kept, offered!);

        if (row == labels.Count - 1)
        {
            var typed = AskTyped(reader, writer, title);

            // EOF is the absence of an answer, not an empty one: conflating them would build an
            // app off a closed pipe.
            return typed is null ? null : (ValueChoice.Typed, typed);
        }

        return (ValueChoice.None, string.Empty);
    }

    private static bool? AskYesNo(
        Func<string, IReadOnlyList<string>, int, int?> choose, string title, bool current)
    {
        var row = choose(title, ["Yes", "No"], current ? 0 : 1);

        return row is null ? null : row == 0;
    }

    /// <summary>What was typed at the prompt behind a "type one" row, or null if input closed.</summary>
    private static string? AskTyped(TextReader reader, TextWriter writer, string title)
    {
        writer.Write($"  {title}: ");

        return reader.ReadLine()?.Trim();
    }

    /// <summary>The row holding <paramref name="value"/>, or the first.</summary>
    private static int IndexOf(IReadOnlyList<string> values, string value)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }
}
