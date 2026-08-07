using CodeIntelligence.Abstractions;
using CodeIntelligence.Storage;

namespace CodeGenerator.App;

/// <summary>Asks for confirmation and records the answer.
///
/// Lifted out of <c>CodeGeneratorStarter</c> so the recording half is testable without a
/// console: the claim that the decision log is written WHICHEVER way the user answers is
/// the whole basis for calibrating a later auto-execute threshold, and a decline is as
/// informative as an accept. Left inside a private method it could only ever be verified
/// by hand, alongside the menu painting that genuinely needs a human.</summary>
internal static class AiDecisionRecorder
{
    internal static bool ConfirmAndRecord(
        AiResolution resolution, string input, DecisionLog log, Func<AiResolution, bool> confirm)
    {
        var accepted = confirm(resolution);
        log.Append(input, resolution, accepted);
        return accepted;
    }
}
