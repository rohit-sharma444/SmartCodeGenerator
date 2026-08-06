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

        var duplicates = docs
            .GroupBy(d => d.Text.ToLowerInvariant())
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Harvest_is_deterministic()
    {
        IntentRepository.Initialize();

        var first = CorpusHarvester.Harvest().Select(d => d.Text).ToList();
        var second = CorpusHarvester.Harvest().Select(d => d.Text).ToList();

        Assert.Equal(first, second);
    }
}
