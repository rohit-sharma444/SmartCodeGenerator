using DomainDiscovery.Llm;
using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;

namespace DomainDiscovery.Bundled;

/// <summary>
/// Discovery with nothing installed and no network: the built-in domain the description is about,
/// plus any class the user named that the domain lacks.
/// </summary>
/// <remarks>
/// No match is not an error, it is an unsuccessful outcome carrying the user's own classes, so
/// whatever comes after this in the fallback chain still runs — and if nothing does, the review
/// opens on what the user said rather than on nothing.
/// </remarks>
public sealed class BundledDomainSource : IDomainModelSource
{
    public const string NoMatchLabel = "the built-in domains";

    private const int NameWeight = 3;

    private readonly BundledDomainCatalog _catalog;
    private readonly Func<string, CandidateDomainModel> _seeds;

    public BundledDomainSource(BundledDomainCatalog catalog, Func<string, CandidateDomainModel> seeds)
    {
        _catalog = catalog;
        _seeds = seeds;
    }

    /// <summary>
    /// The embedded catalog, with seeds from the same analyzer and extractor the Wikipedia pipeline
    /// uses.
    /// </summary>
    public static BundledDomainSource CreateDefault()
    {
        var analyzer = EngineBuilder.BuildDefault().Analyzer;
        var extractor = new SeedEntityExtractor();
        return new BundledDomainSource(BundledDomainCatalog.Default, d => extractor.Extract(analyzer.Analyze(d)));
    }

    public DiscoveryOutcome Discover(string description, Action<DiscoveryProgress>? onProgress = null)
    {
        var seeds = _seeds(description ?? string.Empty);
        var match = _catalog.Match(description);

        if (match is null)
            return new DiscoveryOutcome(seeds, NoMatchLabel, false, "No built-in domain matches that description.");

        var model = LlmModelNormalizer.Normalize(match.Domain.Model, ModelOrigin.BuiltIn(match.Domain.Name));
        MergeSeeds(model, seeds, match.Domain);

        return new DiscoveryOutcome(
            model,
            $"the built-in {match.Domain.Name} domain",
            true,
            $"Matched: {string.Join(", ", match.MatchedWords)}.");
    }

    // The domain's version of a class wins, because it has fields. A seed that is one of the
    // domain's own name words ("library" in "a library that…") names the system, not a class in it.
    private static void MergeSeeds(CandidateDomainModel model, CandidateDomainModel seeds, BundledDomain domain)
    {
        // The seeds' own trail goes FIRST, in the order things happened (the user named it, then the
        // domain had it), so `why Fine` still says "you named it" — and so a rejection below is the
        // last word on a name. `why` reports each name's last decision: appended after, the seed
        // extractor's "kept" for "Library" overrode its rejection and the summary listed it as kept.
        model.Decisions.InsertRange(0, seeds.Decisions);

        foreach (var seed in seeds.Entities)
        {
            if (model.Entities.Any(e => e.Name.Equals(seed.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (domain.Terms.TryGetValue(WordStem.Of(seed.Name), out var weight) && weight >= NameWeight)
            {
                model.Rejected.Add(seed.Name);
                model.Record(seed.Name, DecisionStage.BuiltInDomain, DecisionOutcome.Rejected,
                    DecisionReasons.NamesTheWholeSystem);
                continue;
            }

            model.Entities.Add(seed);
        }
    }
}
