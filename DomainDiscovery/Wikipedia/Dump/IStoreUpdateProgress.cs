namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// Receives progress while the store is brought forward. <see cref="IncrementalUpdater"/> drives the
/// phase callbacks (<see cref="BeginPhase"/>/<see cref="Report"/>/<see cref="EndPhase"/>); the
/// orchestrator drives <see cref="BeginDay"/> and <see cref="Finish"/>.
/// </summary>
/// <remarks>
/// <b><see cref="Report"/> must never throw.</b> It is reached from <c>CountingStream</c>, and
/// <c>BZip2InputStream</c> treats any exception from the stream it reads as end-of-input — a throwing
/// report would silently truncate the apply.
/// </remarks>
public interface IStoreUpdateProgress
{
    /// <summary>A new day's slice begins. <paramref name="dayIndex"/> is 0-based within the pending set.</summary>
    void BeginDay(int dayIndex, int dayCount);

    /// <summary>
    /// A phase of the current day begins, over <paramref name="total"/> bytes with
    /// <paramref name="initial"/> already done (a resumed download). <paramref name="day"/> is for
    /// labelling only.
    /// </summary>
    void BeginPhase(UpdatePhase phase, int day, long total, long initial);

    /// <summary>Absolute bytes done in the current phase.</summary>
    void Report(long done);

    /// <summary>The current phase finished cleanly (not on cancellation).</summary>
    void EndPhase();

    /// <summary>The whole update ended, however it ended.</summary>
    void Finish(StartupUpdateOutcome outcome, int daysApplied);
}
