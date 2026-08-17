using CodeGenerationEngine.Helper;

namespace CodeGenerator.App;

/// <summary>What a command turned out to be, for colouring its whole output.</summary>
internal enum CommandVerdict
{
    /// <summary>It did what was asked.</summary>
    Succeeded,

    /// <summary>It was understood and could not be done.</summary>
    Failed,

    /// <summary>It was not understood at all — nothing was attempted, so nothing failed.</summary>
    Unrecognized
}

/// <summary>
/// Colours a command's <b>entire</b> output by how the command turned out.
/// </summary>
/// <remarks>
/// The requirement is that successful output reads green, a failure red and an unrecognized command
/// amber — the whole block, not one line of it. That is only possible by buffering: the colour
/// depends on the result, and the result is not known until the command has finished printing.
/// <para>
/// So this captures what a command writes, waits for its verdict, and then writes the block in one
/// colour. Buffering is the reason <c>build app</c>, <c>batch</c> and <c>demo</c> are deliberately
/// NOT run through here — they prompt mid-flight and show progress, and buffering would hide the
/// very question the user is meant to answer.
/// </para>
/// </remarks>
internal static class CommandColour
{
    /// <summary>
    /// The verdict for a pipeline outcome.
    /// </summary>
    /// <remarks>
    /// Unrecognized is amber rather than red on purpose: the tool did not fail, it simply did not
    /// know the words. Red is reserved for something that was understood and then went wrong, which
    /// is the distinction a user can act on.
    /// </remarks>
    public static CommandVerdict Verdict(bool recognized, bool handled)
    {
        if (handled)
            return CommandVerdict.Succeeded;

        return recognized ? CommandVerdict.Failed : CommandVerdict.Unrecognized;
    }

    /// <summary>The role a verdict prints in.</summary>
    public static OutputRole Role(CommandVerdict verdict) => verdict switch
    {
        CommandVerdict.Succeeded => OutputRole.Success,
        CommandVerdict.Failed => OutputRole.Error,
        _ => OutputRole.Warning,
    };

    /// <summary>
    /// Runs <paramref name="command"/>, capturing everything it prints, and writes that back in the
    /// colour its verdict earns.
    /// </summary>
    /// <remarks>
    /// When colour is off — redirected output, or <c>NO_COLOR</c> — the command runs untouched and
    /// nothing is buffered. That matters for more than tidiness: buffering changes *when* output
    /// appears, and a piped script must behave exactly as it always has.
    /// <para>
    /// The captured text is written in a <c>finally</c>, so a command that throws still shows what
    /// it had printed before it did. Losing that would make a crash much harder to diagnose than
    /// the uncoloured version was.
    /// </para>
    /// </remarks>
    public static void Run(Func<CommandVerdict> command)
    {
        // Output.ColourEnabled, not a local reading of the environment: it accounts for FORCE_COLOR
        // and for whether a transport could actually be established. Duplicating the rules here is
        // how buffering and colouring would come to disagree — buffering output that then prints
        // uncoloured, which only delays it for nothing.
        if (!Output.ColourEnabled)
        {
            command();
            return;
        }

        var buffer = new StringWriter();
        var previous = Console.Out;
        var verdict = CommandVerdict.Failed;

        // Buffering is exactly why a "still working" indicator belongs here: between pressing Enter
        // and the command finishing, NOTHING reaches the screen, however long it takes. Started
        // before the swap so it holds the real console to draw on, and disposed before the block is
        // written so its line is gone — and gone from the line the output is about to use — by the
        // time that output lands. It shows nothing at all unless the command outlasts the delay.
        var working = Spinner.StartAfter(Spinner.WorkingLabel, Spinner.Threshold);

        try
        {
            Console.SetOut(buffer);
            verdict = command();
        }
        finally
        {
            working.Dispose();

            Console.SetOut(previous);

            var text = buffer.ToString();

            if (text.Length > 0)
                Output.Block(Role(verdict), text);
        }
    }
}
