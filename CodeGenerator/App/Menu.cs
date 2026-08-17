namespace CodeGenerator.App;

/// <summary>
/// Asks a question with a fixed set of answers, the best way this console allows.
/// </summary>
/// <remarks>
/// One place, because there are now a dozen such questions — the application type, the layout, the
/// theme, every yes/no in the customization step, the data format — and a menu that behaves
/// differently from the menu before it is worse than no menu at all.
/// <para>
/// An arrow-key list needs a real console: <c>Console.SetCursorPosition</c> throws when output is
/// redirected and <c>Console.ReadKey</c> throws when input is. So a redirected run gets the
/// numbered list instead, which is also the only form a test can drive — the same split
/// <see cref="CommandLineEditor.ReadCommand"/> makes.
/// </para>
/// </remarks>
internal static class Menu
{
    /// <summary>
    /// The chosen row, or null if the user cancelled.
    /// </summary>
    /// <param name="defaultIndex">
    /// The row the user already has. Pre-selected on the list and taken by a blank line at the
    /// numbered prompt, so Enter means "keep what I had" in both forms.
    /// </param>
    public static int? Choose(string title, IReadOnlyList<string> labels, int defaultIndex)
        => Console.IsInputRedirected || Console.IsOutputRedirected
            ? ChooseNumbered(Console.In, Console.Out, title, labels, defaultIndex)
            : SelectionPrompt.Choose(title, labels, Clamp(defaultIndex, labels.Count));

    /// <summary>
    /// The fallback prompt: a numbered list read a line at a time.
    /// </summary>
    /// <returns>The chosen row, or null to cancel.</returns>
    public static int? ChooseNumbered(
        TextReader reader, TextWriter writer,
        string title, IReadOnlyList<string> labels, int defaultIndex)
    {
        var fallback = Clamp(defaultIndex, labels.Count);

        writer.WriteLine();
        writer.WriteLine(title + ":");

        for (var i = 0; i < labels.Count; i++)
            writer.WriteLine($"  {i + 1}) {labels[i]}");

        while (true)
        {
            writer.Write($"Choose [1-{labels.Count}] (default {fallback + 1}, c to cancel): ");

            var line = reader.ReadLine();

            // EOF is the absence of an answer, not the default answer.
            if (line is null)
                return null;

            var text = line.Trim();

            if (text.Length == 0)
                return fallback;

            if (string.Equals(text, "c", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "cancel", StringComparison.OrdinalIgnoreCase))
                return null;

            if (int.TryParse(text, out var number) && number >= 1 && number <= labels.Count)
                return number - 1;

            writer.WriteLine($"  Choose a number between 1 and {labels.Count}, or c to cancel.");
        }
    }

    /// <summary>
    /// <paramref name="index"/> if it names a row, and the first row otherwise.
    /// </summary>
    /// <remarks>
    /// Defaults are computed from a value the user already holds, which may no longer be on the
    /// menu — a layout that was dropped, a target read out of a saved profile. Some row has to be
    /// pre-selected either way, and the first is the one every caller's list starts with.
    /// </remarks>
    private static int Clamp(int index, int count)
        => index < 0 || index >= count ? 0 : index;
}
