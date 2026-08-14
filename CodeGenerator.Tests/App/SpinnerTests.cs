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
}
