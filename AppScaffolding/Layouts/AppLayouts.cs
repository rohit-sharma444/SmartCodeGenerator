namespace AppScaffolding.Layouts;

/// <summary>Which output targets have a window a layout can shape.</summary>
/// <remarks>
/// Lives here rather than in the CLI so the wizard and the `approve` check read one list. A key
/// list rather than a member on <see cref="Targets.ITargetScaffolder"/>, following the
/// <c>BuildAppCommand.JsonOnlyTargets</c> precedent — and deliberately its own list even though it
/// currently holds the same two keys, because "reads JSON only" and "has a window" are different
/// facts that could diverge.
/// </remarks>
public static class AppLayouts
{
    private static readonly string[] LayoutTargets = ["wpf", "winforms"];

    /// <summary>The targets whose screens are built from the entity roles whatever layout is
    /// named, because the business shell is the only shape they have.</summary>
    private static readonly string[] AlwaysRoleScreens = ["blazor"];

    public static bool Supports(string targetKey)
        => LayoutTargets.Contains(targetKey, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="targetKey"/> under <paramref name="layout"/> actually builds its
    /// screens from the master / transaction split.
    /// </summary>
    /// <remarks>
    /// The classifier runs for every build, so a summary of what it decided can be printed for a
    /// target that has no screens and for a layout that ignores the roles — and it was. Only the
    /// business shell reads them on the desktop; the web app is always that shell; and console,
    /// webapi, worker and classlib have no screen to put a role on at all.
    /// </remarks>
    public static bool UsesRoleScreens(string targetKey, AppLayout layout)
        => AlwaysRoleScreens.Contains(targetKey, StringComparer.OrdinalIgnoreCase)
           || (Supports(targetKey) && layout == AppLayout.Business);
}
