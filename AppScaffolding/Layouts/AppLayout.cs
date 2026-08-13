namespace AppScaffolding;

/// <summary>
/// The shape of a generated desktop front end.
/// </summary>
/// <remarks>
/// <see cref="Tabs"/> is what this generator emitted before layouts existed, and every layout
/// mechanism treats it as the value that must reproduce that output byte for byte.
/// </remarks>
public enum AppLayout
{
    /// <summary>A tab per entity, its grid filling the tab.</summary>
    Tabs,

    /// <summary>A navigation list of entities, the selected one's grid beside it.</summary>
    Sidebar,

    /// <summary>Tab chrome, each pane split into a grid above and an edit form below.</summary>
    MasterDetail,

    /// <summary>A home screen of per-entity cards showing row counts, clicking through to a grid.</summary>
    Dashboard
}
