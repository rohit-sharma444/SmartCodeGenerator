using CodeIntelligence.Abstractions;

namespace CodeGenerator.App;

/// <summary>Asks the user to confirm an AI reading before it executes. Confirmation is
/// mandatory in phase A: a wrong silent resolution could mutate code, and the y/n
/// answers are the labelled data a later phase needs to calibrate a threshold.</summary>
internal static class AiConfirmPrompt
{
    internal static bool Confirm(AiResolution resolution)
    {
        var closest = resolution.Evidence.Count > 0 ? resolution.Evidence[0].Text : "(none)";

        var title =
            $"Not a known command. Closest match \"{closest}\" suggests: " +
            $"{resolution.Intent} ({resolution.Confidence:P0} confidence). Run it?";

        // Choose returns int?, null when the user pressed Esc or input ended. Comparing
        // a nullable to 0 yields false for null, so cancelling declines — which is the
        // right default for a prompt that can execute a code-mutating command.
        var choice = SelectionPrompt.Choose(title, ["Yes, run it", "No, leave it"], defaultIndex: 1);

        return choice == 0;
    }
}
