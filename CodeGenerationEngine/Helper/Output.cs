namespace CodeGenerationEngine.Helper;

/// <summary>The four things output can be, plus plain.</summary>
public enum OutputRole
{
    /// <summary>The terminal's own colour.</summary>
    Plain,

    Success,

    Error,

    Warning,

    Detail
}

/// <summary>
/// The CLI's four output roles.
/// </summary>
/// <remarks>
/// Colour is set through <see cref="Console.ForegroundColor"/> — the console API — and never
/// written into the stream as escape codes. Together with switching off entirely for redirected
/// output, that is what makes this safe to add: a captured or piped transcript is byte-for-byte
/// what it was before colour existed, so no test that reads console output needs to change.
/// <para>
/// Lives in the engine, not the CLI, because most of the messages a user reads are printed from
/// here — the CLI cannot be the owner of something CodeGenerationEngine needs to call.
/// <para>
/// Four roles and no more. A screen painted in six colours reads as less considered, not more, so
/// the discipline is the point: anything that is not success, failure, a warning or secondary
/// detail goes through <see cref="Line"/> and stays the terminal's own colour.
/// </para>
/// </remarks>
public static class Output
{
    /// <summary>The no-color.org convention's variable name.</summary>
    private const string NoColourVariable = "NO_COLOR";

    /// <summary>
    /// Whether colour may be used, given the two things that veto it.
    /// </summary>
    /// <remarks>
    /// Takes its inputs as parameters rather than reading the console and environment itself, so
    /// the rules can be tested — the test host redirects output, which would otherwise make every
    /// case look the same.
    /// <para>
    /// <paramref name="noColourVariable"/> vetoes on <b>presence</b>, not value: per the
    /// convention, <c>NO_COLOR=0</c> and <c>NO_COLOR=false</c> still mean no colour. Only an unset
    /// variable (null) permits it.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Whether colour is on, and if not, why — for <c>status</c> to report.
    /// </summary>
    /// <remarks>
    /// Exists because "I can't see coloured text" is a question the tool should answer about itself.
    /// Both reasons are invisible from the outside: a user cannot tell that their shell sets
    /// <c>NO_COLOR</c>, nor that whatever launched the CLI gave it a pipe instead of a console.
    /// </remarks>
    public static string Explain(bool outputRedirected, string? noColourVariable)
    {
        if (noColourVariable is not null)
            return $"off (NO_COLOR is set to '{noColourVariable}')";

        return outputRedirected
            ? "off (output is redirected, so a transcript stays plain text)"
            : "on";
    }

    /// <summary>As <see cref="Explain(bool, string?)"/>, for the console this process actually has.</summary>
    public static string Explain()
        => Explain(
            Console.IsOutputRedirected,
            Environment.GetEnvironmentVariable(NoColourVariable));

    public static bool ColourAllowed(bool outputRedirected, string? noColourVariable)
        => !outputRedirected && noColourVariable is null;

    private static bool Allowed
        => ColourAllowed(
            Console.IsOutputRedirected,
            Environment.GetEnvironmentVariable(NoColourVariable));

    /// <summary>Something worked: a green build, a project written.</summary>
    public static void Success(string text) => Write(text, ConsoleColor.Green);

    /// <summary>Something failed.</summary>
    public static void Error(string text) => Write(text, ConsoleColor.Red);

    /// <summary>Nothing failed, but the user needs to know: a missing store, an ignored option.</summary>
    public static void Warning(string text) => Write(text, ConsoleColor.Yellow);

    /// <summary>
    /// Secondary text — a path, a count, a label.
    /// </summary>
    /// <remarks>
    /// Used sparingly and never for anything the user must read: dark grey is close to invisible
    /// on some terminal themes. Anything essential goes through <see cref="Line"/> instead.
    /// </remarks>
    public static void Detail(string text) => Write(text, ConsoleColor.DarkGray);

    /// <summary>Ordinary output, in whatever colour the terminal already is.</summary>
    public static void Line(string text) => Console.WriteLine(text);

    /// <summary>
    /// Writes text that may span many lines in a single colour, exactly as given.
    /// </summary>
    /// <remarks>
    /// For colouring a whole command's output by its result: the caller has buffered everything the
    /// command printed and now knows the verdict. Written with <see cref="Console.Write(string)"/>
    /// rather than WriteLine because the captured text already carries its own line breaks — adding
    /// one would insert a blank line the command never printed.
    /// </remarks>
    public static void Block(OutputRole role, string text)
    {
        var colour = role switch
        {
            OutputRole.Success => ConsoleColor.Green,
            OutputRole.Error => ConsoleColor.Red,
            OutputRole.Warning => ConsoleColor.Yellow,
            OutputRole.Detail => ConsoleColor.DarkGray,
            _ => (ConsoleColor?)null,
        };

        if (colour is null || !Allowed)
        {
            Console.Write(text);
            return;
        }

        var previous = Console.ForegroundColor;

        try
        {
            Console.ForegroundColor = colour.Value;
            Console.Write(text);
        }
        finally
        {
            Console.ForegroundColor = previous;
        }
    }

    /// <summary>
    /// A message whose colour follows an outcome the caller already knows.
    /// </summary>
    /// <remarks>
    /// Several handlers receive <c>(bool ok, string message)</c> from a service and print the
    /// message unconditionally. Branching at each of those sites would be the same three lines
    /// repeated; this keeps the decision in one place and makes it obvious that a failure here is a
    /// <see cref="Warning"/> rather than an <see cref="Error"/> — the operation did not happen, but
    /// nothing is broken (a class that was not there to delete, for instance).
    /// </remarks>
    public static void Result(bool ok, string message)
    {
        if (ok)
            Success(message);
        else
            Warning(message);
    }

    private static void Write(string text, ConsoleColor colour)
    {
        if (!Allowed)
        {
            Console.WriteLine(text);
            return;
        }

        // The previous colour is restored rather than ResetColor()'d: a terminal with a
        // non-default foreground would otherwise drift to the system default, one line at a time.
        var previous = Console.ForegroundColor;

        try
        {
            Console.ForegroundColor = colour;
            Console.WriteLine(text);
        }
        finally
        {
            Console.ForegroundColor = previous;
        }
    }
}
