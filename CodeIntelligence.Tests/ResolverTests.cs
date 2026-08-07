using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;
using CodeIntelligence.Resolution;
using CodeIntelligence.Storage;
using CodeIntelligence.Tests.Fakes;
using Xunit;

namespace CodeIntelligence.Tests;

public sealed class ResolverTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ci-resolver-" + Guid.NewGuid().ToString("N"));

    public ResolverTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private SqliteVectorStore SeededStore(FakeEmbeddingProvider embedder)
    {
        var store = new SqliteVectorStore(Path.Combine(_dir, "ai-index.db"));

        (string Text, IntentType Intent)[] seed =
        [
            ("list classes", IntentType.ListClasses),
            ("show classes", IntentType.ListClasses),
            ("create class", IntentType.CreateClass),
            ("delete property", IntentType.DeleteProperty)
        ];

        store.ReplaceAll(embedder.ModelId, embedder.Dimension,
        [
            .. seed.Select(s => (
                new CorpusDocument(s.Text, s.Intent, CorpusSourceKind.IntentRule, "test", s.Text),
                embedder.Embed(s.Text)))
        ]);

        return store;
    }

    [Fact]
    public void Resolves_a_paraphrase_to_the_neighbouring_intent()
    {
        var embedder = new FakeEmbeddingProvider();
        var resolver = new RetrievalIntentResolver(
            SeededStore(embedder), embedder, topK: 3, floor: 0.4, minTopScore: 0.0, budget: TimeSpan.FromSeconds(5));

        var result = resolver.Resolve("show me the classes", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(IntentType.ListClasses, result!.Intent);
        Assert.NotEmpty(result.Evidence);
    }

    [Fact]
    public void Returns_null_for_input_with_no_overlap()
    {
        var embedder = new FakeEmbeddingProvider();
        var resolver = new RetrievalIntentResolver(
            SeededStore(embedder), embedder, topK: 3, floor: 0.4, minTopScore: 0.0, budget: TimeSpan.FromSeconds(5));

        Assert.Null(resolver.Resolve("zzzz qqqq", CancellationToken.None));
    }

    [Fact]
    public void Returns_null_when_the_index_was_built_by_another_model()
    {
        var seeder = new FakeEmbeddingProvider { ModelId = "fake-v1" };
        var store = SeededStore(seeder);

        var other = new FakeEmbeddingProvider { ModelId = "fake-v2" };
        var resolver = new RetrievalIntentResolver(
            store, other, topK: 3, floor: 0.4, minTopScore: 0.0, budget: TimeSpan.FromSeconds(5));

        Assert.Null(resolver.Resolve("list classes", CancellationToken.None));
    }

    [Fact]
    public void A_throwing_embedder_yields_null_rather_than_propagating()
    {
        var embedder = new FakeEmbeddingProvider();
        var store = SeededStore(embedder);

        var broken = new FakeEmbeddingProvider
        {
            OnEmbed = _ => throw new InvalidOperationException("onnx exploded")
        };

        var resolver = new RetrievalIntentResolver(
            store, broken, topK: 3, floor: 0.4, minTopScore: 0.0, budget: TimeSpan.FromSeconds(5));

        var result = resolver.Resolve("list classes", CancellationToken.None);

        Assert.Null(result);

        // Null alone does not prove the catch ran — every other failure path returns
        // null too, so a broken embedder that was never CALLED would pass. Pinning
        // Unavailable is what says the exception reached the handler rather than the
        // resolver bailing out earlier at the reindex gate.
        Assert.Contains("InvalidOperationException", resolver.Unavailable);
        Assert.Contains("onnx exploded", resolver.Unavailable);
    }

    [Fact]
    public void A_cancelled_token_yields_null()
    {
        var embedder = new FakeEmbeddingProvider();
        var resolver = new RetrievalIntentResolver(
            SeededStore(embedder), embedder, topK: 3, floor: 0.4, minTopScore: 0.0, budget: TimeSpan.FromSeconds(5));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Null(resolver.Resolve("list classes", cts.Token));
    }

    [Fact]
    public void Blank_input_yields_null()
    {
        var embedder = new FakeEmbeddingProvider();
        var resolver = new RetrievalIntentResolver(
            SeededStore(embedder), embedder, topK: 3, floor: 0.4, minTopScore: 0.0, budget: TimeSpan.FromSeconds(5));

        Assert.Null(resolver.Resolve("   ", CancellationToken.None));
    }
}
