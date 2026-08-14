using CodeGenerationEngine.Helper;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// When the CLI is allowed to use colour.
/// </summary>
/// <remarks>
/// The rendering itself cannot be asserted — colour goes through the console API, not the output
/// stream, and the test host redirects output, which switches it off. So the decision is what is
/// lifted out and tested; that is also the part with rules worth stating.
/// </remarks>
/// <remarks>
/// <see cref="DoNotParallelizeAttribute"/> because <see cref="WritingIsUnaffectedByColourBeingOff"/>
/// captures <see cref="Console.Out"/>, which is process-global — a sibling test writing while it
/// holds the redirect lands its output in this one's buffer. Latent when this was written; added
/// after the same race made a spinner test fail for real.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class OutputColourTests
{
    [TestMethod]
    public void RedirectedOutputGetsNoColour()
    {
        // The property every existing test depends on: a captured transcript must be exactly what
        // it was before colour existed.
        Assert.IsFalse(Output.ColourAllowed(outputRedirected: true, noColourVariable: null));
    }

    [TestMethod]
    public void NoColourIsHonouredWhateverItIsSetTo()
    {
        // The no-color.org convention: presence is what counts, not the value. "0" and "false"
        // still mean no colour, which is the part people get wrong.
        foreach (var value in new[] { "1", "0", "false", "yes", "" })
        {
            Assert.IsFalse(
                Output.ColourAllowed(outputRedirected: false, noColourVariable: value),
                $"NO_COLOR={value} was not honoured");
        }
    }

    [TestMethod]
    public void AnAttachedConsoleWithoutNoColourGetsColour()
    {
        Assert.IsTrue(Output.ColourAllowed(outputRedirected: false, noColourVariable: null));
    }

    [TestMethod]
    public void WritingIsUnaffectedByColourBeingOff()
    {
        // Whatever the colour decision, the text written must be identical. Guards against a
        // future edit that decorates the string rather than setting the console colour.
        var captured = new StringWriter();
        var previous = Console.Out;

        try
        {
            Console.SetOut(captured);

            Output.Success("done");
            Output.Error("broke");
            Output.Warning("careful");
            Output.Detail("aside");
            Output.Line("plain");
        }
        finally
        {
            Console.SetOut(previous);
        }

        var lines = captured.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { "done", "broke", "careful", "aside", "plain" }, lines);
    }

    [TestMethod]
    public void ExplainNamesTheReasonColourIsOff()
    {
        // The whole point: a user who sees no colour must be able to find out why from the tool
        // rather than by asking. Both causes are invisible from outside the process.
        Assert.Contains("NO_COLOR", Output.Explain(false, "1", null, ColourTransport.Ansi));
        Assert.Contains("redirected", Output.Explain(true, null, null, ColourTransport.Ansi));
        Assert.StartsWith("on", Output.Explain(false, null, null, ColourTransport.Ansi));
    }

    [TestMethod]
    public void ExplainQuotesTheValueSoAnEmptyOneIsStillVisible()
    {
        // NO_COLOR= (empty) still means no colour, and a bare "off (NO_COLOR is set to )" would
        // read like a bug in the message.
        Assert.Contains("''", Output.Explain(false, "", null, ColourTransport.Ansi));
    }

    [TestMethod]
    public void ExplainAgreesWithColourAllowed()
    {
        // Two functions, one truth. A mismatch would have the tool report colour as on while
        // printing none.
        foreach (var redirected in new[] { true, false })
        {
            foreach (var noColour in new[] { null, "", "1" })
            {
                var allowed = Output.ColourAllowed(redirected, noColour);
                var explained = Output.Explain(redirected, noColour, null, ColourTransport.Ansi);

                Assert.AreEqual(allowed, explained.StartsWith("on", StringComparison.Ordinal),
                    $"redirected={redirected} NO_COLOR={noColour ?? "(unset)"} -> {explained}");
            }
        }
    }

    [TestMethod]
    public void ResultFollowsTheOutcomeItIsGiven()
    {
        // Writes the same text either way; only the colour differs, and that is not observable
        // here. What is worth pinning is that it does not swallow or alter the message.
        var captured = new StringWriter();
        var previous = Console.Out;

        try
        {
            Console.SetOut(captured);

            Output.Result(true, "worked");
            Output.Result(false, "did not");
        }
        finally
        {
            Console.SetOut(previous);
        }

        var lines = captured.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToArray();

        CollectionAssert.AreEqual(new[] { "worked", "did not" }, lines);
    }

    [TestMethod]
    public void ForceColourOverridesRedirection()
    {
        // The opt-in a host uses when it captures the stream and renders the sequences itself --
        // the only route by which the VS Code panel could ever show colour.
        Assert.IsTrue(Output.ColourAllowed(
            outputRedirected: true, noColourVariable: null, forceColourVariable: "1"));
    }

    [TestMethod]
    public void NoColourBeatsForceColour()
    {
        // Asking for no colour is the stronger request: it is the one a person makes to fix
        // something they are looking at right now.
        Assert.IsFalse(Output.ColourAllowed(
            outputRedirected: false, noColourVariable: "1", forceColourVariable: "1"));

        Assert.IsFalse(Output.ColourAllowed(
            outputRedirected: true, noColourVariable: "1", forceColourVariable: "1"));
    }

    [TestMethod]
    public void ExplainNamesHowColourIsDelivered()
    {
        // "on" alone was not enough to diagnose a terminal that reported colour as enabled and still
        // rendered white. Which mechanism is in use is the missing half of that answer.
        var ansi = Output.Explain(false, null, null, ColourTransport.Ansi);
        var attributes = Output.Explain(false, null, null, ColourTransport.ConsoleAttributes);

        Assert.Contains("ANSI", ansi);
        Assert.Contains("console attributes", attributes);
        Assert.AreNotEqual(ansi, attributes);
    }

    [TestMethod]
    public void ExplainTellsYouHowToOverrideRedirection()
    {
        Assert.Contains("FORCE_COLOR",
            Output.Explain(true, null, null, ColourTransport.Ansi));
    }
}
