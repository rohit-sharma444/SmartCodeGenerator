namespace AppScaffolding;

/// <summary>
/// The layout vocabulary: the CLI keys and their descriptions, in display order.
/// </summary>
/// <remarks>
/// One catalogue rather than one per target, because the WPF and Windows Forms targets ship the
/// same four layouts under the same four names — so the inline parser, the wizard prompt and the
/// help catalogue all read from here and no surface keeps its own list.
/// </remarks>
public static class AppLayoutCatalog
{
    private static readonly (AppLayout Layout, string Key, string Description)[] All =
    [
        (AppLayout.Tabs, "tabs", "a tab per entity, its grid filling the tab"),
        (AppLayout.Sidebar, "sidebar", "a list of entities on the left, the selected one's grid beside it"),
        // Hyphenated deliberately: the inline parser tokenizes on whitespace.
        (AppLayout.MasterDetail, "master-detail", "tabs, with an edit form for the selected row under the grid"),
        (AppLayout.Dashboard, "dashboard", "a home screen of per-entity cards showing row counts"),
        (AppLayout.Business, "business",
            "a dashboard home, a rail grouped into masters and transactions, and an editor for every record"),
    ];

    /// <summary>Layout keys in display order; the first is the default.</summary>
    public static IReadOnlyList<string> Keys { get; } = All.Select(x => x.Key).ToList();

    public static IReadOnlyDictionary<AppLayout, string> Descriptions { get; } =
        All.ToDictionary(x => x.Layout, x => x.Description);

    /// <summary>The prompt/help rendering of the vocabulary: "tabs/sidebar/master-detail/dashboard".</summary>
    public static string PromptOptions { get; } = string.Join("/", Keys);

    /// <summary>The pipe rendering the help catalogue uses: "tabs|sidebar|master-detail|dashboard".</summary>
    public static string HelpOptions { get; } = string.Join("|", Keys);

    public static string KeyFor(AppLayout layout) => All.First(x => x.Layout == layout).Key;

    /// <summary>Resolves a key (case-insensitively) to a layout.</summary>
    public static bool TryResolve(string key, out AppLayout layout)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                layout = candidate.Layout;
                return true;
            }
        }

        layout = AppLayout.Tabs;
        return false;
    }
}
