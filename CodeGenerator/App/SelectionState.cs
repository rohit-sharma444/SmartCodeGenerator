namespace CodeGenerator.App;

/// <summary>
/// The decidable half of an arrow-key menu: which row is selected, how a key changes that, and
/// exactly what to paint. Holds no console reference, so the whole behaviour is testable — the
/// split <c>BuildAppCommand.ResolveTarget</c> and <c>DecisionReport</c> already use.
/// </summary>
internal sealed class SelectionState
{
    private const string SelectedPrefix = "  \u276f ";
    private const string UnselectedPrefix = "    ";

    private readonly IReadOnlyList<string> _labels;

    public SelectionState(IReadOnlyList<string> labels, int selected = 0)
    {
        _labels = labels;
        Selected = Math.Clamp(selected, 0, Math.Max(0, labels.Count - 1));
    }

    public int Selected { get; private set; }

    /// <summary>
    /// Moves the selection. Clamped rather than wrapping: overshooting the end and landing back
    /// at the start reads as a mis-press rather than a move.
    /// </summary>
    public void Move(ConsoleKey key)
    {
        Selected = key switch
        {
            ConsoleKey.UpArrow => Math.Max(0, Selected - 1),
            // Max(0, ...) on the outside too: on an empty list, _labels.Count - 1 is -1, and
            // without the outer clamp this would return -1 instead of staying at the invariant
            // the constructor establishes (Selected is never negative).
            ConsoleKey.DownArrow => Math.Max(0, Math.Min(_labels.Count - 1, Selected + 1)),
            _ => Selected,
        };
    }

    /// <summary>One line per label. Both prefixes are the same width, so the labels stay aligned
    /// as the marker moves.</summary>
    public IReadOnlyList<string> Lines()
        => _labels
            .Select((label, index) =>
                (index == Selected ? SelectedPrefix : UnselectedPrefix) + label)
            .ToList();
}
