using DomainDiscovery.Model;

namespace DomainDiscovery;

/// <summary>
/// What a discovery source produced, and which source produced it.
/// </summary>
/// <remarks>
/// Deliberately NOT <see cref="DiscoveryResult"/>. That type carries a <see cref="TopicResult"/>
/// whose <c>IsRecognized</c> means "has a Wikipedia article URL" — a question the LLM path can
/// never answer yes to, which would make every successful discovery report itself as a failure.
/// <para>
/// <see cref="SourceLabel"/> is here rather than inferred by the caller because the Discover panel
/// must state which source produced the classes, on success and on fallback alike.
/// </para>
/// </remarks>
public sealed record DiscoveryOutcome(
    CandidateDomainModel Model,
    string SourceLabel,
    bool Succeeded,
    string? Message);

/// <summary>
/// Composes the one user-facing line for a <see cref="DiscoveryOutcome"/> — shared so the CLI
/// (<c>BuildAppCommand</c>) and the web panel (<c>DiscoverStage</c>) cannot drift apart, as they
/// once did by each hand-composing the same three cases.
/// </summary>
public static class DiscoveryOutcomeDescription
{
    /// <summary>
    /// On success with no message: "Classes from {SourceLabel}." On success with a message (e.g.
    /// a fallback reason): that, plus " {Message}". On failure: "{Message} (Source: {SourceLabel})".
    /// </summary>
    public static string Describe(this DiscoveryOutcome outcome) => outcome.Succeeded
        ? string.IsNullOrWhiteSpace(outcome.Message)
            ? $"Classes from {outcome.SourceLabel}."
            : $"Classes from {outcome.SourceLabel}. {outcome.Message}"
        : $"{outcome.Message} (Source: {outcome.SourceLabel})";
}
