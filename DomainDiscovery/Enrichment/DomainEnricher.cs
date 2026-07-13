using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;
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

        // Wikilinks name the multi-word concepts prose-noun frequency structurally cannot
        // see: "cash register" tokenizes into two common nouns that normalize to junk.
        foreach (var title in LinkedConceptSource.Candidates(_client.FetchLinks(topic.Title), text))
        {
            var name = NameNormalizer.ToCompoundEntityName(title);

            if (!NameNormalizer.IsValidIdentifier(name))
                continue;

            if (harvested.Entities.Any(
                    e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                continue;

            harvested.Entities.Add(new CandidateEntity
            {
                Name = name,
                SourceTitle = title,
                Provenance = Provenance.Wiki,
                Confidence = LinkConfidence
            });
        }

        return Merge(seed, harvested);
    }

    /// <summary>
    /// A linked concept is a real, editor-disambiguated topic, but it carries no mention
    /// count of its own, so it ranks below the article's most-repeated nouns.
    /// </summary>
    private const double LinkConfidence = 0.5;

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
