using CodeGenerator.App;
using CodeIntelligence;
using CodeIntelligence.Onnx;
using CodeIntelligence.Resolution;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The AI "did you mean" offered when the build-app review loop cannot parse a typed
/// line. Uses the real committed model, like the other retrieval tests, because the
/// point of these is whether retrieval actually separates the eight review verbs.
/// </summary>
[TestClass]
public class ReviewCommandSuggestionTests
{
    /// <summary>The resolver exactly as the review loop builds it — the catalog's own
    /// calibrated constants, so these tests measure what actually ships.</summary>
    private static PhraseResolver Resolver()
    {
        var model = ModelRegistry.DefaultEmbeddingModel;
        var embedder = new OnnxEmbeddingProvider(model.ModelPath, model.VocabPath, model.ModelId);

        return new PhraseResolver(
            embedder, ReviewCommandPhrases.All,
            ReviewCommandPhrases.TopK,
            ReviewCommandPhrases.ConfidenceFloor,
            ReviewCommandPhrases.MinTopScore);
    }

    [TestMethod]
    public void Every_phrase_label_is_a_real_review_verb()
    {
        // The drift guard. A label that no longer matches a ReviewActions verb produces
        // a suggestion the loop's switch cannot dispatch, so the user is told to type
        // something that does nothing.
        var verbs = ReviewActions.All.Select(a => a.Verb).ToHashSet(StringComparer.Ordinal);

        var orphans = ReviewCommandPhrases.All
            .Select(p => p.Label)
            .Distinct()
            .Where(label => !verbs.Contains(label))
            .ToList();

        Assert.IsEmpty(orphans, $"phrase labels with no review action: {string.Join(", ", orphans)}");
    }

    [TestMethod]
    public void Every_review_verb_has_at_least_one_phrasing()
    {
        // The other direction: a verb with no phrasings can never be suggested, so the
        // feature silently does nothing for it.
        var labelled = ReviewCommandPhrases.All.Select(p => p.Label).ToHashSet(StringComparer.Ordinal);

        var uncovered = ReviewActions.All
            .Select(a => a.Verb)
            .Where(verb => !labelled.Contains(verb))
            .ToList();

        Assert.IsEmpty(uncovered, $"review verbs with no phrasing: {string.Join(", ", uncovered)}");
    }

    [TestMethod]
    public void No_phrasing_is_simply_the_verb_itself()
    {
        // A catalog of bare verbs would be useless: those already match exactly and
        // never reach the unrecognized branch.
        var bare = ReviewCommandPhrases.All
            .Where(p => p.Text.Trim().Equals(p.Label, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Text)
            .ToList();

        Assert.IsEmpty(bare);
    }

    [TestMethod]
    public void No_suggestion_without_a_resolver()
        => Assert.IsEmpty(BuildAppCommand.SuggestionLines(null, "get rid of Reservation"));

    [TestMethod]
    public void No_suggestion_for_a_blank_line()
        => Assert.IsEmpty(BuildAppCommand.SuggestionLines(Resolver(), "   "));

    [TestMethod]
    public void Unrelated_input_is_declined_rather_than_guessed()
    {
        // The whole point of the two gates. "declining is always better than a
        // confident wrong answer" applies here exactly as it does at the main prompt.
        Assert.IsEmpty(BuildAppCommand.SuggestionLines(Resolver(), "the weather in Paris is cold"));
    }

    [TestMethod]
    public void A_paraphrase_of_remove_suggests_remove()
    {
        var lines = BuildAppCommand.SuggestionLines(Resolver(), "get rid of that entity");

        Assert.IsNotEmpty(lines);
        StringAssert.Contains(lines[0], "remove");
    }

    [TestMethod]
    public void A_paraphrase_of_cancel_suggests_cancel()
    {
        var lines = BuildAppCommand.SuggestionLines(Resolver(), "forget it, stop the review");

        Assert.IsNotEmpty(lines);
        StringAssert.Contains(lines[0], "cancel");
    }

    [TestMethod]
    public void The_suggestion_shows_the_arguments_the_verb_needs()
    {
        // Usage comes from ReviewActions, so "remove" must carry its operand rather
        // than being printed bare - a bare verb would fail the loop's `parts.Length`
        // guard and land right back on "Unrecognized command."
        var lines = BuildAppCommand.SuggestionLines(Resolver(), "get rid of that entity");

        Assert.IsNotEmpty(lines);
        StringAssert.Contains(lines[0], "<");
        StringAssert.Contains(lines[1], "confidence");
    }

    [TestMethod]
    public void A_zero_argument_verb_is_suggested_without_placeholders()
    {
        var lines = BuildAppCommand.SuggestionLines(Resolver(), "forget it, stop the review");

        Assert.IsNotEmpty(lines);
        Assert.DoesNotContain('<', lines[0],
            $"cancel takes no arguments but was shown with placeholders: {lines[0]}");
    }

    [TestMethod]
    public void Gibberish_is_declined_even_though_the_vote_is_unanimous()
    {
        // The reason MinTopScore exists for this catalog. With eight well-separated
        // labels the neighbours agree on anything, so share-of-vote reads 100% for input
        // the index has never seen. Measured: "the weather in Paris is cold" votes
        // `field` at 1.00 confidence on a closest score of 0.136.
        Assert.IsEmpty(BuildAppCommand.SuggestionLines(Resolver(), "zzzz qqqq"));
    }
}
