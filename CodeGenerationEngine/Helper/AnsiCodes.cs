namespace CodeGenerationEngine.Helper;

/// <summary>
/// The escape sequences for each output role, and their console-attribute equivalents.
/// </summary>
/// <remarks>
/// Both tables live together deliberately: two mechanisms delivering the same four roles must not
/// drift into disagreeing about what green means, and a test walks every role through both.
/// </remarks>
public static class AnsiCodes
{
    /// <summary>ESC, written as an escape so no source file carries a raw control character.</summary>
    private const char Escape = '\u001b';

    /// <summary>
    /// Resets everything rather than just the foreground.
    /// </summary>
    /// <remarks>
    /// <c>39</c> would restore the default foreground alone and leave anything else intact, which is
    /// tidier in principle. <c>0</c> is chosen because it is understood by every terminal that
    /// understands escape sequences at all, and nothing here ever sets bold or a background, so the
    /// broader reset costs nothing.
    /// </remarks>
    private static readonly string Reset = $"{Escape}[0m";

    /// <summary>The SGR sequence that starts <paramref name="role"/>, or null for plain text.</summary>
    public static string? Start(OutputRole role) => role switch
    {
        // Bright variants: the plain 3x codes are dim on many dark themes, and these match what
        // ConsoleColor.Green / Red / Yellow actually produce, keeping the two mechanisms aligned.
        OutputRole.Success => $"{Escape}[92m",
        OutputRole.Error => $"{Escape}[91m",
        OutputRole.Warning => $"{Escape}[93m",

        // Bright black — the conventional "dim" that is still legible.
        OutputRole.Detail => $"{Escape}[90m",
        _ => null,
    };

    /// <summary><paramref name="text"/> wrapped in <paramref name="role"/>'s sequence.</summary>
    /// <remarks>
    /// Unchanged for <see cref="OutputRole.Plain"/>, and for empty text — wrapping nothing in colour
    /// codes writes sequences a reader would see if their terminal did not understand them, for no
    /// benefit whatever.
    /// </remarks>
    public static string Wrap(string text, OutputRole role)
    {
        var start = Start(role);

        return start is null || text.Length == 0 ? text : start + text + Reset;
    }

    /// <summary>The console-attribute colour for <paramref name="role"/>, or null for plain.</summary>
    public static ConsoleColor? ConsoleColourFor(OutputRole role) => role switch
    {
        OutputRole.Success => ConsoleColor.Green,
        OutputRole.Error => ConsoleColor.Red,
        OutputRole.Warning => ConsoleColor.Yellow,
        OutputRole.Detail => ConsoleColor.DarkGray,
        _ => null,
    };

    /// <summary>
    /// <paramref name="text"/> with any SGR sequences removed.
    /// </summary>
    /// <remarks>
    /// For tests, and for anything that needs the words without the decoration. Hand-written rather
    /// than a regex because it runs over generated output in assertions and the grammar it needs to
    /// handle is exactly "ESC [ digits and semicolons m".
    /// </remarks>
    public static string Strip(string text)
    {
        if (!text.Contains(Escape))
            return text;

        var result = new System.Text.StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != Escape || i + 1 >= text.Length || text[i + 1] != '[')
            {
                result.Append(text[i]);
                continue;
            }

            // Skip to the terminating 'm', or to the end if the sequence is truncated.
            var end = i + 2;

            while (end < text.Length && text[end] != 'm')
                end++;

            i = end;
        }

        return result.ToString();
    }
}
