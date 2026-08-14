namespace CodeGenerator.App;

/// <summary>What a bare command-line flag asked for.</summary>
internal enum StartupFlag
{
    /// <summary>Not a flag — treat the arguments as a command, as before.</summary>
    None,

    Version,

    Help
}

/// <summary>
/// Recognizes <c>--version</c>, <c>--help</c> and their usual spellings before the arguments reach
/// the command parser.
/// </summary>
/// <remarks>
/// <c>Program.cs</c> joins its arguments into one command string, so without this
/// <c>CodeGenerator --version</c> fell through to the parser and answered "Command not
/// recognized" — the two most universal conventions in command-line tools, unanswered, and more
/// glaring once the tool had a version worth reporting.
/// </remarks>
internal static class StartupFlags
{
    private static readonly HashSet<string> VersionFlags =
        new(StringComparer.OrdinalIgnoreCase) { "--version", "-v", "/version", "/v", "version" };

    private static readonly HashSet<string> HelpFlags =
        new(StringComparer.OrdinalIgnoreCase) { "--help", "-h", "-?", "/?", "/help", "/h" };

    /// <summary>
    /// The flag <paramref name="args"/> is, or <see cref="StartupFlag.None"/>.
    /// </summary>
    /// <remarks>
    /// Only a lone argument counts. <c>build app: the --version endpoint</c> is a description that
    /// happens to contain a flag, and intercepting it would break a legitimate command — so the
    /// whole invocation has to be the flag and nothing else.
    /// <para>
    /// Bare <c>help</c> is deliberately absent from <see cref="HelpFlags"/>: it is a real command
    /// and <c>HelpCommand</c> already handles it, including <c>help &lt;topic&gt;</c>, which this
    /// would flatten to the overview.
    /// </para>
    /// </remarks>
    public static StartupFlag Parse(IReadOnlyList<string> args)
    {
        if (args.Count != 1)
            return StartupFlag.None;

        var argument = args[0].Trim();

        if (VersionFlags.Contains(argument))
            return StartupFlag.Version;

        return HelpFlags.Contains(argument) ? StartupFlag.Help : StartupFlag.None;
    }

    /// <summary>
    /// The bare version, with nothing around it.
    /// </summary>
    /// <remarks>
    /// Scripts read this. A banner or a label would have to be stripped by every caller, so the
    /// convention is the version alone on one line.
    /// </remarks>
    public static string VersionText() => VersionInfo.Current;

    /// <summary>The same overview the prompt's <c>help</c> gives — one question, one answer.</summary>
    public static string HelpText() => HelpCommand.Render("help");
}
