namespace NaturalLanguageBridge;

/// <summary>
/// The meaning of one answer at a batch approval prompt.
/// </summary>
public enum ApprovalReply
{
    /// <summary>Run this action.</summary>
    Approve,

    /// <summary>Skip this action, continue to the next.</summary>
    Reject,

    /// <summary>Abandon the whole batch without executing anything.</summary>
    Cancel,

    /// <summary>The answer was not understood; ask again.</summary>
    Reprompt
}

/// <summary>
/// Parses the user's answer at a batch approval prompt. Kept out of the CLI
/// project so it can be unit-tested: CodeGenerator.Tests cannot reference the
/// CodeGenerator executable.
/// </summary>
public static class ApprovalReplyParser
{
    /// <summary>
    /// Maps a raw console line to an <see cref="ApprovalReply"/>.
    /// </summary>
    /// <param name="line">
    /// The line read from the console, or <see langword="null"/> for EOF.
    /// </param>
    public static ApprovalReply Parse(string? line)
    {
        // null is EOF / closed stdin, which must abandon the batch rather than
        // spin the prompt forever.
        if (line is null)
            return ApprovalReply.Cancel;

        var trimmed = line.Trim();

        // An empty line re-prompts. It must never mean "approve".
        if (trimmed.Length == 0)
            return ApprovalReply.Reprompt;

        return trimmed.ToLowerInvariant() switch
        {
            "y" or "yes" => ApprovalReply.Approve,
            "n" or "no" => ApprovalReply.Reject,
            _ => ApprovalReply.Reprompt
        };
    }
}
