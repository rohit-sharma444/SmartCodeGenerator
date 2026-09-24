namespace CodeGenerator.App;

/// <summary>
/// The prompt under the candidate model, in a short form and a full one.
/// </summary>
/// <remarks>
/// This used to be one 140-character line naming all eight verbs with their argument shapes,
/// reprinted after every action. It is therefore the text a user looks at more than any other in
/// the tool, and it wrapped on any normal terminal — so the four that most runs need go on one
/// line and the rest are one keystroke away.
/// </remarks>
internal static class ReviewHints
{
    /// <summary>What is shown every time.</summary>
    public static IReadOnlyList<string> Short { get; } =
    [
        "  [a]pprove   add <name>   remove <name>   [c]ancel        ? for all actions",
    ];

    /// <summary>Everything the loop accepts, shown on request.</summary>
    /// <remarks>
    /// Grouped rather than run together: the pairs that take two arguments are the ones nobody
    /// guesses, and they are easier to read on their own lines than buried mid-sentence.
    /// </remarks>
    public static IReadOnlyList<string> Full { get; } =
    [
        "  approve [target] [sqlite]   generate the app",
        "  cancel                      generate nothing",
        "  add <name>                  add a class",
        "  remove <name>               remove a class",
        "  rename <old> <new>          rename a class",
        "  field <class> <name>[:<type>]   add a field (type inferred if omitted)",
        "  unfield <class> <name>      remove a field",
        "  renamefield <class> <old> <new>   rename a field, keeping its rules",
        "  retype <class> <field> <type>     change a field's type",
        "  why [name]                  explain what discovery decided",
        "  rules                       the rules your data supports",
        "  accept <n>|all              apply a proposed rule",
        "  unaccept <n>|all            take an applied rule back",
        "  order status <v1,v2,...> in <Class>   state a status column's transition order",
        "  final status <v1,v2,...> in <Class>   mark one or more values as final",
    ];

    /// <summary>Whether <paramref name="input"/> is asking for the full list.</summary>
    /// <remarks>
    /// <c>help</c> as well as <c>?</c>: it is what a user types when a prompt has not told them
    /// enough, and in this loop it collides with nothing.
    /// </remarks>
    public static bool IsMoreRequest(string? input)
    {
        var text = (input ?? string.Empty).Trim();

        return text == "?" || text.Equals("help", StringComparison.OrdinalIgnoreCase);
    }
}
