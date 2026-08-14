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
/// Colour is delivered by whichever mechanism the host understands — see
/// <see cref="ColourDelivery"/>. ANSI escape sequences where virtual-terminal processing is
/// available (every modern terminal, and anything reading a pipe that renders them), Windows
/// console attributes otherwise, and nothing at all when colour is switched off.
/// <para>
/// Lives in the engine, not the CLI, because most of the messages a user reads are printed from
/// here — the CLI cannot be the owner of something CodeGenerationEngine needs to call.
/// </para>
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
    /// Asks for colour even when output is redirected.
    /// </summary>
    /// <remarks>
    /// The standard opt-in for a host that captures the stream and renders the escape sequences
    /// itself — the only way the VS Code extension panel could ever show colour, since it spawns the
    /// CLI with a pipe and console attributes cannot cross that boundary.
    /// </remarks>
    private const string ForceColourVariable = "FORCE_COLOR";

    /// <summary>
    /// Whether colour may be used at all.
    /// </summary>
    /// <remarks>
    /// Takes its inputs as parameters rather than reading the console and environment itself, so the
    /// rules can be tested — the test host redirects output, which would otherwise make every case
    /// look the same.
    /// <para>
    /// <paramref name="noColourVariable"/> vetoes on <b>presence</b>, not value: per the convention,
    /// <c>NO_COLOR=0</c> and <c>NO_COLOR=false</c> still mean no colour. It also beats
    /// <paramref name="forceColourVariable"/> — asking for no colour is the stronger request, being
    /// the one a person makes to fix something they are looking at.
    /// </para>
    /// </remarks>
    public static bool ColourAllowed(
        bool outputRedirected, string? noColourVariable, string? forceColourVariable = null)
    {
        if (noColourVariable is not null)
            return false;

        return !outputRedirected || forceColourVariable is not null;
    }

    internal static bool Allowed
        => ColourAllowed(
            Console.IsOutputRedirected,
            Environment.GetEnvironmentVariable(NoColourVariable),
            Environment.GetEnvironmentVariable(ForceColourVariable));

    /// <summary>Whether colour was explicitly asked for despite redirection.</summary>
    internal static bool ColourForced
        => Environment.GetEnvironmentVariable(NoColourVariable) is null
           && Environment.GetEnvironmentVariable(ForceColourVariable) is not null;

    /// <summary>
    /// Whether anything will actually be coloured — the question a caller needs before deciding to
    /// buffer output in order to colour it as one block.
    /// </summary>
    public static bool ColourEnabled => ColourDelivery.Current != ColourTransport.None;

    /// <summary>
    /// Whether colour is on, how it is delivered, and if it is off, why — for <c>status</c>.
    /// </summary>
    /// <remarks>
    /// Names the transport as well as the state. "on" alone was not enough to diagnose a terminal
    /// that reported colour as enabled and still rendered white; whether the tool is sending escape
    /// sequences or setting console attributes is the missing half of that answer.
    /// </remarks>
    /// <param name="transport">
    /// Passed in rather than read from <see cref="ColourDelivery.Current"/>, which is decided once
    /// per process. Reading it here would let the explanation contradict its own arguments — saying
    /// colour is on, from parameters, while reporting no transport because the real process is
    /// redirected. A test caught exactly that.
    /// </param>
    public static string Explain(
        bool outputRedirected,
        string? noColourVariable,
        string? forceColourVariable,
        ColourTransport transport)
    {
        if (noColourVariable is not null)
            return $"off (NO_COLOR is set to '{noColourVariable}')";

        if (outputRedirected && forceColourVariable is null)
            return $"off (output is redirected; set {ForceColourVariable} to override)";

        return transport switch
        {
            ColourTransport.Ansi => "on (ANSI escape sequences)",
            ColourTransport.ConsoleAttributes => "on (Windows console attributes)",
            _ => "off (no way to deliver colour to this host)",
        };
    }

    /// <summary>As above, for this process's console.</summary>
    public static string Explain()
        => Explain(
            Console.IsOutputRedirected,
            Environment.GetEnvironmentVariable(NoColourVariable),
            Environment.GetEnvironmentVariable(ForceColourVariable),
            ColourDelivery.Current);

    /// <summary>Something worked: a class written, a rule registered, a green build.</summary>
    public static void Success(string text) => WriteLine(text, OutputRole.Success);

    /// <summary>Something failed.</summary>
    public static void Error(string text) => WriteLine(text, OutputRole.Error);

    /// <summary>Nothing failed, but the user needs to know: a missing store, an unknown command.</summary>
    public static void Warning(string text) => WriteLine(text, OutputRole.Warning);

    /// <summary>
    /// Secondary text — a path, a count, a label.
    /// </summary>
    /// <remarks>
    /// Used sparingly and never for anything the user must read: dark grey is close to invisible on
    /// some terminal themes. Anything essential goes through <see cref="Line"/> instead.
    /// </remarks>
    public static void Detail(string text) => WriteLine(text, OutputRole.Detail);

    /// <summary>Ordinary output, in whatever colour the terminal already is.</summary>
    public static void Line(string text) => Console.WriteLine(text);

    /// <summary>
    /// A message whose colour follows an outcome the caller already knows.
    /// </summary>
    /// <remarks>
    /// Several handlers receive <c>(bool ok, string message)</c> from a service and print the message
    /// unconditionally. A failure here is a <see cref="Warning"/> rather than an <see cref="Error"/>:
    /// the operation did not happen — no such class to delete, say — but nothing is broken.
    /// </remarks>
    public static void Result(bool ok, string message)
    {
        if (ok)
            Success(message);
        else
            Warning(message);
    }

    /// <summary>
    /// Writes text that may span many lines in a single colour, exactly as given.
    /// </summary>
    /// <remarks>
    /// For colouring a whole command's output by its result: the caller has buffered everything the
    /// command printed and now knows the verdict. Written without an added line break because the
    /// captured text already carries its own — appending one would insert a blank line the command
    /// never printed.
    /// </remarks>
    public static void Block(OutputRole role, string text) => Write(text, role, newLine: false);

    private static void WriteLine(string text, OutputRole role) => Write(text, role, newLine: true);

    private static void Write(string text, OutputRole role, bool newLine)
    {
        switch (ColourDelivery.Current)
        {
            case ColourTransport.Ansi:
                WritePlain(AnsiCodes.Wrap(text, role), newLine);
                return;

            case ColourTransport.ConsoleAttributes:
                WriteWithAttributes(text, role, newLine);
                return;

            default:
                WritePlain(text, newLine);
                return;
        }
    }

    private static void WritePlain(string text, bool newLine)
    {
        if (newLine)
            Console.WriteLine(text);
        else
            Console.Write(text);
    }

    private static void WriteWithAttributes(string text, OutputRole role, bool newLine)
    {
        var colour = AnsiCodes.ConsoleColourFor(role);

        if (colour is null)
        {
            WritePlain(text, newLine);
            return;
        }

        // The previous colour is restored rather than ResetColor()'d: a terminal with a non-default
        // foreground would otherwise drift to the system default, one line at a time.
        var previous = Console.ForegroundColor;

        try
        {
            Console.ForegroundColor = colour.Value;
            WritePlain(text, newLine);
        }
        finally
        {
            Console.ForegroundColor = previous;
        }
    }
}
