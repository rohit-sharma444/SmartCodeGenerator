using CodeGenerationEngine.Models;

namespace NaturalLanguageBridge;

/// <summary>
/// One line of a batch, together with the command it was understood to be.
/// </summary>
/// <param name="Sentence">
/// The raw text, trimmed. This — not <paramref name="Preview"/> — is what gets
/// executed, by being re-parsed against live state at execution time.
/// </param>
/// <param name="Intent">
/// The detected intent. <see cref="IntentType.Unknown"/> means unactionable.
/// </param>
/// <param name="Confidence">Detection confidence, 0..1.</param>
/// <param name="Preview">
/// The structured action shown to the user, or <see langword="null"/> when the
/// line is unactionable or parsing threw. For display only.
/// </param>
/// <param name="Note">Why the line is unactionable, when it is.</param>
public sealed record DetectedAction(
    string Sentence,
    IntentType Intent,
    double Confidence,
    ParsedCommand? Preview,
    string? Note)
{
    /// <summary>Whether this line may be approved and executed.</summary>
    public bool IsActionable => Intent != IntentType.Unknown;
}
