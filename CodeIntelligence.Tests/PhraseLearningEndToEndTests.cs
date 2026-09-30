using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;
using CodeIntelligence.Corpus;
using CodeIntelligence.Onnx;
using CodeIntelligence.Resolution;
using CodeIntelligence.Storage;
using Xunit;

namespace CodeIntelligence.Tests;

/// <summary>
/// The whole loop against the real model: a phrasing the shipped corpus does not understand,
/// one miss and the retry that fixed it, the candidate that produces, the line it becomes in
/// learned-phrases.tsv - and the same phrasing, with a DIFFERENT argument, now understood.
/// </summary>
public sealed class PhraseLearningEndToEndTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ci-learn-" + Guid.NewGuid().ToString("N"));

    private readonly OnnxEmbeddingProvider _embedder;

    public PhraseLearningEndToEndTests()
    {
        Directory.CreateDirectory(_dir);
        var model = ModelRegistry.DefaultEmbeddingModel;
        _embedder = new OnnxEmbeddingProvider(model.ModelPath, model.VocabPath, model.ModelId);
    }

    public void Dispose()
    {
        _embedder.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void A_phrasing_learned_from_one_retry_is_understood_the_next_time()
    {
        IntentRepository.Initialize();
        // Without learned-phrases.tsv, whose first batch taught "whip up a class" for real.
        var shipped = CorpusHarvester.Harvest()
            .Where(d => d.SourceKind != CorpusSourceKind.Learned)
            .ToList();

        // Before: not understood - declined, or taken to mean something else.
        const string nextTime = "whip up a class Customer";
        var before = ResolverOver(shipped, "before").Resolve(nextTime, CancellationToken.None);
        Assert.True(before is null || before.Intent != IntentType.CreateClass,
            $"precondition: the shipped corpus already understands '{nextTime}'");

        // One miss, and the command that fixed it, as LearningCoordinator sees them.
        var phrase = PhraseCandidates.Generalize(
            "whip up a class Invoice",
            new ParsedCommand { Intent = IntentType.CreateClass, ClassName = "Invoice" });
        Assert.Equal("whip up a class", phrase);

        // Reviewed and committed: the line as it would sit in learned-phrases.tsv.
        var learned = LearnedPhrases.Parse($"{phrase}\t{IntentType.CreateClass}\n");
        Assert.Empty(learned.Problems);

        var after = ResolverOver([.. shipped, .. learned.Documents], "after")
            .Resolve(nextTime, CancellationToken.None);

        Assert.NotNull(after);
        Assert.Equal(IntentType.CreateClass, after.Intent);
    }

    /// <summary>At the thresholds that ship, so "understood" means understood in the CLI.</summary>
    private RetrievalIntentResolver ResolverOver(IReadOnlyList<CorpusDocument> documents, string name)
    {
        var store = new SqliteVectorStore(Path.Combine(_dir, name + ".db"));
        store.ReplaceAll(_embedder.ModelId, _embedder.Dimension,
            [.. documents.Select(d => (d, _embedder.Embed(d.Text)))]);

        return new RetrievalIntentResolver(store, _embedder, topK: AiConfig.DefaultTopK,
            floor: AiConfig.DefaultConfidenceFloor, minTopScore: AiConfig.DefaultMinTopScore,
            budget: TimeSpan.FromSeconds(30));
    }
}
