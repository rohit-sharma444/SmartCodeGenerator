using CodeGenerationEngine.Helper;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Colouring a command's whole output by its result.
/// </summary>
/// <remarks>
/// The requirement is that a successful command reads green throughout, a failure red and an
/// unrecognized command amber — the whole block. Since the colour depends on the result, and the
/// result is only known once the command has finished printing, the output has to be buffered.
/// <para>
/// <see cref="DoNotParallelizeAttribute"/> because the buffering swaps <see cref="Console.Out"/>,
/// which is process-global.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public class CommandColourTests
{
    [TestMethod]
    public void SucceedingIsGreen()
    {
        Assert.AreEqual(CommandVerdict.Succeeded,
            CommandColour.Verdict(recognized: true, handled: true));

        Assert.AreEqual(OutputRole.Success, CommandColour.Role(CommandVerdict.Succeeded));
    }

    [TestMethod]
    public void AnUnderstoodCommandThatFailedIsRed()
    {
        Assert.AreEqual(CommandVerdict.Failed,
            CommandColour.Verdict(recognized: true, handled: false));

        Assert.AreEqual(OutputRole.Error, CommandColour.Role(CommandVerdict.Failed));
    }

    [TestMethod]
    public void AnUnrecognizedCommandIsAmberNotRed()
    {
        // The distinction a user can act on: the tool did not fail, it did not know the words.
        Assert.AreEqual(CommandVerdict.Unrecognized,
            CommandColour.Verdict(recognized: false, handled: false));

        Assert.AreEqual(OutputRole.Warning, CommandColour.Role(CommandVerdict.Unrecognized));
    }

    [TestMethod]
    public void HandledWinsEvenIfRecognitionSaysOtherwise()
    {
        // Defensive: a command that did the work is a success whatever the recognition flag says.
        Assert.AreEqual(CommandVerdict.Succeeded,
            CommandColour.Verdict(recognized: false, handled: true));
    }

    [TestMethod]
    public void EveryVerdictHasAColour()
    {
        // A verdict with no role would fall through to plain and silently lose its colour.
        foreach (var verdict in Enum.GetValues<CommandVerdict>())
            Assert.AreNotEqual(OutputRole.Plain, CommandColour.Role(verdict));
    }

    [TestMethod]
    public void TheCommandStillRunsAndItsOutputSurvives()
    {
        // Buffering must not swallow anything. The test host is redirected, so this exercises the
        // no-buffering path — which is exactly the path every piped script takes.
        var captured = new StringWriter();
        var previous = Console.Out;
        var ran = false;

        try
        {
            Console.SetOut(captured);

            CommandColour.Run(() =>
            {
                ran = true;
                Console.WriteLine("line one");
                Console.WriteLine("line two");
                return CommandVerdict.Succeeded;
            });
        }
        finally
        {
            Console.SetOut(previous);
        }

        Assert.IsTrue(ran);
        Assert.Contains("line one", captured.ToString());
        Assert.Contains("line two", captured.ToString());
    }

    [TestMethod]
    public void OutputPrintedBeforeAThrowIsNotLost()
    {
        // A crash must not also destroy the evidence of what the command had managed to do.
        var captured = new StringWriter();
        var previous = Console.Out;

        try
        {
            Console.SetOut(captured);

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                CommandColour.Run(() =>
                {
                    Console.WriteLine("got this far");
                    throw new InvalidOperationException("boom");
                }));
        }
        finally
        {
            Console.SetOut(previous);
        }

        Assert.Contains("got this far", captured.ToString());
    }

    [TestMethod]
    public void ConsoleOutIsAlwaysRestored()
    {
        // If it were not, every later line in the session would vanish into a dead buffer.
        var before = Console.Out;

        try
        {
            CommandColour.Run(() => throw new InvalidOperationException("boom"));
        }
        catch (InvalidOperationException)
        {
        }

        Assert.AreSame(before, Console.Out);
    }
}
