namespace CodeGenerator.App;

/// <summary>
/// One entry in the review loop's action list: the verb its switch case matches, the row the
/// picker shows, and the arguments the loop collects before that verb can run.
/// </summary>
internal sealed record ReviewAction(
    string Verb,
    string Label,
    IReadOnlyList<string> ArgumentPrompts);

/// <summary>
/// The review loop's action catalog, and the bridge from a picked action back to the
/// <c>string[] parts</c> its switch already understands.
/// </summary>
/// <remarks>
/// Pure and tested for the same reason <see cref="BuildAppCommand.ResolveTarget"/> is: the loop
/// reads the console and cannot be driven from a test, so everything decidable is lifted out of
/// it. Composing <c>parts</c> — rather than formatting a line and re-splitting it — is what lets
/// the picker reuse the existing switch, and every helper below it, untouched.
/// </remarks>
internal static class ReviewActions
{
    public const string Title = "What next?";

    public static IReadOnlyList<ReviewAction> All { get; } =
    [
        new("approve", "approve   — generate the app", []),
        new("add",     "add       — add a class", ["New class name"]),
        new("remove",  "remove    — remove a class", ["Class to remove"]),
        new("rename",  "rename    — rename a class", ["Class to rename", "New name"]),
        new("field",   "field     — add a field to a class",
            ["Class", "Field, name or name:type"]),
        new("unfield", "unfield   — remove a field from a class",
            ["Class", "Field to remove"]),
        new("why",     "why       — explain how the model was decided",
            ["Class (blank for the whole report)"]),
        new("rules",   "rules     — the rules your data supports", []),
        new("accept",  "accept    — apply a proposed rule", ["Number, or 'all'"]),
        new("cancel",  "cancel    — nothing is generated", []),
    ];

    public static IReadOnlyList<string> Labels { get; } =
        All.Select(a => a.Label).ToList();

    /// <summary>
    /// The parts array the review loop's switch expects: the verb followed by the answers,
    /// each trimmed.
    /// </summary>
    /// <remarks>
    /// A trailing blank answer is dropped, which is how <c>why</c> with no name becomes the
    /// whole-report form <see cref="BuildAppCommand.WhyLines"/> already handles. No other action
    /// can reach here with a blank: the prompting layer abandons those instead of composing.
    /// </remarks>
    public static string[] Compose(ReviewAction action, IReadOnlyList<string> answers)
    {
        var parts = new List<string> { action.Verb };

        parts.AddRange(answers.Select(a => a.Trim()));

        while (parts.Count > 1 && parts[^1].Length == 0)
            parts.RemoveAt(parts.Count - 1);

        return [.. parts];
    }
}
