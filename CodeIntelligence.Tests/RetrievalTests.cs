using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;
using CodeIntelligence.Retrieval;
using Xunit;

namespace CodeIntelligence.Tests;

public sealed class RetrievalTests
{
    private static StoredVector V(string text, IntentType intent, params float[] vector)
        => new(text.GetHashCode(), text, intent, Similarity.Normalize(vector));

    [Fact]
    public void Cosine_of_identical_unit_vectors_is_one()
        => Assert.Equal(1.0, Similarity.Cosine([1f, 0f], [1f, 0f]), 6);

    [Fact]
    public void Cosine_of_orthogonal_vectors_is_zero()
        => Assert.Equal(0.0, Similarity.Cosine([1f, 0f], [0f, 1f]), 6);

    [Fact]
    public void Cosine_of_mismatched_lengths_is_zero()
        => Assert.Equal(0.0, Similarity.Cosine([1f, 0f], [1f, 0f, 0f]), 6);

    [Fact]
    public void Unanimous_neighbours_win_with_high_confidence()
    {
        var corpus = new[]
        {
            V("list classes", IntentType.ListClasses, 1f, 0f),
            V("show classes", IntentType.ListClasses, 0.99f, 0.01f),
            V("create class", IntentType.CreateClass, 0f, 1f)
        };

        var result = NeighbourVote.Decide(corpus, Similarity.Normalize([1f, 0f]), topK: 2, floor: 0.1, minTopScore: 0.35);

        Assert.NotNull(result);
        Assert.Equal(IntentType.ListClasses, result!.Intent);
        Assert.True(result.Confidence > 0.9);
    }

    [Fact]
    public void A_split_vote_yields_low_confidence()
    {
        var corpus = new[]
        {
            V("list classes", IntentType.ListClasses, 1f, 0f),
            V("create class", IntentType.CreateClass, 1f, 0f)
        };

        var result = NeighbourVote.Decide(corpus, Similarity.Normalize([1f, 0f]), topK: 2, floor: 0.0, minTopScore: 0.35);

        Assert.NotNull(result);
        Assert.True(result!.Confidence < 0.6);
    }

    [Fact]
    public void Below_the_floor_returns_null_rather_than_guessing()
    {
        var corpus = new[]
        {
            V("list classes", IntentType.ListClasses, 1f, 0f),
            V("create class", IntentType.CreateClass, 1f, 0f)
        };

        var result = NeighbourVote.Decide(corpus, Similarity.Normalize([1f, 0f]), topK: 2, floor: 0.9, minTopScore: 0.35);

        Assert.Null(result);
    }

    [Fact]
    public void An_empty_corpus_returns_null()
        => Assert.Null(NeighbourVote.Decide([], [1f, 0f], topK: 5, floor: 0.1, minTopScore: 0.35));

    [Fact]
    public void Evidence_is_ordered_best_first_and_capped_at_topK()
    {
        var corpus = new[]
        {
            V("a", IntentType.ListClasses, 0.2f, 1f),
            V("b", IntentType.ListClasses, 1f, 0f),
            V("c", IntentType.ListClasses, 0.6f, 0.8f)
        };

        var result = NeighbourVote.Decide(corpus, Similarity.Normalize([1f, 0f]), topK: 2, floor: 0.0, minTopScore: 0.35);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Evidence.Count);
        Assert.Equal("b", result.Evidence[0].Text);
        Assert.True(result.Evidence[0].Score >= result.Evidence[1].Score);
    }

    [Fact]
    public void A_lone_weak_match_returns_null_despite_100_percent_vote_share()
    {
        var corpus = new[]
        {
            V("weak", IntentType.ListClasses, 0.02f, 1f)
        };

        var result = NeighbourVote.Decide(corpus, Similarity.Normalize([1f, 0f]), topK: 5, floor: 0.0, minTopScore: 0.35);

        Assert.Null(result);
    }

    [Fact]
    public void Floor_boundary_is_inclusive_minimum()
    {
        var corpus = new[]
        {
            V("a", IntentType.ListClasses, 1f, 0f),
            V("b", IntentType.CreateClass, 1f, 0f)
        };

        var result = NeighbourVote.Decide(corpus, Similarity.Normalize([1f, 0f]), topK: 2, floor: 0.5, minTopScore: 0.0);

        Assert.NotNull(result);
        Assert.Equal(0.5, result!.Confidence, 6);
    }

    [Fact]
    public void Voting_is_score_weighted_not_count_based()
    {
        var corpus = new[]
        {
            V("strong", IntentType.ListClasses, 0.99f, 0.1414f),
            V("weak1", IntentType.CreateClass, 0.1f, 0.9949f),
            V("weak2", IntentType.CreateClass, 0.1f, 0.9949f)
        };

        var result = NeighbourVote.Decide(corpus, Similarity.Normalize([1f, 0f]), topK: 3, floor: 0.0, minTopScore: 0.0);

        Assert.NotNull(result);
        Assert.Equal(IntentType.ListClasses, result!.Intent);
        Assert.True(result.Confidence > 0.8);
    }

    [Fact]
    public void Evidence_ordering_is_total()
    {
        var corpus = new[]
        {
            V("text", IntentType.CreateClass, 0.5f, 0f),
            V("text", IntentType.ListClasses, 0.5f, 0f)
        };

        var result = NeighbourVote.Decide(corpus, Similarity.Normalize([1f, 0f]), topK: 2, floor: 0.0, minTopScore: 0.0);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Evidence.Count);
        Assert.Equal(IntentType.CreateClass, result.Evidence[0].Intent);
        Assert.Equal(IntentType.ListClasses, result.Evidence[1].Intent);
    }
}
