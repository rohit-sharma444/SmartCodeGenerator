using CodeGenerationEngine.Helper;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// Capturing what a command printed, as plain text.
/// </summary>
/// <remarks>
/// The chat page shows a command's whole output and tints it by verdict, so the text it captures
/// must carry no colour of its own. Colour is decided once per process
/// (<c>ColourDelivery.Current</c> is a static readonly), so suppression is a flag consulted at the
/// point of writing — otherwise the ANSI transport writes escape sequences into the buffer and the
/// transcript shows them as text.
/// <para>
/// <see cref="DoNotParallelizeAttribute"/> because <c>Console.SetOut</c> is process-global: two of
/// these running at once capture each other's output.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public class OutputCaptureTests
{
    [TestMethod]
    public void Suppression_takes_the_transport_to_none()
    {
        // The pure half, testable because the transport is an argument. The test host is
        // redirected, so ColourDelivery.Current is None here whatever the code does.
        Assert.AreEqual(
            ColourTransport.None, Output.TransportFor(ColourTransport.Ansi, suppressed: true));

        Assert.AreEqual(
            ColourTransport.None,
            Output.TransportFor(ColourTransport.ConsoleAttributes, suppressed: true));

        Assert.AreEqual(
            ColourTransport.Ansi, Output.TransportFor(ColourTransport.Ansi, suppressed: false));
    }

    [TestMethod]
    public void Captures_what_was_written_and_restores_the_previous_writer()
    {
        var before = Console.Out;
        string captured;

        using (var capture = OutputCapture.Start())
        {
            Console.WriteLine("first");
            Output.Success("second");
            captured = capture.Text;
        }

        Assert.AreSame(before, Console.Out);
        Assert.Contains("first", captured);
        Assert.Contains("second", captured);
    }

    [TestMethod]
    public void Suppresses_colour_only_while_capturing()
    {
        Assert.IsFalse(OutputCapture.Suppressed);

        using (OutputCapture.Start())
            Assert.IsTrue(OutputCapture.Suppressed);

        Assert.IsFalse(OutputCapture.Suppressed);
    }

    [TestMethod]
    public void Restores_the_writer_when_the_command_throws()
    {
        // The chat writes the buffer to the turn in a finally for the same reason CommandColour
        // does: a command that throws must still show what it printed before it did. If the
        // writer is not restored, every later command prints into a dead buffer.
        var before = Console.Out;

        try
        {
            using var capture = OutputCapture.Start();
            Console.WriteLine("printed before the throw");
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        Assert.AreSame(before, Console.Out);
        Assert.IsFalse(OutputCapture.Suppressed);
    }

    [TestMethod]
    public void Refuses_to_nest()
    {
        // Console.SetOut is process-global, so a nested capture would steal the outer one's
        // output and restore the wrong writer. The chat serializes commands; this is the
        // invariant made enforceable rather than assumed.
        using var outer = OutputCapture.Start();

        Assert.ThrowsExactly<InvalidOperationException>(() => OutputCapture.Start());
    }
}
