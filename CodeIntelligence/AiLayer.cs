using CodeIntelligence.Abstractions;
using CodeIntelligence.Corpus;
using CodeIntelligence.Onnx;
using CodeIntelligence.Resolution;
using CodeIntelligence.Storage;

namespace CodeIntelligence;

/// <summary>Composition root and the single catch-all boundary for the AI layer.
///
/// Construction never throws: if the model is missing, the runtime fails to load, or
/// anything else goes wrong, Resolver stays null and Unavailable carries the reason.
/// A null Resolver is what the bridge already treats as "no AI", so every failure
/// degrades to exactly today's behavior.</summary>
public sealed class AiLayer
{
    private AiLayer(AiConfig config, string indexPath)
    {
        Config = config;
        IndexPath = indexPath;
        DecisionLog = new DecisionLog(indexPath);
    }

    public AiConfig Config { get; }
    public string IndexPath { get; }
    public DecisionLog DecisionLog { get; }
    public IIntentResolver? Resolver { get; private set; }
    public string? Unavailable { get; private set; }

    private IEmbeddingProvider? _embedder;
    private SqliteVectorStore? _store;

    public static AiLayer Create(string? configPath = null, string? indexPath = null)
    {
        var config = AiConfig.Load(configPath);
        indexPath ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai-index.db");

        var layer = new AiLayer(config, indexPath);
        layer.Initialize();
        return layer;
    }

    private void Initialize()
    {
        // Reset both outputs first. Reindex() re-enters here, and without the reset a
        // reason recorded by an earlier attempt outlives a later successful one — so
        // `ai status` would report a stale cause for a layer that is now working.
        Unavailable = null;
        Resolver = null;

        try
        {
            _store = new SqliteVectorStore(IndexPath);

            var model = ModelRegistry.DefaultEmbeddingModel;
            var problem = ModelRegistry.Validate(model);
            if (problem is not null)
            {
                Unavailable = problem;
                return;
            }

            _embedder = new OnnxEmbeddingProvider(model.ModelPath, model.VocabPath, model.ModelId);

            if (!Config.Enabled)
            {
                Unavailable = "disabled - run 'ai enable'";
                return;
            }

            Resolver = new RetrievalIntentResolver(
                _store, _embedder, Config.TopK, Config.ConfidenceFloor, Config.MinTopScore,
                TimeSpan.FromMilliseconds(Config.RetrievalTimeoutMs));
        }
        catch (Exception ex)
        {
            // Typically a native load failure: wrong architecture, or a missing
            // Visual C++ runtime. Report it, do not rethrow.
            Unavailable = $"{ex.GetType().Name}: {ex.Message}";
            Resolver = null;
        }
    }

    /// <summary>Rebuilds the corpus and its embeddings. ReplaceAll is one transaction,
    /// so an interrupted reindex leaves the previous index intact and serving.</summary>
    public string Reindex()
    {
        try
        {
            if (_embedder is null || _store is null)
                return $"AI unavailable: {Unavailable ?? "not initialized"}";

            var documents = CorpusHarvester.Harvest();
            var rows = documents.Select(d => (d, _embedder.Embed(d.Text))).ToList();

            _store.ReplaceAll(_embedder.ModelId, _embedder.Dimension, rows);
            Initialize();

            return $"Indexed {rows.Count} phrasings with '{_embedder.ModelId}'.";
        }
        catch (Exception ex)
        {
            return $"Reindex failed [{ex.GetType().Name}]: {ex.Message}. Previous index left intact.";
        }
    }

    public string StatusText()
    {
        if (_store is null)
            return $"AI: unavailable ({Unavailable ?? "not initialized"})";

        var modelId = _embedder?.ModelId ?? Config.EmbeddingModelId;
        var status = _store.GetStatus(modelId);

        var state = Resolver is not null
            ? "enabled"
            : $"off ({Unavailable ?? "unknown reason"})";

        // Both gates are shown. They are independent — floor measures neighbour
        // agreement, minTopScore measures whether anything is actually close — and
        // Task 10 calibrates them separately, so reading one without the other cannot
        // explain why a given input was declined.
        return $"""
            AI: {state}
            Mode: {Config.ResolverMode}   floor: {Config.ConfidenceFloor:0.00}   minTopScore: {Config.MinTopScore:0.00}   topK: {Config.TopK}
            Model: {modelId}
            Index: {status.DocumentCount} phrasings{(status.ReindexRequired ? " - REINDEX REQUIRED ('ai reindex')" : "")}
            Built: {(status.BuiltUtc.HasValue ? status.BuiltUtc.Value.ToString("u") : "never")}
            Decisions logged: {DecisionLog.Count()}
            """;
    }
}
