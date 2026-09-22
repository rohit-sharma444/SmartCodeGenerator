namespace AppScaffolding.Screens;

/// <summary>
/// The entry-template vocabulary: the CLI keys and their descriptions, in display order.
/// </summary>
/// <remarks>
/// One catalogue, the same shape as <see cref="AppLayoutCatalog"/>: the inline parser, the wizard
/// prompt and the help catalogue all read from here and no surface keeps its own list.
/// </remarks>
public static class EntryTemplateCatalog
{
    private static readonly (EntryTemplate Template, string Key, string Description)[] All =
    [
        (EntryTemplate.Classic, "classic", "one flat column of fields, no grouping"),
        (EntryTemplate.Grouped, "grouped", "fields grouped into titled panels, packed three columns wide"),
        (EntryTemplate.Tabbed, "tabbed", "fields grouped into titled panels, one panel per tab"),
        // Hyphenated deliberately: the inline parser tokenizes on whitespace.
        (EntryTemplate.TwoColumn, "two-column", "fields grouped into titled panels, packed two columns wide"),
        (EntryTemplate.Split, "split",
            "the record list and its editor shown together, as the master screen already does"),
        (EntryTemplate.Ribbon, "ribbon", "the action bar as an icon command strip instead of plain buttons"),
    ];

    /// <summary>Template keys in display order. Classic is first because it is the simplest shape,
    /// not because it is the default: <see cref="AppCustomization.EffectiveEntryTemplate"/> answers
    /// "grouped" for a build that names nothing.</summary>
    public static IReadOnlyList<string> Keys { get; } = All.Select(x => x.Key).ToList();

    public static IReadOnlyDictionary<EntryTemplate, string> Descriptions { get; } =
        All.ToDictionary(x => x.Template, x => x.Description);

    /// <summary>The prompt/help rendering: "classic/grouped/tabbed/two-column/split/ribbon".</summary>
    public static string PromptOptions { get; } = string.Join("/", Keys);

    /// <summary>The pipe rendering the help catalogue uses.</summary>
    public static string HelpOptions { get; } = string.Join("|", Keys);

    public static string KeyFor(EntryTemplate template) => All.First(x => x.Template == template).Key;

    /// <summary>Resolves a key (case-insensitively) to a template.</summary>
    public static bool TryResolve(string key, out EntryTemplate template)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                template = candidate.Template;
                return true;
            }
        }

        template = EntryTemplate.Grouped;
        return false;
    }
}
