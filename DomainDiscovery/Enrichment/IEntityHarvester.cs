using DomainDiscovery.Model;
using NaturalLanguageEngine.Models.Semantics;

namespace DomainDiscovery.Enrichment;

public interface IEntityHarvester
{
    CandidateDomainModel Harvest(SemanticResult analysis);
}
