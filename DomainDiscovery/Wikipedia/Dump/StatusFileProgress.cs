namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// An <see cref="IStoreUpdateProgress"/> that publishes a <see cref="StoreUpdateStatus"/> snapshot
/// instead of drawing to a console — the sink a headless host uses.
/// </summary>
/// <remarks>
/// Percentage aggregation is delegated to the existing <see cref="OverallPercentProgress"/>; this
/// class only decides <i>when</i> to publish and <i>what</i> the snapshot says.
/// <para>
/// <b>Nothing here may throw.</b> Every entry point is reachable from <c>CountingStream</c>, and
/// <c>BZip2InputStream</c> treats any exception from the stream it reads as end-of-input — so a
/// throwing publish would silently truncate the apply rather than merely losing a status update.
/// </para>
/// </remarks>
public sealed class StatusFileProgress : IStoreUpdateProgress
{
    /// <summary>
    /// How often a snapshot is published while applying. Progress callbacks arrive per read of the
    /// download/apply stream, which is far too often to touch the disk on every one.
    /// </summary>
    public static readonly TimeSpan PublishInterval = TimeSpan.FromSeconds(1);

    private readonly IStoreUpdateProgress _percentages;
    private readonly Action<StoreUpdateStatus> _publish;
    private readonly Func<DateTimeOffset> _now;
    private readonly int _pendingDays;
    private readonly string? _version;

    private DateTimeOffset? _lastSuccessUtc;
    private DateTimeOffset _lastPublish = DateTimeOffset.MinValue;
    private int _currentDay;
    private int _percent;

    public StatusFileProgress(
        Action<StoreUpdateStatus> publish, int pendingDays,
        DateTimeOffset? lastSuccessUtc, string? serviceVersion, Func<DateTimeOffset>? now = null)
    {
        _publish = publish;
        _pendingDays = pendingDays;
        _lastSuccessUtc = lastSuccessUtc;
        _version = serviceVersion;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _percentages = new OverallPercentProgress(OnPercent, OnFinish);
    }

    public void BeginDay(int dayIndex, int dayCount) => Safely(
        () => _percentages.BeginDay(dayIndex, dayCount));

    public void BeginPhase(UpdatePhase phase, int day, long total, long initial) => Safely(() =>
    {
        // IncrementalUpdater.UpdateDay threads the real day into both Download and Apply now, so
        // this can trust it unconditionally rather than guessing at a value learned earlier.
        _currentDay = day;

        _percentages.BeginPhase(phase, day, total, initial);
    });

    public void Report(long done) => Safely(() => _percentages.Report(done));

    public void EndPhase() => Safely(() => _percentages.EndPhase());

    public void Finish(StartupUpdateOutcome outcome, int daysApplied) => Safely(
        () => _percentages.Finish(outcome, daysApplied));

    private void OnPercent(int percent)
    {
        _percent = percent;

        var now = _now();
        if (now - _lastPublish < PublishInterval)
            return;

        _lastPublish = now;
        Publish(StoreUpdateState.Applying, error: null);
    }

    private void OnFinish(StartupUpdateOutcome outcome, int daysApplied)
    {
        // Only a completed run proves the store advanced. An interrupted or failed one keeps
        // whatever success was last recorded — the days that landed are still there.
        if (outcome is StartupUpdateOutcome.Completed)
            _lastSuccessUtc = _now();

        var failed = outcome is StartupUpdateOutcome.Failed;

        Publish(
            failed ? StoreUpdateState.Failed : StoreUpdateState.Idle,
            failed ? StoreUpdateText.Final(outcome, daysApplied) : null);
    }

    private void Publish(StoreUpdateState state, string? error) => _publish(new StoreUpdateStatus(
        state, _percent, _pendingDays, _currentDay == 0 ? null : _currentDay,
        _lastSuccessUtc, error, _version, _now()));

    private static void Safely(Action work)
    {
        try { work(); }
        catch { /* see the no-throw note on this class */ }
    }
}
