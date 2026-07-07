using DomainDiscovery.Model;

namespace DomainDiscovery.Enrichment;

public interface IDomainEnricher
{
    CandidateDomainModel Enrich(TopicResult topic, CandidateDomainModel seed);
}
