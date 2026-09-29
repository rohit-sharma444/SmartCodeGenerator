using CodeIntelligence;
using Xunit;

namespace CodeIntelligence.Tests;

public sealed class AiLayerTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ci-layer-" + Guid.NewGuid().ToString("N"));

    public AiLayerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string ConfigPath => Path.Combine(_dir, "ai.json");
    private string IndexPath => Path.Combine(_dir, "ai-index.db");

    private AiLayer Layer(bool enabled)
    {
        var config = AiConfig.Load(ConfigPath);
        config.Enabled = enabled;
        config.Save(ConfigPath);

        return AiLayer.Create(ConfigPath, IndexPath);
    }

    [Fact]
    public void A_disabled_layer_offers_no_resolver_and_says_why()
    {
        var layer = Layer(enabled: false);

        Assert.Null(layer.Resolver);
        Assert.Contains("disabled", layer.Unavailable);
        Assert.Contains("off (", layer.StatusText());
    }

    [Fact]
    public void An_enabled_layer_exposes_a_resolver()
    {
        var layer = Layer(enabled: true);

        Assert.Null(layer.Unavailable);
        Assert.NotNull(layer.Resolver);
    }

    [Fact]
    public void Reindex_populates_the_index()
    {
        var layer = Layer(enabled: true);

        var message = layer.Reindex();

        Assert.Contains("Indexed", message);
        Assert.Contains("all-MiniLM-L6-v2-int8", message);
        Assert.NotNull(layer.Resolver);
    }

    [Fact]
    public void A_successful_reinitialization_clears_the_previous_reason()
    {
        // Start DISABLED, which is what puts a reason in Unavailable. Starting enabled
        // would leave it null from the outset and this test could not fail.
        var layer = Layer(enabled: false);
        Assert.Contains("disabled", layer.Unavailable);

        // The embedder and store are built before the Enabled check, so a disabled
        // layer can still reindex — this path is reachable, not hypothetical.
        layer.Config.Enabled = true;
        layer.Reindex();

        Assert.NotNull(layer.Resolver);

        // Initialize() must reset Unavailable at the top. Without that reset the layer
        // reports a working resolver and "disabled - run 'ai enable'" at the same time.
        Assert.Null(layer.Unavailable);
    }

    /// <summary>
    /// An enabled layer builds its own index, rather than declining every phrase with
    /// "index is empty - run 'ai reindex'" until somebody knows to.
    /// </summary>
    /// <remarks>
    /// That was every fresh install: nothing ever ran the reindex, so the layer shipped on and
    /// answered nothing. And a corpus that GREW - a learned phrase in a new release - reached no
    /// existing install either, whose index still held the old phrasings.
    /// </remarks>
    [Fact]
    public void An_enabled_layer_indexes_its_corpus_without_being_asked()
    {
        var layer = Layer(enabled: true);

        Assert.DoesNotContain("REINDEX REQUIRED", layer.StatusText());
        Assert.NotNull(layer.Resolver!.Resolve("create a class", CancellationToken.None));
    }

    [Fact]
    public void An_index_that_matches_the_corpus_is_not_rebuilt()
    {
        Layer(enabled: true);
        var built = new Storage.SqliteVectorStore(IndexPath).GetStatus(ModelId).BuiltUtc;
        Assert.NotNull(built); // or "unchanged" would hold of an index that was never built

        AiLayer.Create(ConfigPath, IndexPath);

        Assert.Equal(built, new Storage.SqliteVectorStore(IndexPath).GetStatus(ModelId).BuiltUtc);
    }

    [Fact]
    public void An_index_built_from_a_different_corpus_is_rebuilt()
    {
        Layer(enabled: true);

        // What an install still holds from an older release: fewer phrasings than it ships now.
        var store = new Storage.SqliteVectorStore(IndexPath);
        var stale = store.LoadAll(ModelId).Take(3).ToList();
        store.ReplaceAll(ModelId, stale[0].Vector.Length,
            [.. stale.Select(v => (new Abstractions.CorpusDocument(
                v.Text, v.Intent, Abstractions.CorpusSourceKind.IntentRule, "old", "old-" + v.DocumentId), v.Vector))]);

        AiLayer.Create(ConfigPath, IndexPath);

        Assert.True(new Storage.SqliteVectorStore(IndexPath).GetStatus(ModelId).DocumentCount > 3);
    }

    private static string ModelId => Onnx.ModelRegistry.DefaultEmbeddingModel.ModelId;

    [Fact]
    public void Status_surfaces_both_gates_so_they_can_be_calibrated_separately()
    {
        var status = Layer(enabled: true).StatusText();

        Assert.Contains("floor:", status);
        Assert.Contains("minTopScore:", status);
    }

    [Fact]
    public void Reindex_on_a_layer_that_never_initialized_reports_rather_than_throws()
    {
        // Index path inside a file, not a folder, so the store cannot be created.
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "not a directory");

        var config = AiConfig.Load(ConfigPath);
        config.Enabled = true;
        config.Save(ConfigPath);

        var layer = AiLayer.Create(ConfigPath, Path.Combine(blocker, "ai-index.db"));

        Assert.Null(layer.Resolver);
        Assert.Contains("AI unavailable", layer.Reindex());
    }
}
