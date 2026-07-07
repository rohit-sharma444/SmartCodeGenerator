using DomainDiscovery.Model;
using DomainDiscovery.Wikipedia;
using NaturalLanguageEngine.Abstractions.Semantics;

namespace DomainDiscovery.Enrichment;

/// <summary>
/// Fetches the recognized article, runs it through the NLE, harvests a candidate
/// model, and merges it into the seed. Seed entities/fields win on conflict.
/// </summary>
public sealed class DomainEnricher : IDomainEnricher
{
    private readonly IWikipediaClient _client;
    private readonly ISemanticAnalyzer _analyzer;
    private readonly IEntityHarvester _harvester;

    public DomainEnricher(IWikipediaClient client, ISemanticAnalyzer analyzer, IEntityHarvester harvester)
    {
        _client = client;
        _analyzer = analyzer;
        _harvester = harvester;
    }

    public CandidateDomainModel Enrich(TopicResult topic, CandidateDomainModel seed)
    {
        var text = _client.FetchExtract(topic.Title);
        if (string.IsNullOrWhiteSpace(text))
            return seed;

        var harvested = _harvester.Harvest(_analyzer.Analyze(text));
        return Merge(seed, harvested);
    }

    private static CandidateDomainModel Merge(CandidateDomainModel seed, CandidateDomainModel harvested)
    {
        foreach (var incoming in harvested.Entities)
        {
            var existing = seed.Entities.FirstOrDefault(
                e => string.Equals(e.Name, incoming.Name, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                seed.Entities.Add(incoming);
                continue;
            }

            foreach (var field in incoming.Fields)
            {
                if (!existing.Fields.Any(f => string.Equals(f.Name, field.Name, StringComparison.OrdinalIgnoreCase)))
                    existing.Fields.Add(field);
            }
        }

        return seed;
    }
}
