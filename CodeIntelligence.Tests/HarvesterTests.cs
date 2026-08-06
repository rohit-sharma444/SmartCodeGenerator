using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;
using CodeIntelligence.Corpus;
using Xunit;

namespace CodeIntelligence.Tests;

public sealed class HarvesterTests
{
    [Fact]
    public void Harvest_produces_documents()
    {
        IntentRepository.Initialize();
        var docs = CorpusHarvester.Harvest();

        Assert.NotEmpty(docs);
    }

    [Fact]
    public void Harvest_never_emits_an_unknown_intent()
    {
        IntentRepository.Initialize();
        var docs = CorpusHarvester.Harvest();

        Assert.DoesNotContain(docs, d => d.Intent == IntentType.Unknown);
    }

    [Fact]
    public void Harvest_includes_the_literal_command_table_phrasings()
    {
        IntentRepository.Initialize();
        var docs = CorpusHarvester.Harvest();

        Assert.Contains(docs, d =>
            d.SourceKind == CorpusSourceKind.LiteralCommand &&
            d.Text.Equals("show learning", StringComparison.OrdinalIgnoreCase) &&
            d.Intent == IntentType.ShowLearning);
    }

    [Fact]
    public void Harvest_composes_action_and_target_pairs_from_intent_rules()
    {
        IntentRepository.Initialize();
        var docs = CorpusHarvester.Harvest();

        Assert.Contains(docs, d => d.SourceKind == CorpusSourceKind.IntentRule);
    }

    [Fact]
    public void Harvest_deduplicates_identical_phrasings()
    {
        IntentRepository.Initialize();
        var docs = CorpusHarvester.Harvest();

        // The dedup key is (Text, Intent), not Text alone: two documents sharing
        // text but disagreeing on intent are an ambiguous phrase, not a duplicate
        // (see Harvest_keeps_both_intents_for_an_ambiguous_phrase). This only
        // asserts that no (Text, Intent) pair repeats.
        var duplicates = docs
            .GroupBy(d => (Text: d.Text.ToLowerInvariant(), d.Intent))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Harvest_keeps_both_intents_for_an_ambiguous_phrase()
    {
        IntentRepository.Initialize();
        var docs = CorpusHarvester.Harvest();

        // GenerateDocumentation and GenerateDesignDocument both compose the
        // action "generate" with the target "design document" (IntentRules.cs),
        // so both intents produce the identical phrase "generate design
        // document". First-wins dedup would arbitrarily discard one intent's
        // signal; the correct behavior is to keep both, so a query for this
        // phrase splits the k-NN vote and the resolver declines rather than
        // guessing.
        Assert.Contains(docs, d =>
            d.Text.Equals("generate design document", StringComparison.OrdinalIgnoreCase) &&
            d.Intent == IntentType.GenerateDocumentation);

        Assert.Contains(docs, d =>
            d.Text.Equals("generate design document", StringComparison.OrdinalIgnoreCase) &&
            d.Intent == IntentType.GenerateDesignDocument);
    }

    [Fact]
    public void Harvest_is_deterministic()
    {
        IntentRepository.Initialize();

        var first = CorpusHarvester.Harvest().Select(d => d.Text).ToList();
        var second = CorpusHarvester.Harvest().Select(d => d.Text).ToList();

        Assert.Equal(first, second);
    }

    [Fact]
    public void StripPlaceholders_keeps_a_stray_closer_with_nothing_of_its_own_kind_open()
    {
        // No '<' ever opens, so the '>' is not closing anything — it's a literal
        // character and must survive, not be silently swallowed.
        Assert.Equal("abc > def", CorpusHarvester.StripPlaceholders("abc > def"));
    }

    [Fact]
    public void StripPlaceholders_does_not_let_a_mismatched_closer_end_the_span()
    {
        // '<' opens an angle span; ']' is the WRONG closer kind, so it must not
        // close it. The angle span stays open (swallowing "a]" and " bar") until
        // end of string, since no matching '>' ever appears. A shared-counter
        // implementation incorrectly treats "<a]" as balanced and lets " bar"
        // leak back into the output.
        Assert.Equal("foo", CorpusHarvester.StripPlaceholders("foo <a] bar"));
    }
}
