namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>How a startup update ended.</summary>
public enum StartupUpdateOutcome
{
    /// <summary>The store was current, or Wikimedia never answered. Either way, nothing ran.</summary>
    NothingToDo,

    /// <summary>Every pending day was applied.</summary>
    Completed,

    /// <summary>The user interrupted it. The days that landed are kept.</summary>
    Skipped,

    /// <summary>It broke — the network, the disk, the server. The days that landed are kept.</summary>
    Failed,
}

/// <summary>What the startup update managed to do before it stopped.</summary>
public sealed record StartupUpdateResult(
    int DaysApplied,
    StartupUpdateOutcome Outcome,
    string? Failure = null);

/// <summary>
/// Runs the pending daily increments while the app starts, and — whatever happens — lets it start.
/// </summary>
/// <remarks>
/// <b>Nothing here may prevent the application from running.</b> This work is worth hours and
/// gigabytes, and the user asked for a code generator, not a download. So an interruption is not an
/// error (they pressed Ctrl-C; they want their prompt), and a failure is not fatal (Wikimedia hung up;
/// the store is merely as stale as it already was). Both end this and hand control back.
/// <para>
/// What makes that safe is that <see cref="IncrementalUpdater"/> commits a day at a time and moves the
/// store's date only once a day has FULLY landed. So stopping in the middle costs the day in progress
/// and nothing else: the days already applied are kept, and the next run resumes at the one that was
/// interrupted.
/// </para>
/// </remarks>
public sealed class StartupDumpUpdate
{
    /// <summary>Brings the store forward by one day. Must honour the token, and must be resumable.</summary>
    public delegate void ApplyDay(int day, CancellationToken cancellation);

    public StartupUpdateResult Run(
        DumpStaleness staleness, ApplyDay apply, CancellationToken cancellation)
    {
        // IsStale, not "PendingDays.Count > 0": a check Wikimedia never answered knows nothing, and
        // must not be mistaken for either a clean bill of health or a reason to start downloading.
        if (!staleness.IsStale)
            return new StartupUpdateResult(0, StartupUpdateOutcome.NothingToDo);

        var applied = 0;

        foreach (var day in staleness.PendingDays)
        {
            try
            {
                apply(day, cancellation);
                applied++;
            }
            catch (OperationCanceledException)
            {
                return new StartupUpdateResult(applied, StartupUpdateOutcome.Skipped);
            }
            catch (Exception failure)
            {
                return new StartupUpdateResult(applied, StartupUpdateOutcome.Failed, failure.Message);
            }
        }

        return new StartupUpdateResult(applied, StartupUpdateOutcome.Completed);
    }
}
