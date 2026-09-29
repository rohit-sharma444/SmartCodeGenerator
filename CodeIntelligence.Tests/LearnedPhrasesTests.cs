using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;
using CodeIntelligence.Corpus;
using Xunit;

namespace CodeIntelligence.Tests;

/// <summary>
/// learned-phrases.tsv: the phrasings users typed that the CLI did not understand, paired with
/// the command they typed next that it did, reviewed and committed - so every build ships them.
/// </summary>
public sealed class LearnedPhrasesTests
{
    [Fact]
    public void A_line_is_a_phrase_and_the_intent_it_means()
    {
        var parsed = LearnedPhrases.Parse("make me a new class\tCreateClass\n");

        var document = Assert.Single(parsed.Documents);
        Assert.Equal("make me a new class", document.Text);
        Assert.Equal(IntentType.CreateClass, document.Intent);
        Assert.Equal(CorpusSourceKind.Learned, document.SourceKind);
        Assert.Empty(parsed.Problems);
    }

    [Fact]
    public void Blank_lines_and_comments_are_skipped()
    {
        var parsed = LearnedPhrases.Parse("# reviewed 2026-09-29\n\n   \nmake me a new class\tCreateClass\n");

        Assert.Single(parsed.Documents);
        Assert.Empty(parsed.Problems);
    }

    /// <summary>A bad line is reported, never thrown: the file ships inside the CLI, and a typo
    /// in it must cost that one phrase, not the command layer.</summary>
    [Fact]
    public void A_malformed_line_is_reported_and_the_rest_are_kept()
    {
        var parsed = LearnedPhrases.Parse(
            "make me a new class\tCreateClass\nno tab here\nwipe it all\tNotAnIntent\nlist my classes\tListClasses\n");

        Assert.Equal(2, parsed.Documents.Count);
        Assert.Equal(2, parsed.Problems.Count);
        Assert.Contains(parsed.Problems, p => p.Contains("line 2", StringComparison.Ordinal));
        Assert.Contains(parsed.Problems, p => p.Contains("NotAnIntent", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_is_not_a_phrase_anything_can_mean()
    {
        var parsed = LearnedPhrases.Parse("huh\tUnknown\n");

        Assert.Empty(parsed.Documents);
        Assert.Single(parsed.Problems);
    }

    /// <summary>The file as it ships: every line a phrase the harvester can use.</summary>
    [Fact]
    public void The_shipped_file_has_no_malformed_lines()
    {
        Assert.Empty(LearnedPhrases.Shipped().Problems);
    }

    [Fact]
    public void The_harvest_includes_every_shipped_phrase()
    {
        IntentRepository.Initialize();
        var harvested = CorpusHarvester.Harvest()
            .Select(d => (d.Text, d.Intent))
            .ToHashSet();

        foreach (var learned in LearnedPhrases.Shipped().Documents)
            Assert.Contains((learned.Text, learned.Intent), harvested);
    }
}
