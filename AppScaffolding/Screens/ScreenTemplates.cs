namespace AppScaffolding.Screens;

/// <summary>The screen types a generated application declares for itself.</summary>
/// <remarks>
/// The generator has its own <see cref="EntityRole"/>, used to decide what to emit. This emits the
/// application's copy, in the application's own namespace: a generated project references nothing
/// of this generator, so a shared enum is not available to it.
/// </remarks>
public static class ScreenTemplates
{
    /// <summary>Source of <c>Screens/EntityRole.cs</c> in the generated app.</summary>
    public static string RoleEnum(string rootNamespace) => $$"""
namespace {{rootNamespace}}.Screens;

/// <summary>What kind of screen an entity gets.</summary>
public enum EntityRole
{
    /// <summary>Reference data — a list and an editor.</summary>
    Master,

    /// <summary>A record that references reference data — a filtered list, totals, and an editor
    /// that may hold lines.</summary>
    Transaction,

    /// <summary>A line belonging to a transaction. Edited inside its parent, never on a screen of
    /// its own.</summary>
    DocumentLine
}
""";
}
