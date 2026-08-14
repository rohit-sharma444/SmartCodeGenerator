namespace CodeGenerator.App;

/// <summary>What to do about a project folder that already holds a generated project.</summary>
internal enum OverwriteAction
{
    /// <summary>Regenerate in place, as every version before the guard existed did.</summary>
    Overwrite,

    /// <summary>Generate under a different name, leaving the existing project alone.</summary>
    Rename,

    /// <summary>Write nothing. The reviewed model survives, so the loop can be tried again.</summary>
    Cancel
}

/// <summary>
/// The question asked before a second <c>build app</c> writes over the first one's sources.
/// </summary>
/// <remarks>
/// Pure and tested, like <c>BuildReport</c> and <c>NextSteps</c>: the console loop that asks it
/// cannot be driven from a test, so what each answer means lives here instead.
/// </remarks>
internal static class OverwritePrompt
{
    /// <summary>
    /// The action <paramref name="answer"/> selects, or null if it is not an answer at all and
    /// the question should be put again.
    /// </summary>
    /// <remarks>
    /// Blank and end-of-input both cancel. The effortless answer has to be the safe one: a user
    /// who has stopped reading and hits Enter must not thereby overwrite a project, and a closed
    /// pipe is the absence of an answer rather than consent. Everything unrecognized returns
    /// null rather than falling back to a default, because a default here would make a typo
    /// destructive.
    /// </remarks>
    public static OverwriteAction? Parse(string? answer)
    {
        var text = answer?.Trim() ?? string.Empty;

        if (text.Length == 0)
            return OverwriteAction.Cancel;

        return text.ToLowerInvariant() switch
        {
            "o" or "overwrite" => OverwriteAction.Overwrite,
            "r" or "rename" => OverwriteAction.Rename,
            "c" or "cancel" => OverwriteAction.Cancel,
            _ => null
        };
    }

    /// <summary>
    /// What the user is being asked, and what each answer costs them.
    /// </summary>
    /// <remarks>
    /// Says what survives as well as what is lost. Without that the honest answer to the question
    /// is unknowable, and a user would cancel regenerations that were perfectly safe — which is
    /// how a guard stops being read and starts being dismissed.
    /// </remarks>
    public static IReadOnlyList<string> Warning(string projectFolder) =>
    [
        string.Empty,
        $"{projectFolder} already holds a generated project.",
        "Regenerating overwrites its generated sources — Models, Data, Views and the",
        "*.Generated.cs validator halves. Your .Custom.cs rules, the project file and",
        "anything you added yourself are kept.",
        string.Empty,
        "  [o]verwrite   [r]ename   [c]ancel   (Enter cancels)"
    ];
}
