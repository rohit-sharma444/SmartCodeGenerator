namespace AppScaffolding.Screens;

/// <summary>
/// Which targets the entry-template choice applies to.
/// </summary>
/// <remarks>
/// Phase 1 ships WPF's six templates only. Windows Forms and Blazor keep today's single rendering
/// until their own phases land, and this is the one place that decides whether the prompt, menu,
/// inline option and web dropdown are even reachable for a given target — so a build for a target
/// this phase does not support can never set an entry template its renderer does not implement.
/// </remarks>
public static class EntryTemplates
{
    private static readonly string[] SupportedTargetKeys = ["wpf"];

    public static bool Supports(string targetKey)
        => SupportedTargetKeys.Contains(targetKey, StringComparer.OrdinalIgnoreCase);
}
