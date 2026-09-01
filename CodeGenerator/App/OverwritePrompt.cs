using AppScaffolding.Incremental;

namespace CodeGenerator.App;

/// <summary>What to do about a project folder that already holds a generated project.</summary>
internal enum OverwriteAction
{
    /// <summary>Regenerate in place, as every version before the guard existed did.</summary>
    Overwrite,

    /// <summary>Generate under a different name, leaving the existing project alone.</summary>
    Rename,

    /// <summary>Write nothing. The reviewed model survives, so the loop can be tried again.</summary>
    Cancel,

    /// <summary>
    /// Write only the classes this model has that the app does not, plus whatever has to
    /// change for them to appear. Every other generated file is left as it is.
    /// </summary>
    Add
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
            "a" or "add" => OverwriteAction.Add,
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

    /// <summary>
    /// As <see cref="Warning(string)"/>, also saying what this model has that the app does
    /// not — and offering to write just that.
    /// </summary>
    /// <remarks>
    /// <see cref="OverwriteAction.Add"/> appears only when something is new. An answer that
    /// would do nothing must not be on the menu; offering it would be a lie about what the
    /// tool can do.
    /// <para>
    /// A CHANGED class is named rather than glossed. Adding will not reconcile it and
    /// overwriting would, so someone choosing "add" has to be able to see that Customer stays
    /// as it is.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Warning(
        string projectFolder, IReadOnlyList<ClassDelta> delta)
    {
        var onDisk = delta.Count(d => d.Fate != ClassFate.New);
        var added = delta.Count(d => d.Fate == ClassFate.New);
        var changed = delta.Where(d => d.Fate == ClassFate.Changed).Select(d => d.Name).ToList();

        var lines = new List<string>
        {
            string.Empty,
            $"{projectFolder} already holds a generated project.",
            $"It has {onDisk} class(es); this model has {delta.Count}, of which {added} "
            + (added == 1 ? "is new." : "are new."),
        };

        if (changed.Count > 0)
        {
            lines.Add(
                $"{Classes(changed.Count)} differs: {string.Join(", ", changed)}. "
                + "Adding leaves them as they are.");
        }

        lines.AddRange(
        [
            string.Empty,
            "Regenerating overwrites its generated sources — Models, Data, Views and the",
            "*.Generated.cs validator halves. Your .Custom.cs rules, the project file and",
            "anything you added yourself are kept.",
            string.Empty,
            added > 0
                ? $"  [a]dd the {added} new   [o]verwrite all   [r]ename   [c]ancel   (Enter cancels)"
                : "  [o]verwrite   [r]ename   [c]ancel   (Enter cancels)",
        ]);

        return lines;
    }

    /// <summary>"1 class", "2 classes" — a line reading "1 classes" about a user's own model
    /// is the kind of detail that makes output look machine-made.</summary>
    private static string Classes(int n) => n == 1 ? "1 class" : $"{n} classes";
}
