namespace AppScaffolding.Screens;

/// <summary>What kind of screen an entity gets.</summary>
/// <remarks>
/// <see cref="Master"/> is the safe default and the value anything unrecognised takes: a
/// misclassified master renders as the ordinary grid this generator has always produced, whereas a
/// misclassified <see cref="DocumentLine"/> hides a screen entirely.
/// </remarks>
public enum EntityRole
{
    /// <summary>Reference data — a list and an editor.</summary>
    Master,

    /// <summary>A record that references reference data — a filtered list, totals, and an editor
    /// that may hold lines.</summary>
    Transaction,

    /// <summary>A line belonging to a transaction. Gets no screen of its own; it is edited inside
    /// its parent's editor.</summary>
    DocumentLine
}
