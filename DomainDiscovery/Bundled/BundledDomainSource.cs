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
        var match = _catalog.Match(description);

        if (match is null)
        {
            RejectWordsThatAreNotClasses(seeds, null, []);
            return new DiscoveryOutcome(seeds, NoMatchLabel, false, "No built-in domain matches that description.");
        }

        var model = LlmModelNormalizer.Normalize(match.Domain.Model, ModelOrigin.BuiltIn(match.Domain.Name));
        RejectWordsThatAreNotClasses(seeds, match.Domain, model.Entities);
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
    //
    // The reason must be the true one. "tracks job applications" was refused as "the software
    // being built" while the Recruitment domain keeps JobApplication; Movement and Maintenance,
    // which are activities rather than software, got the same words. A word the matched domain
    // keeps under its own name now says so, and an activity word says it is an activity.
    private static void RejectWordsThatAreNotClasses(
        CandidateDomainModel seeds, BundledDomain? domain, IReadOnlyList<CandidateEntity> domainClasses)
    {
        foreach (var seed in seeds.Entities.ToList())
        {
            string? reason;
            var stage = DecisionStage.SeedExtraction;

            if (StopWords.Contains(seed.Name))
                reason = DecisionReasons.NotAUsableClassName;
            else if (!ArchetypeLexicon.IsAbstractName(seed.Name))
                reason = null;
            else if (domain is not null && DomainVersionOf(seed.Name, domainClasses, domain) is { } kept)
            {
                reason = kept;
                stage = DecisionStage.BuiltInDomain;
            }
            else
                reason = ArchetypeLexicon.IsSoftwareName(seed.Name)
                    ? DecisionReasons.NamesTheSoftware
                    : DecisionReasons.NamesAnActivity;

            if (reason is null)
                continue;

            seeds.Entities.Remove(seed);
            seeds.Rejected.Add(seed.Name);
            seeds.Record(seed.Name, stage, DecisionOutcome.Rejected, reason);
        }
    }

    // Why the domain has its own version of this seed, or null when it has none: a word of one of
    // its class names (Application -> JobApplication) or another word for one (Customer -> Owner).
    private static string? DomainVersionOf(string seedName, IReadOnlyList<CandidateEntity> domainClasses, BundledDomain domain)
    {
        var stem = WordStem.Of(seedName);
        if (domainClasses.FirstOrDefault(e => NameWords(e.Name).Any(w => WordStem.Of(w) == stem)) is { } covering)
            return DecisionReasons.CoveredBy(covering.Name, domain.Name);

        return SameClassAs(stem, domainClasses, domain) is { } same
            ? DecisionReasons.AnotherWordFor(same.Name, domain.Name)
            : null;
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

            string? reason =
                domain.Self.Contains(WordStem.Of(seed.Name)) ? DecisionReasons.NamesTheWholeSystem
                : DomainVersionOf(seed.Name, domainClasses, domain) is { } kept ? kept
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

    /// <summary>
    /// The domain class <paramref name="stem"/> is another word for, or null: the domain's own
    /// synonyms first ("customer" is a vet's Owner), then words that mean the same role in any
    /// domain ("staff" is HR's Employee, "vehicle" Car rental's Car).
    /// </summary>
    /// <remarks>
    /// Without this, a seed with a different word for a domain class became a second, empty class
    /// beside it - Customer beside the Owner, with only an Id, its own screens, dashboard tiles and
    /// three seeded rows of nothing - and nothing linked to it. A group matches a domain class by
    /// the last word of its name, so "vehicles" finds Fleet's Vehicle but "staff" does not find a
    /// TeamMember.
    /// </remarks>
    private static CandidateEntity? SameClassAs(string stem, IReadOnlyList<CandidateEntity> domainClasses, BundledDomain domain)
    {
        if (domain.Synonyms.TryGetValue(stem, out var named)
            && domainClasses.FirstOrDefault(e => e.Name == named) is { } own)
            return own;

        var group = SynonymGroups.FirstOrDefault(g => g.Contains(stem));
        if (group is null)
            return null;

        return domainClasses.FirstOrDefault(e =>
            NameWords(e.Name).LastOrDefault() is { } last && group.Contains(WordStem.Of(last)));
    }

    // Words that name the same role whichever domain they are said in. Stems, as WordStem makes them.
    private static readonly HashSet<string>[] SynonymGroups =
    [
        Group("customer", "client", "patron"),
        Group("employee", "staff", "worker", "personnel"),
        Group("teacher", "tutor", "instructor", "lecturer"),
        Group("student", "pupil", "learner"),
        Group("car", "vehicle"),
        Group("supplier", "vendor"),
        Group("product", "goods"),
        Group("doctor", "physician", "clinician"),
        Group("attendee", "participant", "delegate"),
        Group("tenant", "renter", "lessee"),
    ];

    private static HashSet<string> Group(params string[] words) =>
        words.Select(WordStem.Of).ToHashSet(StringComparer.Ordinal);

    // DiningTable -> Dining, Table.
    private static IEnumerable<string> NameWords(string name) =>
        Regex.Matches(name, "[A-Z][a-z]*|[a-z]+").Select(m => m.Value);
}
