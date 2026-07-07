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

    public DomainDiscoveryPipeline(
        ISemanticAnalyzer analyzer,
        SeedEntityExtractor seedExtractor,
        ITopicRecognizer recognizer,
        IDomainEnricher enricher)
    {
        _analyzer = analyzer;
        _seedExtractor = seedExtractor;
        _recognizer = recognizer;
        _enricher = enricher;
    }

    public DiscoveryResult Discover(string freeText)
    {
        var analysis = _analyzer.Analyze(freeText ?? string.Empty);
        var seed = _seedExtractor.Extract(analysis);

        var topic = _recognizer.Recognize(seed.Entities.Select(e => e.Name));
        if (!topic.IsRecognized)
            return new DiscoveryResult(topic, seed);

        var enriched = _enricher.Enrich(topic, seed);
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
            new DomainEnricher(client, analyzer, harvester));
    }
}
