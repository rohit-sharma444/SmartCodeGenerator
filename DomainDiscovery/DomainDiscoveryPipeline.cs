using DomainDiscovery.Enrichment;
using DomainDiscovery.Model;
using DomainDiscovery.Recognition;
using DomainDiscovery.SeedExtraction;
using DomainDiscovery.Wikipedia;
using DomainDiscovery.Wikipedia.Dump;
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
        // class at all — has to wait until the article has been chosen. It gets the user's own
        // text as well: the domain is named by a sequence of those words ("point of sale"), and no
        // single seed name carries it.
        var topic = _recognizer.Recognize(seed.Entities.Select(e => e.Name), freeText);
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
    /// The dump store the pipeline uses when the caller names none. It is built once by
    /// WikipediaDumpImporter and is the normal source: local, deterministic, and free of the
    /// API's rate limit.
    /// </summary>
    public const string DefaultDumpDatabasePath =
        @"D:\SmartCodeGenerator-Repository\Dump\enwiki-full.db";

    /// <summary>
    /// Builds a pipeline over the local Wikipedia dump when its store exists, and over the
    /// MediaWiki API when it does not.
    /// </summary>
    /// <remarks>
    /// The dump is the intended source. <see cref="HttpWikipediaClient"/> survives only as the
    /// answer to "the store has not been imported yet", because failing outright would leave a
    /// fresh clone with no way to run `build app:` at all.
    /// </remarks>
    /// <param name="cacheDirectory">Disk cache for the HTTP fallback. Unused by the dump client.</param>
    /// <param name="dumpDatabasePath">
    /// The SQLite store to read. Defaults to <see cref="DefaultDumpDatabasePath"/>.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The store exists but carries no search index. See <see cref="RequireUsableStore"/>.
    /// </exception>
    public static DomainDiscoveryPipeline CreateDefault(
        string cacheDirectory, string? dumpDatabasePath = null)
    {
        var analyzer = EngineBuilder.BuildDefault().Analyzer;
        var databasePath = dumpDatabasePath ?? DefaultDumpDatabasePath;

        IWikipediaClient client;
        if (File.Exists(databasePath))
        {
            var store = new WikipediaStore(databasePath);
            RequireUsableStore(store, databasePath);
            client = new DumpWikipediaClient(store);
        }
        else
        {
            client = new HttpWikipediaClient(cacheDirectory);
        }

        var harvester = new EntityHarvester();

        return new DomainDiscoveryPipeline(
            analyzer,
            new SeedEntityExtractor(),
            new WikipediaTopicRecognizer(client),
            new DomainEnricher(client, analyzer, harvester),
            new EntityShaper(client),
            client);
    }

    /// <summary>
    /// Refuses a store that has rows but no search index, rather than reading it anyway.
    /// </summary>
    /// <remarks>
    /// An unindexed store is not slightly slower — it is unusable. Every lookup becomes a full scan
    /// of the whole table, and one discovery makes dozens: measured on the 18 GB store, a single
    /// exact read took 24 seconds and one prefix search took 17 minutes, so `build app:` simply
    /// never returned.
    /// <para>
    /// The store is left this way by an import — or an <c>--index-only</c> rebuild — that was
    /// killed before its last step, which is an ordinary thing to do to a run that takes hours. It
    /// used to fail silently, because nothing on the read path ever asked. Now it says so, and says
    /// what to run: the index can be rebuilt from the store alone, without decoding the dump again.
    /// </para>
    /// </remarks>
    private static void RequireUsableStore(WikipediaStore store, string databasePath)
    {
        if (store.HasSearchIndexes())
            return;

        throw new InvalidOperationException(
            $"""
            The Wikipedia store at {databasePath} has no search index, so every read would scan the
            whole table and discovery would take hours rather than seconds. An import that was
            stopped before its final step leaves it like this.

            Rebuild the index from the store as it stands (no need to decode the dump again):

                dotnet run --project WikipediaDumpImporter -c Release -- --index-only

            """);
    }
}
