namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// Turns the updater's per-phase byte reports into one overall percentage across every pending day,
/// and hands each new whole-percent value to a renderer. Each day is an equal <c>1/dayCount</c>
/// slice; within a day the download fills the first half of that slice and the apply the second.
/// </summary>
/// <remarks>
/// De-dupes on the integer percent, so a renderer is called at most 101 times over a whole update —
/// that is the throttle. Emission itself is pure arithmetic and never throws on its own; it does
/// call the supplied renderer, whose failures are contained at the call site (<c>CountingStream</c>
/// swallows on the apply path, and the renderers guard their own writes) — this class must not be
/// relied on to make an unguarded renderer safe.
/// </remarks>
public sealed class OverallPercentProgress : IStoreUpdateProgress
{
    private readonly Action<int> _onPercent;
    private readonly Action<StartupUpdateOutcome, int> _onFinish;

    private int _dayIndex;
    private int _dayCount = 1;
    private UpdatePhase _phase;
    private long _total;
    private int _lastPercent = -1;

    public OverallPercentProgress(Action<int> onPercent, Action<StartupUpdateOutcome, int> onFinish)
    {
        _onPercent = onPercent;
        _onFinish = onFinish;
    }

    public void BeginDay(int dayIndex, int dayCount)
    {
        _dayIndex = dayIndex;
        _dayCount = dayCount < 1 ? 1 : dayCount;
    }

    public void BeginPhase(UpdatePhase phase, int day, long total, long initial)
    {
        _phase = phase;
        _total = total;
        Emit(initial);
    }

    public void Report(long done) => Emit(done);

    public void EndPhase() => Emit(_total);

    public void Finish(StartupUpdateOutcome outcome, int daysApplied)
        => _onFinish(outcome, daysApplied);

    private void Emit(long done)
    {
        var fraction = _total > 0 ? Math.Clamp((double)done / _total, 0, 1) : 0;

        var phaseFraction = _phase == UpdatePhase.Download
            ? 0.5 * fraction
            : 0.5 + 0.5 * fraction;

        var overall = (_dayIndex + phaseFraction) / _dayCount;
        var percent = Math.Clamp((int)Math.Round(overall * 100), 0, 100);

        if (percent == _lastPercent)
            return;

        _lastPercent = percent;
        _onPercent(percent);
    }
}
