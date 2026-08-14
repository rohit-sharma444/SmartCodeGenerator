namespace CodeGenerator.App;

/// <summary>
/// What an unexpected failure says to the user.
/// </summary>
/// <remarks>
/// This used to print the exception's type name — <c>Unexpected error
/// [InvalidOperationException]: …</c> — which tells a user nothing they can act on and reads like
/// a crash rather than a message. The type and stack are still available, behind
/// <see cref="VerboseVariable"/>, because whoever ends up diagnosing it does need them.
/// </remarks>
internal static class ErrorText
{
    /// <summary>Set to anything to get the type name and stack trace back. Follows the
    /// <c>CG_NO_LEARN</c> convention already used for opt-in behaviour.</summary>
    public const string VerboseVariable = "CG_VERBOSE";

    /// <summary>Whether the environment has asked for full detail.</summary>
    public static bool Verbose
        => Environment.GetEnvironmentVariable(VerboseVariable) is not null;

    public static IReadOnlyList<string> Lines(Exception exception, bool verbose)
    {
        var lines = new List<string> { $"Something went wrong: {Describe(exception)}" };

        if (!verbose)
        {
            // A dead end is what makes an error frustrating. Always say how to learn more.
            lines.Add($"  Set {VerboseVariable}=1 for the full detail.");
            return lines;
        }

        lines.Add($"  {exception.GetType().FullName}");

        // Null for an exception that was constructed but never thrown. Reporting a failure must
        // not itself fail.
        if (exception.StackTrace is { } stack)
            lines.Add(stack);

        return lines;
    }

    /// <summary>
    /// The exception's message, plus the innermost one when it differs.
    /// </summary>
    /// <remarks>
    /// The outer message is often the vague half ("An error occurred while…") and the inner is the
    /// one that says what actually happened — a missing file, a locked path. Showing only the outer
    /// throws away the useful part.
    /// </remarks>
    private static string Describe(Exception exception)
    {
        var innermost = exception;

        while (innermost.InnerException is { } inner)
            innermost = inner;

        return ReferenceEquals(innermost, exception)
               || string.Equals(innermost.Message, exception.Message, StringComparison.Ordinal)
            ? exception.Message
            : $"{exception.Message} — {innermost.Message}";
    }
}
