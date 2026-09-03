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

    /// <summary>
    /// How much of the line the eraser has to blank: exactly what was drawn on it.
    /// </summary>
    /// <remarks>
    /// A constant 60 before, which is narrower than the label discovery uses — 65 characters — so
    /// the tail of the last frame survived the erase and sat on the end of the line the spinner
    /// left behind: <c>...the first time) 23s3s</c>. Any width shorter than the widest frame leaves
    /// some of that frame on screen, so it is measured rather than assumed. Written under
    /// <see cref="_gate"/> with the rest of the drawing state.
    /// </remarks>
    private int _drawnWidth;

    /// <summary>
    /// How long a command may take before it is worth saying it is still going.
    /// </summary>
    /// <remarks>
    /// Long enough that the commands which finish in milliseconds — most of them — show nothing at
    /// all, and short enough that anything a user would notice as a pause is announced before they
    /// wonder. A spinner that flickered on every <c>list projects</c> would be worse than silence.
    /// </remarks>
    public static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(300);

    /// <summary>What a command that is taking a while says about itself.</summary>
    /// <remarks>
    /// Generic rather than an echo of the command: the command is already on the line above, and a
    /// long one would wrap the spinner's own line on a narrow window.
    /// </remarks>
    public const string WorkingLabel = "Working";

    /// <summary>
    /// What the spinner says it is doing. Not readonly, because a long step that learns something
    /// part-way through should be able to say so.
    /// </summary>
    /// <remarks>
    /// Written and read under <see cref="_gate"/> along with the rest of the drawing state: the
    /// caller updating it is on a different thread from the one drawing frames, and the erase in
    /// <see cref="Dispose"/> sizes itself from a width this can change.
    /// </remarks>
    private string _label;

    private readonly TimeSpan _delay;

    /// <summary>
    /// Where to draw — captured at construction, not read at each frame.
    /// </summary>
    /// <remarks>
    /// <see cref="CommandColour"/> replaces <see cref="Console.Out"/> with a buffer for the whole
    /// of a command so its output can be coloured by the verdict. A spinner reading
    /// <c>Console.Out</c> as it drew would therefore animate INTO that buffer — invisible while it
    /// mattered, then flushed out as a row of dead spinner frames in the middle of the output.
    /// Holding the writer from before the swap is what puts the indicator on the screen instead.
    /// </remarks>
    private readonly TextWriter _out;
    private readonly CancellationTokenSource _stop = new();

    /// <summary>
    /// A thread of its own, deliberately not a <see cref="Task"/>.
    /// </summary>
    /// <remarks>
    /// On the thread pool the animation is queued behind the work it is decorating, and the moments
    /// the pool has no free worker are exactly the moments a user most needs to be told the tool is
    /// still going. A loaded CI runner showed it: half a second of a 20ms spinner produced no output
    /// at all, because the frame loop never got a worker. One thread for the length of one slow
    /// command is the cheaper side of that trade. Background, so it can never hold the process open.
    /// </remarks>
    private readonly Thread _loop;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>Guards <see cref="_drew"/> against the dispose that races the first frame.</summary>
    private readonly object _gate = new();

    /// <summary>Whether anything was ever put on screen, and so whether anything must be tidied
    /// away.</summary>
    private bool _drew;

    private Spinner(string label, TimeSpan delay)
    {
        _label = label;
        _delay = delay;
        _out = Console.Out;
        _loop = new Thread(Spin) { IsBackground = true, Name = "spinner" };
        _loop.Start();
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
    /// Changes what the spinner says while it is still spinning.
    /// </summary>
    /// <remarks>
    /// For a step whose progress is only knowable once it has started — domain discovery, which
    /// cannot say how many classes it will find until it has found some. A console that is not
    /// animating ignores this entirely: <see cref="NoSpinner"/> has already printed its one line,
    /// and a piped transcript must not grow a line per update.
    /// </remarks>
    public static void Describe(IDisposable spinner, string label)
    {
        if (spinner is not Spinner running)
            return;

        lock (running._gate)
            running._label = label;
    }

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
            return new Spinner(label, TimeSpan.Zero);

        Console.WriteLine(label);

        return new NoSpinner();
    }

    /// <summary>
    /// A spinner that says nothing unless the work outlasts <paramref name="delay"/>.
    /// </summary>
    /// <remarks>
    /// For wrapping work that is <i>usually</i> instant — which is every command the user types.
    /// Disposed inside the delay it draws nothing and leaves nothing behind, so the ordinary case
    /// looks exactly as it always has.
    /// <para>
    /// A console that cannot animate is left completely alone, label and all: this wraps every
    /// command, and printing the label into a redirected run would put a new line on the front of
    /// every command in every piped transcript.
    /// </para>
    /// </remarks>
    public static IDisposable StartAfter(string label, TimeSpan delay)
        => StartAfter(label, Enabled(Console.IsOutputRedirected, Console.IsInputRedirected), delay);

    /// <summary>As <see cref="StartAfter(string, TimeSpan)"/>, with the decision supplied — the
    /// seam <see cref="Start(string, bool)"/> exists for, and for the same reason.</summary>
    internal static IDisposable StartAfter(string label, bool animate, TimeSpan delay)
        => animate ? new Spinner(label, delay) : new NoSpinner();

    private void Spin()
    {
        var tick = 0;

        try
        {
            // Nothing on screen until the work has earned it. Cancelled inside this wait, the
            // spinner never existed as far as the user is concerned.
            if (_delay > TimeSpan.Zero && Waited(_delay))
                return;

            while (!_stop.IsCancellationRequested)
            {
                lock (_gate)
                {
                    // Checked inside the lock: Dispose may have run between the loop condition and
                    // here, and a frame drawn after the tidy-up would be left on screen for good.
                    if (_stop.IsCancellationRequested)
                        return;

                    var frame = $"{_label} {Frame(tick++)} {_clock.Elapsed.TotalSeconds:0}s";

                    _out.Write('\r');
                    _out.Write(frame);
                    _out.Flush();

                    // Measured, not assumed: the seconds grow a digit as the work goes on, so the
                    // widest frame is not necessarily the last one drawn.
                    _drawnWidth = Math.Max(_drawnWidth, frame.Length);
                    _drew = true;
                }

                if (Waited(Tick))
                    return;
            }
        }
        catch (ObjectDisposedException)
        {
            // Dispose gave up waiting for this thread and released the token. Rare, and there is
            // nothing left to draw on.
        }
        catch (IOException)
        {
            // The console went away mid-spin. Nothing here is worth failing an operation over.
        }
    }

    /// <summary>Sleeps for <paramref name="span"/>, returning true if the spinner was stopped
    /// instead — a cancellable wait without a task to schedule.</summary>
    private bool Waited(TimeSpan span) => _stop.Token.WaitHandle.WaitOne(span);

    public void Dispose()
    {
        _stop.Cancel();

        _loop.Join(TimeSpan.FromSeconds(1));

        // Nothing was ever drawn — the work finished inside the delay — so there is nothing to
        // erase and nothing to say. This is the ordinary case for a command that returns at once.
        lock (_gate)
        {
            if (!_drew)
            {
                _stop.Dispose();
                return;
            }
        }

        // Erase, then reprint the label as a plain line, so the transcript reads the same as it
        // always did rather than keeping a frozen spinner frame in it.
        try
        {
            _out.Write('\r');
            _out.Write(new string(' ', _drawnWidth));
            _out.Write('\r');
            _out.WriteLine($"{_label} {_clock.Elapsed.TotalSeconds:0}s");
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
