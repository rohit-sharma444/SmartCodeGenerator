using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using DomainDiscovery.Recognition;
using DomainDiscovery.SeedExtraction;
using DomainDiscovery.Wikipedia;
using NaturalLanguageEngine.Abstractions.Semantics;

namespace DomainDiscovery;

/// <summary>
/// Orchestrates discovery: NLE-extract seed entities from the user's text,
/// recognize the topic, then enrich from that article. No console I/O.
/// </summary>
public sealed class DomainDiscoveryPipeline
{
    private readonly ISemanticAnalyzer _analyzer;
    private readonly SeedEntityExtractor _seedExtractor;
    private readonly ITopicRecognizer _recognizer;
    private readonly IDomainEnricher _enricher;
    private readonly EntityShaper _shaper;

    /// <summary>
    /// Held so <see cref="Discover"/> can re-fetch the topic article's extract for
    /// <see cref="RelationshipMiner"/> after shaping, without the pipeline needing its own
    /// notion of "the enricher's client" — the same client the enricher already used, so the
    /// re-fetch lands on the disk cache <see cref="DomainEnricher"/> just populated.
    /// </summary>
    private readonly IWikipediaClient _wikipediaClient;

    private readonly RelationshipMiner _relationshipMiner = new();

    /// <param name="wikipediaClient">
    /// Used only after shaping, to re-read the topic article's extract for
    /// <see cref="RelationshipMiner"/> — mining needs the final, shaped class list (with
    /// each class's <see cref="Enrichment.EntityRole"/> resolved) and so cannot run any
    /// earlier than this. Pass the same client the enricher uses so this second read is a
    /// disk-cache hit, not a repeat network call.
    /// </param>
    public DomainDiscoveryPipeline(
        ISemanticAnalyzer analyzer,
        SeedEntityExtractor seedExtractor,
        ITopicRecognizer recognizer,
        IDomainEnricher enricher,
        EntityShaper shaper,
        IWikipediaClient wikipediaClient)
    {
        _analyzer = analyzer;
        _seedExtractor = seedExtractor;
        _recognizer = recognizer;
        _enricher = enricher;
        _shaper = shaper;
        _wikipediaClient = wikipediaClient;
    }

    public DiscoveryResult Discover(string freeText)
    {
        var analysis = _analyzer.Analyze(freeText ?? string.Empty);
        var seed = _seedExtractor.Extract(analysis);

        // Recognition searches on the seed names, so shaping — which decides what is a domain
        // class at all — has to wait until the article has been chosen.
        var topic = _recognizer.Recognize(seed.Entities.Select(e => e.Name));
        if (!topic.IsRecognized)
        {
            // No article was found, so there is nothing to mine relationships from — leave
            // the seed-only model exactly as Shape leaves it.
            _shaper.Shape(seed);
            return new DiscoveryResult(topic, seed);
        }

        var enriched = _enricher.Enrich(topic, seed);
        _shaper.Shape(enriched);

        // Mining runs after Shape: it needs the final class list and each class's Role (a
        // suggested or rejected class must never acquire a relationship), and it re-reads the
        // same article DomainEnricher already fetched — a disk-cache hit by now, not a second
        // network call (see HttpWikipediaClient.FetchExtract / WikipediaCache).
        _relationshipMiner.Mine(enriched, _wikipediaClient.FetchExtract(topic.Title));

        return new DiscoveryResult(topic, enriched);
    }

    /// <summary>
    /// Builds a pipeline wired to the real Wikipedia HTTP client (cached under
    /// <paramref name="cacheDirectory"/>) and the default NLE analyzer.
    /// </summary>
    public static DomainDiscoveryPipeline CreateDefault(string cacheDirectory)
    {
        var analyzer = EngineBuilder.BuildDefault().Analyzer;
        IWikipediaClient client = new HttpWikipediaClient(cacheDirectory);
        var harvester = new EntityHarvester();

        return new DomainDiscoveryPipeline(
            analyzer,
            new SeedEntityExtractor(),
            new WikipediaTopicRecognizer(client),
            new DomainEnricher(client, analyzer, harvester),
            new EntityShaper(client),
            client);
    }
}
