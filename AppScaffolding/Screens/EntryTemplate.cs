namespace AppScaffolding.Screens;

/// <summary>
/// How a Business-layout entry form arranges a record's fields.
/// </summary>
/// <remarks>
/// <see cref="Grouped"/> is what this generator emitted before this choice existed, and every
/// caller that resolves <see cref="AppCustomization.EffectiveEntryTemplate"/> treats it as the
/// value that must reproduce that output byte for byte.
/// </remarks>
public enum EntryTemplate
{
    /// <summary>One flat column of fields, no grouping.</summary>
    Classic,

    /// <summary>Fields grouped into titled panels, packed three columns wide.</summary>
    Grouped,

    /// <summary>Fields grouped into titled panels, one panel per tab.</summary>
    Tabbed,

    /// <summary>Fields grouped into titled panels, packed two columns wide.</summary>
    TwoColumn,

    /// <summary>The record list and its editor shown together, the way the master screen already
    /// shows a master record — applied to transaction screens too.</summary>
    Split,

    /// <summary>The action bar as a row of slimmer, taller buttons.</summary>
    Ribbon
}
