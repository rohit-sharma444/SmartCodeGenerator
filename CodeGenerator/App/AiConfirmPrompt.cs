using CodeIntelligence.Abstractions;

namespace CodeGenerator.App;

/// <summary>Asks the user to confirm an AI reading before it executes. Confirmation is
/// mandatory in phase A: a wrong silent resolution could mutate code, and the y/n
/// answers are the labelled data a later phase needs to calibrate a threshold.
///
/// Split the way <see cref="SelectionState"/> splits an arrow-key menu — everything
/// decidable (the wording, the default, what an answer means) is a pure member here and
/// is tested; only <see cref="Confirm"/> touches the console, and console-driving code
/// is hand-verified in this repo because a test cannot supply real key input.</summary>
internal static class AiConfirmPrompt
{
    internal static IReadOnlyList<string> Options { get; } = ["Yes, run it", "No, leave it"];

    /// <summary>Points at "No". A prompt that can execute a code-mutating command must
    /// not run it because someone hit Enter to dismiss the menu.</summary>
    internal const int DefaultIndex = 1;

    internal static string Title(AiResolution resolution)
    {
        var closest = resolution.Evidence.Count > 0 ? resolution.Evidence[0].Text : "(none)";

        return $"Not a known command. Closest match \"{closest}\" suggests: " +
               $"{resolution.Intent} ({resolution.Confidence:P0} confidence). Run it?";
    }

    /// <summary>What a menu answer means. <see cref="SelectionPrompt.Choose"/> returns
    /// null when the user pressed Esc or input ended, and only index 0 is a yes — so
    /// every other outcome, cancellation included, declines.</summary>
    internal static bool Accepted(int? choice) => choice == 0;

    internal static bool Confirm(AiResolution resolution)
        => Accepted(SelectionPrompt.Choose(Title(resolution), Options, DefaultIndex));
}
