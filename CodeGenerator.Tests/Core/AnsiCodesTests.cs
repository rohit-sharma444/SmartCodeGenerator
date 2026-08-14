using CodeGenerationEngine.Helper;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// The escape sequences, and their agreement with the console-attribute equivalents.
/// </summary>
/// <remarks>
/// Two mechanisms deliver the same four roles. If they drifted apart, colour would mean one thing on
/// a modern terminal and another on an old console — so every role is walked through both.
/// </remarks>
[TestClass]
public class AnsiCodesTests
{
    private const char Escape = '\u001b';

    [TestMethod]
    public void EveryColouredRoleHasBothAnAnsiCodeAndAConsoleColour()
    {
        // A role with one and not the other renders on one kind of terminal and silently not on the
        // other, which is the hardest sort of gap to notice.
        foreach (var role in Enum.GetValues<OutputRole>().Where(r => r != OutputRole.Plain))
        {
            Assert.IsNotNull(AnsiCodes.Start(role), $"{role} has no ANSI code");
            Assert.IsNotNull(AnsiCodes.ConsoleColourFor(role), $"{role} has no console colour");
        }
    }

    [TestMethod]
    public void PlainHasNeither()
    {
        Assert.IsNull(AnsiCodes.Start(OutputRole.Plain));
        Assert.IsNull(AnsiCodes.ConsoleColourFor(OutputRole.Plain));
    }

    [TestMethod]
    public void EverySequenceIsAWellFormedSgrCode()
    {
        foreach (var role in Enum.GetValues<OutputRole>().Where(r => r != OutputRole.Plain))
        {
            var code = AnsiCodes.Start(role)!;

            Assert.AreEqual(Escape, code[0], $"{role} does not start with ESC");
            Assert.AreEqual('[', code[1], $"{role} is not a CSI sequence");
            Assert.AreEqual('m', code[^1], $"{role} is not terminated with 'm'");
        }
    }

    [TestMethod]
    public void TheThreeRolesUseDistinctColours()
    {
        // Green, red and amber have to be told apart; that is the entire point of them.
        var codes = new[] { OutputRole.Success, OutputRole.Error, OutputRole.Warning }
            .Select(AnsiCodes.Start)
            .ToList();

        Assert.AreEqual(codes.Count, codes.Distinct().Count());
    }

    [TestMethod]
    public void WrappingAddsAStartAndAReset()
    {
        var wrapped = AnsiCodes.Wrap("hello", OutputRole.Success);

        Assert.StartsWith(AnsiCodes.Start(OutputRole.Success)!, wrapped);
        Assert.EndsWith($"{Escape}[0m", wrapped);
        Assert.Contains("hello", wrapped);
    }

    [TestMethod]
    public void WrappingIsReversible()
    {
        // The safety property: colour decorates text and never alters it. If stripping did not give
        // the original back, colour would be corrupting output rather than styling it.
        foreach (var role in Enum.GetValues<OutputRole>())
        {
            const string text = "Saved generated class to: D:\\Work\\Shop\\Order.cs";

            Assert.AreEqual(text, AnsiCodes.Strip(AnsiCodes.Wrap(text, role)), $"{role}");
        }
    }

    [TestMethod]
    public void MultiLineTextSurvivesWrappingAndStripping()
    {
        var text = "Classes in Shop" + Environment.NewLine + "Order" + Environment.NewLine;

        Assert.AreEqual(text, AnsiCodes.Strip(AnsiCodes.Wrap(text, OutputRole.Success)));
    }

    [TestMethod]
    public void PlainTextIsNotWrappedAtAll()
    {
        Assert.AreEqual("hello", AnsiCodes.Wrap("hello", OutputRole.Plain));
    }

    [TestMethod]
    public void EmptyTextIsNotWrapped()
    {
        // Wrapping nothing would emit sequences with no text between them — invisible if the terminal
        // understands them and visible rubbish if it does not, for no benefit either way.
        Assert.AreEqual(string.Empty, AnsiCodes.Wrap(string.Empty, OutputRole.Success));
    }

    [TestMethod]
    public void StrippingLeavesTextWithoutSequencesAlone()
    {
        Assert.AreEqual("nothing to strip", AnsiCodes.Strip("nothing to strip"));
        Assert.AreEqual(string.Empty, AnsiCodes.Strip(string.Empty));
    }

    [TestMethod]
    public void StrippingSurvivesATruncatedSequence()
    {
        // Defensive: a sequence cut off mid-way must not run past the end of the string.
        Assert.AreEqual("before", AnsiCodes.Strip($"before{Escape}[9"));
    }
}
