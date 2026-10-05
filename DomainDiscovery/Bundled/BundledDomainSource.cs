using System.Text.RegularExpressions;
using DomainDiscovery.Enrichment;
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
        return new BundledDomainSource(BundledDomainCatalog.Default, d => extractor.Extract(analyzer.Analyze(d), d));
    }

    public DiscoveryOutcome Discover(string description, Action<DiscoveryProgress>? onProgress = null)
    {
        var seeds = _seeds(description ?? string.Empty);
        RejectWordsThatAreNotClasses(seeds);
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

    // The Wikipedia path refuses these by shaping each seed against its own article (EntityShaper);
    // here there is no article, so a seed meets the verdict that shaper falls back to without one
    // — the name alone (ArchetypeLexicon.IsAbstractName) — plus the function words the harvester
    // refuses ("where"). Without this, "a library management system" kept Management and System,
    // and a class called System does not compile. Done for the no-match outcome as well, because
    // those seeds are what the review opens on if nothing later in the chain succeeds.
    private static void RejectWordsThatAreNotClasses(CandidateDomainModel seeds)
    {
        foreach (var seed in seeds.Entities.ToList())
        {
            var reason =
                StopWords.Contains(seed.Name) ? DecisionReasons.NotAUsableClassName
                : ArchetypeLexicon.IsAbstractName(seed.Name) ? DecisionReasons.NamesTheSoftware
                : null;

            if (reason is null)
                continue;

            seeds.Entities.Remove(seed);
            seeds.Rejected.Add(seed.Name);
            seeds.Record(seed.Name, DecisionStage.SeedExtraction, DecisionOutcome.Rejected, reason);
        }
    }

    // The domain's version of a class wins, because it has fields. A seed is left out, with the
    // reason in its trail, when it
    //  - names the system itself ("library" in "a library that…") — the domain's Self words only,
    //    so "landlords" and "librarians" are kept;
    //  - is a word of a domain class's name ("tables" → DiningTable, "tasks" → ProjectTask), which
    //    would otherwise sit beside it as a near-duplicate;
    //  - was made from a verb ("books patients" → Booking): the domain defines its own records, and
    //    beside the Clinic's Appointment that seed was an empty master nobody asked for.
    private static void MergeSeeds(CandidateDomainModel model, CandidateDomainModel seeds, BundledDomain domain)
    {
        // The seeds' own trail goes FIRST, in the order things happened (the user named it, then the
        // domain had it), so `why Fine` still says "you named it" — and so a rejection below is the
        // last word on a name. `why` reports each name's last decision: appended after, the seed
        // extractor's "kept" for "Library" overrode its rejection and the summary listed it as kept.
        model.Decisions.InsertRange(0, seeds.Decisions);
        model.Rejected.AddRange(seeds.Rejected);

        var domainClasses = model.Entities.ToList();
        var madeFromVerbs = seeds.Decisions
            .Where(d => d.Reason == DecisionReasons.YouSaidItAsAVerb)
            .Select(d => d.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var seed in seeds.Entities)
        {
            // By spelling key, so a seed "Enrollment" is the School domain's own Enrolment.
            if (domainClasses.Any(e => NameNormalizer.SpellingKey(e.Name) == NameNormalizer.SpellingKey(seed.Name)))
                continue;

            var stem = WordStem.Of(seed.Name);
            var covering = domainClasses.FirstOrDefault(e => NameWords(e.Name).Any(w => WordStem.Of(w) == stem));

            string? reason =
                domain.Self.Contains(stem) ? DecisionReasons.NamesTheWholeSystem
                : covering is not null ? DecisionReasons.CoveredBy(covering.Name, domain.Name)
                : madeFromVerbs.Contains(seed.Name) ? DecisionReasons.LeftToTheBuiltInDomain(domain.Name)
                : null;

            if (reason is null)
            {
                model.Entities.Add(seed);
                continue;
            }

            model.Rejected.Add(seed.Name);
            model.Record(seed.Name, DecisionStage.BuiltInDomain, DecisionOutcome.Rejected, reason);
        }
    }

    // DiningTable -> Dining, Table.
    private static IEnumerable<string> NameWords(string name) =>
        Regex.Matches(name, "[A-Z][a-z]*|[a-z]+").Select(m => m.Value);
}
