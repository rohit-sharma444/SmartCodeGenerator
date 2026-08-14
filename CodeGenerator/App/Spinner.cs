using System.Diagnostics;

namespace CodeGenerator.App;

/// <summary>
/// A "still working" indicator for the operations that go quiet for seconds at a time.
/// </summary>
/// <remarks>
/// Domain discovery and the post-generate build both printed a label and then nothing, sometimes
/// for a minute. Silence reads as hung, and the honest signal costs one line.
/// <para>
/// Disabled whenever output or input is redirected: a spinner rewriting its line with carriage
/// returns turns a piped transcript into noise, and every existing script depends on that
/// transcript. The same gate colour, the pager and the prompts use.
/// </para>
/// </remarks>
internal sealed class Spinner : IDisposable
{
    /// <summary>Deliberately ASCII. The spinner is decoration, and decoration is the last thing
    /// worth risking on a console whose encoding could not be changed.</summary>
    private static readonly char[] Frames = ['-', '\\', '|', '/'];

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(120);

    /// <summary>How wide a line the eraser has to blank.</summary>
    private const int EraseWidth = 60;

    private readonly string _label;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private Spinner(string label)
    {
        _label = label;
        _loop = Task.Run(Spin);
    }

    /// <summary>The frame for <paramref name="tick"/>, cycling.</summary>
    /// <remarks>
    /// Not <c>Math.Abs(tick)</c>, which throws <see cref="OverflowException"/> on
    /// <see cref="int.MinValue"/> — there is no positive counterpart. A spinner is decoration and
    /// must be incapable of taking down the operation it is decorating.
    /// </remarks>
    public static char Frame(int tick)
    {
        var index = tick % Frames.Length;

        return Frames[index < 0 ? index + Frames.Length : index];
    }

    /// <summary>Whether a spinner may be drawn at all.</summary>
    public static bool Enabled(bool outputRedirected, bool inputRedirected)
        => !outputRedirected && !inputRedirected;

    /// <summary>
    /// Prints <paramref name="label"/> and, on a real console, animates beside it until disposed.
    /// </summary>
    /// <remarks>
    /// Always returns something disposable, so the caller has no branch. When animation is off the
    /// label is printed once and that is the whole of it — byte-identical to the behaviour before
    /// this existed.
    /// </remarks>
    public static IDisposable Start(string label)
        => Start(label, Enabled(Console.IsOutputRedirected, Console.IsInputRedirected));

    /// <summary>
    /// As <see cref="Start(string)"/>, with the decision supplied.
    /// </summary>
    /// <remarks>
    /// The seam exists because <see cref="Console.IsOutputRedirected"/> reports the process's own
    /// stdout handle and is unaffected by <see cref="Console.SetOut"/> — so a test capturing output
    /// still sees "not redirected" and cannot otherwise reach the quiet path, which is precisely the
    /// path every piped script depends on.
    /// </remarks>
    internal static IDisposable Start(string label, bool animate)
    {
        if (animate)
            return new Spinner(label);

        Console.WriteLine(label);

        return new NoSpinner();
    }

    private async Task Spin()
    {
        var tick = 0;

        try
        {
            while (!_stop.IsCancellationRequested)
            {
                Console.Write($"\r{_label} {Frame(tick++)} {_clock.Elapsed.TotalSeconds:0}s");

                await Task.Delay(Tick, _stop.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // The expected way out.
        }
        catch (IOException)
        {
            // The console went away mid-spin. Nothing here is worth failing an operation over.
        }
    }

    public void Dispose()
    {
        _stop.Cancel();

        try
        {
            _loop.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }

        // Erase, then reprint the label as a plain line, so the transcript reads the same as it
        // always did rather than keeping a frozen spinner frame in it.
        try
        {
            Console.Write('\r');
            Console.Write(new string(' ', EraseWidth));
            Console.Write('\r');
            Console.WriteLine($"{_label} {_clock.Elapsed.TotalSeconds:0}s");
        }
        catch (IOException)
        {
        }

        _stop.Dispose();
    }

    /// <summary>What <see cref="Start"/> hands back when there is nothing to animate.</summary>
    private sealed class NoSpinner : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
