namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// Drives the daily-increment apply through an <see cref="IStoreUpdateProgress"/>, so the caller
/// decides how progress is shown and whether it blocks anything. Whatever happens — offline,
/// interrupted, failed — the caller keeps running.
/// </summary>
public sealed class BackgroundStoreUpdate
{
    /// <summary>
    /// Applies the pending days, driving <paramref name="progress"/> as it goes, and returns how it
    /// ended. The day loop is <see cref="StartupDumpUpdate"/>'s, so the resume guarantees hold: only
    /// a day that fully lands moves the store's date.
    /// </summary>
    public StartupUpdateResult Run(
        DumpStaleness staleness, IStoreUpdateProgress progress,
        StartupDumpUpdate.ApplyDay apply, CancellationToken cancellation)
    {
        var count = staleness.PendingDays.Count;
        var index = 0;

        var result = new StartupDumpUpdate().Run(
            staleness,
            (day, token) =>
            {
                progress.BeginDay(index, count);
                apply(day, token);
                index++;
            },
            cancellation);

        progress.Finish(result.Outcome, result.DaysApplied);
        return result;
    }
}
