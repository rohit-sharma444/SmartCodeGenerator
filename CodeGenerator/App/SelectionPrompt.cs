namespace CodeGenerator.App;

/// <summary>
/// Paints a <see cref="SelectionState"/> and drives it from raw key presses. Enter commits, Esc
/// cancels.
/// </summary>
/// <remarks>
/// Deliberately thin and deliberately untested: it exists to talk to a real console, which is
/// exactly what a test cannot supply. Every decision it makes lives in
/// <see cref="SelectionState"/> instead. <see cref="CommandLineEditor"/> is untested for the same
/// reason.
/// </remarks>
internal static class SelectionPrompt
{
    /// <summary>The chosen index, or null if the user cancelled.</summary>
    public static int? Choose(string title, IReadOnlyList<string> labels, int defaultIndex)
    {
        var state = new SelectionState(labels, defaultIndex);

        Console.WriteLine();
        Console.WriteLine($"{title} (↑↓ then Enter, Esc to cancel):");
        Console.WriteLine();

        var top = Console.CursorTop;

        // Make room for the whole list before the first paint: if the cursor is already near the
        // bottom of the buffer (routine — the header lines above just scrolled it there), painting
        // without growing the buffer clips every row past the edge. Mirrors
        // CommandLineEditor.RewriteLine's guard for the same situation.
        var requiredHeight = top + labels.Count + 1;

        if (requiredHeight >= Console.BufferHeight && OperatingSystem.IsWindows())
            Console.BufferHeight = requiredHeight + 20;

        Paint(state, top);

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    // Leave the cursor below the list so whatever is written next does not
                    // overwrite the menu the user just answered.
                    MoveBelow(top, labels.Count);
                    return state.Selected;

                case ConsoleKey.Escape:
                    MoveBelow(top, labels.Count);
                    return null;

                default:
                    state.Move(key.Key);
                    Paint(state, top);
                    break;
            }
        }
    }

    private static void Paint(SelectionState state, int top)
    {
        var lines = state.Lines();

        for (var i = 0; i < lines.Count; i++)
        {
            var row = top + i;

            if (row >= Console.BufferHeight)
                break;

            Console.SetCursorPosition(0, row);

            // Blank the row first: the previous marker has to go, and the labels differ in length.
            Console.Write(new string(' ', Math.Max(0, Console.BufferWidth - 1)));
            Console.SetCursorPosition(0, row);

            // Clipped to the same width the blank above just cleared: an unclipped label longer
            // than the console would wrap onto the next list row and could scroll the buffer
            // mid-paint, invalidating the captured `top`. Shared with
            // CommandLineEditor.FormatSuggestionLine via ConsoleLine so there is one clipping rule.
            Console.Write(ConsoleLine.Clip(lines[i], Console.BufferWidth));
        }
    }

    private static void MoveBelow(int top, int count)
    {
        var row = Math.Min(top + count, Math.Max(0, Console.BufferHeight - 1));
        Console.SetCursorPosition(0, row);
        Console.WriteLine();
    }
}
