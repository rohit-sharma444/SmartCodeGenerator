namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// The default sink: the per-phase <see cref="ConsoleProgressBar"/> the importer and the
/// pre-background startup path have always drawn. This keeps that behaviour when no other sink is
/// injected, so the importer and the existing tests are unchanged.
/// </summary>
public sealed class ConsoleBarProgress : IStoreUpdateProgress
{
    private ConsoleProgressBar? _bar;

    public void BeginDay(int dayIndex, int dayCount) { }

    public void BeginPhase(UpdatePhase phase, int day, long total, long initial)
    {
        _bar?.Dispose();
        var label = phase == UpdatePhase.Download ? $"  download {day}" : "  apply   ";
        _bar = new ConsoleProgressBar(label, total, initial);
    }

    public void Report(long done) => _bar?.Report(done);

    public void EndPhase()
    {
        _bar?.Dispose();
        _bar = null;
    }

    public void Finish(StartupUpdateOutcome outcome, int daysApplied)
    {
        _bar?.Dispose();
        _bar = null;
    }
}
