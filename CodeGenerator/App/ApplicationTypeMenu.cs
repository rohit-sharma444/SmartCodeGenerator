using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.App;

/// <summary>
/// The kinds of app <c>build app</c> offers, and how a choice becomes a target.
/// </summary>
/// <remarks>
/// Labels are display-only and ordered for a reader; <see cref="TargetScaffolders"/> keeps its own
/// order and its keys are what <c>approve &lt;target&gt;</c> accepts. Keeping the two separate is
/// what lets the menu read well without renaming anything scriptable.
/// </remarks>
internal static class ApplicationTypeMenu
{
    private sealed record Choice(string Label, string Key);

    private static readonly Choice[] Choices =
    [
        new("WPF (Windows desktop)", "wpf"),
        new("Windows Desktop (WinForms)", "winforms"),
        new("ASP.NET Web API", "webapi"),
        new("Console", "console"),
        new("Class Library", "classlib"),
        new("Worker Service (background)", "worker"),
    ];

    public const string Title = "Application type";

    public static IReadOnlyList<string> Labels { get; } =
        Choices.Select(c => c.Label).ToList();

    /// <summary>The target the row at <paramref name="index"/> selects.</summary>
    public static ITargetScaffolder Resolve(int index)
    {
        // An index outside the menu is a wiring bug rather than user input — every caller gets
        // its index from this same menu. Guard it explicitly so that bug surfaces as the
        // InvalidOperationException documented below, not an IndexOutOfRangeException.
        if (index < 0 || index >= Choices.Length)
            throw new InvalidOperationException($"No menu item at index {index}.");

        var key = Choices[index].Key;

        // Every key here is registered, so a miss is a wiring bug rather than user input.
        if (!TargetScaffolders.TryResolve(key, out var target))
            throw new InvalidOperationException($"No target registered for menu key '{key}'.");

        return target;
    }

    /// <summary>The menu label for a target, or its key if it is not on the menu.</summary>
    public static string LabelFor(ITargetScaffolder target)
        => Choices.FirstOrDefault(
               c => string.Equals(c.Key, target.Key, StringComparison.OrdinalIgnoreCase))
               ?.Label
           ?? target.Key;

    /// <summary>The row that selects <paramref name="target"/>, or 0 when it is not on the menu.
    /// Falls back rather than throwing, same tolerance as <see cref="LabelFor"/> — a target that
    /// slips off the menu still needs some row pre-selected.</summary>
    public static int IndexOf(ITargetScaffolder target)
    {
        var index = Array.FindIndex(
            Choices, c => string.Equals(c.Key, target.Key, StringComparison.OrdinalIgnoreCase));

        return index < 0 ? 0 : index;
    }

    /// <summary>
    /// The fallback prompt: a numbered list read a line at a time. Used when the console cannot
    /// be repainted (redirected output makes the cursor APIs throw), and testable for the same
    /// reason.
    /// </summary>
    /// <returns>The chosen index, or null to cancel.</returns>
    public static int? ChooseNumbered(TextReader reader, TextWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine(Title + ":");

        for (var i = 0; i < Choices.Length; i++)
            writer.WriteLine($"  {i + 1}) {Choices[i].Label}");

        while (true)
        {
            writer.Write($"Choose [1-{Choices.Length}] (default 1, c to cancel): ");

            var line = reader.ReadLine();

            // EOF is the absence of an answer, not the default answer.
            if (line is null)
                return null;

            var text = line.Trim();

            if (text.Length == 0)
                return 0;

            if (string.Equals(text, "c", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "cancel", StringComparison.OrdinalIgnoreCase))
                return null;

            if (int.TryParse(text, out var number) && number >= 1 && number <= Choices.Length)
                return number - 1;

            writer.WriteLine($"  Choose a number between 1 and {Choices.Length}, or c to cancel.");
        }
    }

    /// <summary>
    /// Asks for an application type the best way this console allows: an arrow-key list normally,
    /// the numbered prompt when input or output is redirected. <c>Console.SetCursorPosition</c>
    /// throws when output is redirected; <c>Console.ReadKey</c> throws when input is redirected.
    /// Matches the guard in <see cref="CommandLineEditor.ReadCommand"/>. Defaults to the first row.
    /// </summary>
    public static int? Choose()
        => Choose(0);

    /// <summary>
    /// Same as the parameterless <see cref="Choose()"/>, but with <paramref name="defaultIndex"/>
    /// pre-selected — how a target the wizard already settled stays chosen unless the user moves
    /// off it. The numbered fallback has no notion of a default row beyond "1", so a redirected
    /// console still goes through <see cref="ChooseNumbered"/> unchanged.
    /// </summary>
    public static int? Choose(int defaultIndex)
        => Console.IsInputRedirected || Console.IsOutputRedirected
            ? ChooseNumbered(Console.In, Console.Out)
            : SelectionPrompt.Choose(Title, Labels, defaultIndex);
}
