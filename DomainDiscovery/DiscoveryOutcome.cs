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
