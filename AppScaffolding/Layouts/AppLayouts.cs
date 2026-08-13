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

    public static bool Supports(string targetKey)
        => LayoutTargets.Contains(targetKey, StringComparer.OrdinalIgnoreCase);
}
