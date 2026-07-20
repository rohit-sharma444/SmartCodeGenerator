namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// The user-facing strings for a background store update — kept pure and separate from the console
/// so they can be tested without a console. Renderers add their own prefix (title vs. log line).
/// </summary>
public static class StoreUpdateText
{
    public static string Updating(int percent) => $"Wikipedia store updating {percent}%";

    public static string Final(StartupUpdateOutcome outcome, int daysApplied) => outcome switch
    {
        StartupUpdateOutcome.Completed when daysApplied == 0
            => "Wikipedia store: up to date.",
        StartupUpdateOutcome.Completed
            => $"Wikipedia store: up to date ({daysApplied} day(s) applied).",
        StartupUpdateOutcome.Skipped
            => $"Wikipedia store: update stopped; {daysApplied} day(s) applied and kept.",
        StartupUpdateOutcome.Failed
            => $"Wikipedia store: update failed; {daysApplied} day(s) applied and kept.",
        _ => "Wikipedia store: up to date.",
    };
}
