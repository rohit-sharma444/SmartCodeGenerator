using DomainDiscovery.Model;

namespace DomainDiscovery;

/// <summary>The recognized topic plus the (possibly enriched) candidate model.</summary>
public sealed record DiscoveryResult(TopicResult Topic, CandidateDomainModel Model);
