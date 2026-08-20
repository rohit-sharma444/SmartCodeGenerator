using System.Diagnostics;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The "still working" indicator. Its animation needs a console; its rules do not.
/// </summary>
/// <remarks>
/// <see cref="DoNotParallelizeAttribute"/> because these capture <see cref="Console.Out"/>, which is
/// process-global: without it a sibling test's output lands in this one's buffer. That is exactly
/// how it first failed — 27 characters captured instead of 18, the extra nine being another test's
/// "Working" label.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class SpinnerTests
{
    [TestMethod]
    public void FramesCycleAndNeverGoOutOfRange()
    {
        // Called with an ever-increasing tick for as long as an operation runs, so an unbounded
        // index must stay in bounds — a build can tick for minutes.
        for (var tick = 0; tick < 500; tick++)
            Assert.IsGreaterThan(' ', Spinner.Frame(tick));

        Assert.AreEqual(Spinner.Frame(0), Spinner.Frame(4));
        Assert.AreNotEqual(Spinner.Frame(0), Spinner.Frame(1));
    }

    [TestMethod]
    public void ANegativeTickDoesNotThrow()
    {
        // Overflow after long enough. Cheaper to be total than to reason about the ceiling.
        Assert.IsGreaterThan(' ', Spinner.Frame(-1));
        Assert.IsGreaterThan(' ', Spinner.Frame(int.MinValue));
    }

    [TestMethod]
    public void RedirectionDisablesTheAnimation()
    {
        // A spinner rewriting its line with carriage returns turns a piped transcript into noise,
        // and every existing script depends on that transcript.
        Assert.IsFalse(Spinner.Enabled(outputRedirected: true, inputRedirected: false));
        Assert.IsFalse(Spinner.Enabled(outputRedirected: false, inputRedirected: true));
        Assert.IsFalse(Spinner.Enabled(outputRedirected: true, inputRedirected: true));

        Assert.IsTrue(Spinner.Enabled(outputRedirected: false, inputRedirected: false));
    }

    [TestMethod]
    public void StartAlwaysReturnsSomethingDisposable()
    {
        // So the caller has no branch. The test host is redirected, so this exercises the
        // no-animation path — which must still print the label and dispose cleanly.
        using var spinner = Spinner.Start("Working");

        Assert.IsNotNull(spinner);
    }

    [TestMethod]
    public void TheLabelIsStillPrintedWhenTheAnimationIsOff()
    {
        var captured = new StringWriter();
        var previous = Console.Out;

        try
        {
            Console.SetOut(captured);

            // animate: false explicitly. Console.IsOutputRedirected reports the process's own
            // stdout handle and ignores SetOut, so the test host reads as "not redirected" and
            // would animate — the quiet path could not be reached without this seam.
            using (Spinner.Start("Discovering domain", animate: false))
            {
            }
        }
        finally
        {
            Console.SetOut(previous);
        }

        // Byte-for-byte what a piped run saw before spinners existed: the label, once.
        Assert.AreEqual("Discovering domain", captured.ToString().Trim());
    }

    /// <summary>
    /// A capture that can be read while the spinner thread is still writing to it, which a
    /// <see cref="StringWriter"/> does not promise.
    /// </summary>
    private sealed class Capture : TextWriter
    {
        private readonly System.Text.StringBuilder _text = new();

        public override System.Text.Encoding Encoding => System.Text.Encoding.Unicode;

        public override void Write(char value)
        {
            lock (_text)
                _text.Append(value);
        }

        public override void Write(string? value)
        {
            lock (_text)
                _text.Append(value);
        }

        public override string ToString()
        {
            lock (_text)
                return _text.ToString();
        }
    }

    /// <summary>How long a test waits for a frame when it only cares that one arrives.</summary>
    /// <remarks>
    /// Long enough that only a spinner which never draws at all runs it out. The tests below used to
    /// sleep a few hundred milliseconds instead, which asserts that a loaded machine scheduled the
    /// frame loop inside that window — three of them went red on a CI runner for exactly that
    /// reason, on an empty capture, and it was never the promise under test.
    /// </remarks>
    private static readonly TimeSpan LongEnoughForAFrame = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Blocks until the spinner has drawn its first frame into <paramref name="capture"/>, or
    /// <paramref name="budget"/> runs out — in which case the caller's assertion fails on an empty
    /// capture, which is the failure worth reporting.
    /// </summary>
    private static void WaitUntilItDraws(Capture capture, TimeSpan budget)
    {
        var waited = Stopwatch.StartNew();

        while (capture.ToString().Length == 0 && waited.Elapsed < budget)
            Thread.Sleep(10);
    }

    /// <summary>
    /// Runs a spinner with the console captured until it has drawn, and hands back what it wrote.
    /// </summary>
    private static Capture WhatItDrew(string label, TimeSpan budget)
    {
        var screen = new Capture();
        var previous = Console.Out;

        try
        {
            Console.SetOut(screen);

            using (Spinner.StartAfter(label, animate: true, TimeSpan.FromMilliseconds(20)))
            {
                WaitUntilItDraws(screen, budget);
            }
        }
        finally
        {
            Console.SetOut(previous);
        }

        return screen;
    }

    /// <summary>Runs <paramref name="body"/> with the console captured, and returns what it wrote.</summary>
    private static string Captured(Action body)
    {
        var captured = new StringWriter();
        var previous = Console.Out;

        try
        {
            Console.SetOut(captured);
            body();
        }
        finally
        {
            Console.SetOut(previous);
        }

        return captured.ToString();
    }

    [TestMethod]
    public void AnOperationThatFinishesQuicklyIsNeverAnnounced()
    {
        // The whole point of the delay. Every command is wrapped in one of these, and most finish
        // in milliseconds — a spinner that flashed on each would be worse than the silence it
        // replaces.
        var written = Captured(
            () => Spinner.StartAfter("Working", animate: true, TimeSpan.FromSeconds(30)).Dispose());

        Assert.IsEmpty(written);
    }

    [TestMethod]
    public void PastTheDelayItSaysSoAndLeavesTheTimeItTook()
    {
        var screen = WhatItDrew("Working", LongEnoughForAFrame);

        // Drawn while it ran, and still readable in the transcript afterwards: "it took a moment"
        // is worth knowing once the moment has passed.
        StringAssert.Contains(screen.ToString(), "Working");
    }

    [TestMethod]
    public void AConsoleThatCannotAnimateIsLeftCompletelyAlone()
    {
        // Not even the label, unlike Start(label, animate: false). A piped transcript is what every
        // script reads, and a "Working" line on the front of every command in it would be new
        // output that nothing asked for.
        var written = Captured(
            () => Spinner.StartAfter("Working", animate: false, TimeSpan.Zero).Dispose());

        Assert.IsEmpty(written);
    }

    [TestMethod]
    public void ItDrawsWhereItStarted_NotWhereConsoleOutHasSinceMoved()
    {
        var screen = new Capture();
        var buffer = new Capture();
        var previous = Console.Out;

        try
        {
            Console.SetOut(screen);

            using (Spinner.StartAfter("Working", animate: true, TimeSpan.FromMilliseconds(20)))
            {
                // Exactly what CommandColour does for the whole of a command, so that its output
                // can be coloured by the verdict once it is known.
                Console.SetOut(buffer);
                WaitUntilItDraws(screen, LongEnoughForAFrame);
            }
        }
        finally
        {
            Console.SetOut(previous);
        }

        // On the screen, where the user is: a spinner that followed Console.Out would animate into
        // the buffer instead — invisible while it mattered, then flushed out as a row of dead
        // frames in the middle of the command's output.
        StringAssert.Contains(screen.ToString(), "Working");
        Assert.IsEmpty(buffer.ToString());
    }

    /// <summary>
    /// What the captured writes would actually look like on a console, one string per line.
    /// </summary>
    /// <remarks>
    /// A spinner's output only means anything once the carriage returns are applied: on screen it
    /// is one line rewritten over and over, while in a buffer it is every frame end to end. Without
    /// replaying that, a test cannot see what the user is left looking at — which is where the
    /// residue this fixes was hiding.
    /// </remarks>
    private static List<string> Screen(string captured)
    {
        var lines = new List<string>();
        var line = new System.Text.StringBuilder();
        var column = 0;

        foreach (var ch in captured)
        {
            switch (ch)
            {
                case '\r':
                    column = 0;
                    break;

                case '\n':
                    lines.Add(line.ToString());
                    line.Clear();
                    column = 0;
                    break;

                default:
                    while (line.Length < column)
                        line.Append(' ');

                    if (column < line.Length)
                        line[column] = ch;
                    else
                        line.Append(ch);

                    column++;
                    break;
            }
        }

        if (line.Length > 0)
            lines.Add(line.ToString());

        return lines;
    }

    [TestMethod]
    public void TheLineItLeavesBehindCarriesNoRemainsOfTheAnimation()
    {
        // The real label from the discovery spinner, and the reason this was ever wrong: it is 65
        // characters, the eraser blanked a fixed 60, and the tail of the last frame — "3s" of
        // "23s" — survived to sit on the end of the line as "...first time) 23s3s".
        const string label = "Discovering domain (this may fetch from Wikipedia the first time)";

        var screen = Screen(WhatItDrew(label, LongEnoughForAFrame).ToString());

        Assert.IsNotEmpty(screen);

        // TrimEnd, and only TrimEnd: the eraser blanks the widest frame and the line it reprints is
        // a couple of characters shorter, so the cells past its end hold spaces. Blank cells are
        // indistinguishable from nothing on a console — a leftover "3s" is not.
        StringAssert.Matches(
            screen[^1].TrimEnd(),
            new System.Text.RegularExpressions.Regex(
                "^" + System.Text.RegularExpressions.Regex.Escape(label) + @" \d+s$"),
            $"left on screen: '{screen[^1]}'");
    }

    [TestMethod]
    public void ItStillDrawsWhenEveryThreadPoolWorkerIsBusy()
    {
        // The CI failure this was written for: the animation ran as a thread-pool task, so on a
        // loaded runner it queued behind the very work it was there to say was still going, and
        // half a second of "Working" arrived as nothing at all. An indicator whose whole job is to
        // prove liveness while the machine is busy may not depend on that queue being short.
        ThreadPool.GetMinThreads(out var workers, out _);

        using var release = new ManualResetEventSlim(false);

        // Enough to hold every worker the pool starts with and fill the queue behind them, so a
        // work item submitted now waits on thread injection rather than running.
        for (var queued = 0; queued < workers + 4; queued++)
            ThreadPool.UnsafeQueueUserWorkItem(_ => release.Wait(), null);

        Capture screen;

        try
        {
            // A second, and the bound is the whole assertion — wait indefinitely and this test is
            // vacuous. Measured on this machine with the pool held: a thread of its own draws in
            // 45ms, a queued work item in 4305ms, because it waits on the pool injecting a thread.
            screen = WhatItDrew("Working", TimeSpan.FromSeconds(1));
        }
        finally
        {
            // Before the assertion, so a red run does not leave the pool starved for the tests
            // after it.
            release.Set();
        }

        StringAssert.Contains(screen.ToString(), "Working");
    }

    [TestMethod]
    public void TheThresholdIsLongEnoughNotToFlickerAndShortEnoughToBeSeen()
    {
        Assert.IsGreaterThan(TimeSpan.Zero, Spinner.Threshold);
        Assert.IsLessThan(TimeSpan.FromSeconds(1), Spinner.Threshold);
    }
}
