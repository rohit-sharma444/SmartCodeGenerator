namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// One check-and-apply pass over the store, and the loop that repeats it. This is what a headless
/// host (the Windows Service) runs; it owns no console, no timer, and no service plumbing.
/// </summary>
/// <remarks>
/// <b>Nothing here may throw into the host.</b> Being unable to update is never fatal: the next
/// tick tries again and the store is merely as stale as it already was.
/// <para>
/// Resumability comes from <see cref="IncrementalUpdater"/>, which commits a day at a time and moves
/// the store's dump date only once a day has FULLY landed. So a service stop, a shutdown, or a crash
/// mid-day costs the day in progress and nothing else.
/// </para>
/// </remarks>
public sealed class StoreUpdateCycle
{
    /// <summary>Asks how far behind the store is.</summary>
    public delegate DumpStaleness Check();

    /// <summary>
    /// Builds the per-day apply for a given progress sink. This is a factory rather than a plain
    /// delegate because <see cref="IncrementalUpdater"/> takes its sink at construction, not per day.
    /// </summary>
    public delegate StartupDumpUpdate.ApplyDay ApplyFactory(IStoreUpdateProgress progress);

    private readonly Check _check;
    private readonly ApplyFactory _apply;
    private readonly IUpdateLog _log;
    private readonly StoreUpdateStatusFile _status;
    private readonly string? _version;
    private readonly Func<DateTimeOffset> _now;

    public StoreUpdateCycle(
        Check check, ApplyFactory apply, IUpdateLog log, StoreUpdateStatusFile status,
        string? serviceVersion = null, Func<DateTimeOffset>? now = null)
    {
        _check = check;
        _apply = apply;
        _log = log;
        _status = status;
        _version = serviceVersion;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public StartupUpdateResult RunOnce(CancellationToken cancellation)
    {
        var lastSuccess = _status.Read().LastSuccessUtc;

        try
        {
            _status.Write(new StoreUpdateStatus(
                StoreUpdateState.Checking, 0, 0, null, lastSuccess, null, _version));

            var staleness = _check();

            if (!staleness.IsStale)
                return NothingToDo(staleness, lastSuccess);

            _log.Info(
                $"{staleness.PendingDays.Count} day(s) pending, about " +
                $"{staleness.ApproximateGigabytes:F1} GB.");

            var progress = new StatusFileProgress(
                _status.Write, staleness.PendingDays.Count, lastSuccess, _version, _now);

            var result = new BackgroundStoreUpdate().Run(
                staleness, progress, _apply(progress), cancellation);

            _log.Info(StoreUpdateText.Final(result.Outcome, result.DaysApplied));

            if (result.Outcome is StartupUpdateOutcome.Failed)
                _log.Error(result.Failure ?? "The update failed.");

            return result;
        }
        catch (Exception failure)
        {
            // The check itself can break (a locked store, a broken HttpClient). Record it and carry
            // on; the next tick tries again.
            _log.Error("The update cycle broke.", failure);

            _status.Write(new StoreUpdateStatus(
                StoreUpdateState.Failed, 0, 0, null, lastSuccess, failure.Message, _version));

            return new StartupUpdateResult(0, StartupUpdateOutcome.Failed, failure.Message);
        }
    }

    /// <summary>
    /// Runs a cycle, waits, repeats — until cancelled. <paramref name="delay"/> is injected so the
    /// loop can be tested without waiting a day.
    /// </summary>
    public async Task RunLoop(
        TimeSpan interval, Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            RunOnce(cancellation);

            try
            {
                await delay(interval, cancellation);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private StartupUpdateResult NothingToDo(DumpStaleness staleness, DateTimeOffset? lastSuccess)
    {
        // IsCurrent, not "no pending days": a check Wikimedia never answered knows nothing, and must
        // not be recorded as a clean bill of health.
        if (staleness.IsCurrent)
        {
            _log.Info("The store is up to date.");
            _status.Write(new StoreUpdateStatus(
                StoreUpdateState.Idle, 100, 0, null, lastSuccess, null, _version));
        }
        else
        {
            _log.Info("Wikimedia did not answer. Nothing claimed, nothing downloaded.");
            _status.Write(new StoreUpdateStatus(
                StoreUpdateState.Unknown, 0, 0, null, lastSuccess, null, _version));
        }

        return new StartupUpdateResult(0, StartupUpdateOutcome.NothingToDo);
    }
}
