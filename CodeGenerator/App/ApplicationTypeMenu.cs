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
        new("Windows Service", "worker"),
    ];

    public const string Title = "Application type";

    public static IReadOnlyList<string> Labels { get; } =
        Choices.Select(c => c.Label).ToList();

    /// <summary>The target the row at <paramref name="index"/> selects.</summary>
    public static ITargetScaffolder Resolve(int index)
    {
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
}
