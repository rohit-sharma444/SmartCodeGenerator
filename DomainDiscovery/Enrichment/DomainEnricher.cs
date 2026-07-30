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

    public CandidateDomainModel Enrich(string articleTitle, CandidateDomainModel seed)
    {
        var text = _client.FetchExtract(articleTitle);
        if (string.IsNullOrWhiteSpace(text))
            return seed;

        var harvested = _harvester.Harvest(_analyzer.Analyze(text));

        // Wikilinks name the multi-word concepts prose-noun frequency structurally cannot
        // see: "cash register" tokenizes into two common nouns that normalize to junk.
        foreach (var title in LinkedConceptSource.Candidates(_client.FetchLinks(articleTitle), text))
        {
            var name = NameNormalizer.ToCompoundEntityName(title);

            if (!NameNormalizer.IsValidIdentifier(name))
            {
                harvested.Record(title, DecisionStage.Enrichment, DecisionOutcome.Skipped,
                    DecisionReasons.NotAUsableClassName, $"linked from {articleTitle}");
                continue;
            }

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

        DemoteWhatTheDefinitionDoesNotName(harvested, LeadSection.Of(text), articleTitle);

        return Merge(seed, harvested);
    }

    /// <summary>
    /// Moves every candidate the article's LEAD does not name out of the class list and into
    /// <see cref="CandidateDomainModel.Suggestions"/>.
    /// </summary>
    /// <remarks>
    /// Both candidate sources overreach, and in the same way: they take everything the article
    /// discusses, and an article discusses far more than its domain. Point of sale yielded
    /// Restaurant, Computer, Data (from prose-noun frequency) and Smartphone, Touchscreen,
    /// TabletComputer, DepartmentStore (from wikilinks) — none of which a shop keeps records of.
    /// So the filter is applied once, to both, after they have been pooled.
    /// <para>
    /// Demoted, not dropped: the lead is a strong signal, not a perfect one — it puts Credit card
    /// and Self-checkout out of a POS model, and they are defensible classes. A suggestion costs
    /// the user one <c>add</c> to take back, which is the right price for a judgement call. See
    /// <see cref="LeadSection"/> for why the lead is the signal at all.
    /// </para>
    /// </remarks>
    private static void DemoteWhatTheDefinitionDoesNotName(
        CandidateDomainModel harvested, string lead, string articleTitle)
    {
        if (lead.Length == 0)
            return;

        var demoted = harvested.Entities
            // The title a linked concept came from is the spelling the prose uses ("Cash register");
            // a harvested noun has no title, and its own name is that spelling already.
            .Where(e => !lead.Contains(e.SourceTitle ?? e.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var entity in demoted)
        {
            harvested.Entities.Remove(entity);
            harvested.Suggestions.Add(entity);
            harvested.Record(entity.Name, DecisionStage.Enrichment, DecisionOutcome.Suggested,
                DecisionReasons.LeadDoesNotMentionIt, $"lead of \"{articleTitle}\"");
        }
    }

    /// <summary>
    /// A linked concept is a real, editor-disambiguated topic, but it carries no mention
    /// count of its own, so it ranks below the article's most-repeated nouns.
    /// </summary>
    private const double LinkConfidence = 0.5;

    private static CandidateDomainModel Merge(CandidateDomainModel seed, CandidateDomainModel harvested)
    {
        // The harvested model is a DIFFERENT instance, and every enrichment decision was
        // recorded against it. Without this line the whole stage's trail vanishes here while
        // the entities still arrive - a loss nothing downstream can detect.
        seed.Decisions.AddRange(harvested.Decisions);

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

        // A name the user typed is a class because they said so, whatever the article's lead does
        // or does not name — so a demoted candidate that the seed already claims is dropped here
        // rather than offered back to them as a suggestion for a class they already have.
        foreach (var suggestion in harvested.Suggestions)
        {
            var claimed = seed.Entities.FirstOrDefault(
                e => string.Equals(e.Name, suggestion.Name, StringComparison.OrdinalIgnoreCase));

            if (claimed is not null)
            {
                // Enrich runs once per article into this same model, so by the second article
                // the class it collides with may have come from the first rather than from the
                // user. Saying "your description" for that one blames the wrong source.
                seed.Record(suggestion.Name, DecisionStage.Enrichment, DecisionOutcome.Skipped,
                    claimed.Provenance == Provenance.Seed
                        ? DecisionReasons.AlreadyAClassFromYourDescription
                        : DecisionReasons.AlreadyAClassFromAnEarlierArticle);
                continue;
            }

            // No record here, deliberately: an earlier article already offered this same
            // suggestion and recorded the decision for it. A second record would double-count
            // one candidate in the summary.
            if (seed.Suggestions.Any(s => string.Equals(s.Name, suggestion.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            seed.Suggestions.Add(suggestion);
        }

        return seed;
    }
}
