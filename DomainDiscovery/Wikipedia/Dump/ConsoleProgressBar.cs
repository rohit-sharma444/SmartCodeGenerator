using System.Diagnostics;

namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// A single-line progress bar for work whose total is known: bytes downloaded, bytes decoded.
/// </summary>
/// <remarks>
/// A day's increment is ~780 MB and takes minutes to fetch and longer to apply, so the alternative to
/// this is a cursor sitting still for a quarter of an hour while the user wonders whether it has
/// hung.
/// <para>
/// <b>It redraws only on a terminal.</b> `\r` overwrites a line on a console and does nothing
/// whatsoever in a pipe or a log file, where it would leave a single unreadable mile-long line — and
/// this repo drives its own CLI through a pipe. With output redirected it prints a plain progress
/// LINE, occasionally, which is what a log wants anyway.
/// </para>
/// </remarks>
public sealed class ConsoleProgressBar : IDisposable
{
    /// <summary>Redrawing faster than the eye resolves just burns the console.</summary>
    private static readonly TimeSpan RedrawInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Piped output gets a line at a time, so it must be rare enough to read.</summary>
    private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(10);

    private const int BarWidth = 32;

    private readonly string _label;
    private readonly long _total;
    private readonly bool _interactive;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>
    /// What was already done before this bar started watching — a part-downloaded file being resumed.
    /// Rate and ETA are measured from HERE, not from zero: counting 8 MB that were already on disk as
    /// though this session fetched them in no time flat reported 5,932 MB/s and an ETA of zero.
    /// </summary>
    private readonly long _initial;

    // NOT TimeSpan.MinValue. `_clock.Elapsed - TimeSpan.MinValue` overflows TimeSpan and throws on
    // the very first report — and BZip2InputStream catches ANY exception from the stream it is
    // reading and treats it as end-of-input, so the bar silently truncated the dump it was measuring.
    private TimeSpan _lastDrawn;
    private bool _everDrawn;

    private long _current;
    private bool _drewSomething;

    public ConsoleProgressBar(string label, long total, long initial = 0)
    {
        _label = label;
        _total = total;
        _initial = initial;
        _current = initial;
        _interactive = !Console.IsOutputRedirected;
    }

    /// <summary>Sets absolute progress. Cheap to call in a tight loop — it throttles its own redraw.</summary>
    public void Report(long current)
    {
        _current = current;

        var interval = _interactive ? RedrawInterval : LogInterval;
        if (_everDrawn && _clock.Elapsed - _lastDrawn < interval)
            return;

        _lastDrawn = _clock.Elapsed;
        _everDrawn = true;
        Draw(final: false);
    }

    private void Draw(bool final)
    {
        var fraction = _total > 0 ? Math.Clamp((double)_current / _total, 0, 1) : 0;

        // Only what THIS run has moved, over the time it has been running.
        var done = _current - _initial;
        var seconds = _clock.Elapsed.TotalSeconds;
        var rate = seconds > 0.5 && done > 0 ? done / seconds : 0;

        var eta = rate > 0 && _total > _current
            ? TimeSpan.FromSeconds((_total - _current) / rate)
            : TimeSpan.Zero;

        var text =
            $"{_label} {Megabytes(_current),8} / {Megabytes(_total),-8} " +
            $"{fraction,6:P1}  {Megabytes((long)rate)}/s  ETA {eta:hh\\:mm\\:ss}";

        if (!_interactive)
        {
            Console.WriteLine($"  {text}");
            _drewSomething = true;
            return;
        }

        var filled = (int)Math.Round(fraction * BarWidth);
        var bar = new string('#', filled) + new string('.', BarWidth - filled);

        // Pad to clear whatever the previous, possibly longer, line left behind.
        Console.Write($"\r  [{bar}] {text}".PadRight(Console.BufferWidth - 1));
        _drewSomething = true;

        if (final)
            Console.WriteLine();
    }

    /// <summary>Draws the finished state once, so the last thing on screen is 100% and not 99.7%.</summary>
    public void Dispose()
    {
        if (!_drewSomething)
            return;

        _current = _total;
        Draw(final: true);
    }

    private static string Megabytes(long bytes) => $"{bytes / 1024d / 1024d:N1} MB";
}
