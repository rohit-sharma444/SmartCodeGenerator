using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// When the CLI is allowed to use colour.
/// </summary>
/// <remarks>
/// The rendering itself cannot be asserted — colour goes through the console API, not the output
/// stream, and the test host redirects output, which switches it off. So the decision is what is
/// lifted out and tested; that is also the part with rules worth stating.
/// </remarks>
[TestClass]
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
}
