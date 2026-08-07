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
    private AiLayer(AiConfig config, string configPath, string indexPath)
    {
        Config = config;
        ConfigPath = configPath;
        IndexPath = indexPath;
        DecisionLog = new DecisionLog(indexPath);
    }

    public AiConfig Config { get; }

    /// <summary>Where this layer's config was loaded from, so `ai enable` writes back
    /// to the same file. Without it a layer built on a custom path would silently save
    /// to AiConfig.DefaultPath — harmless in production, where Create() is called with
    /// null, but it makes the toggle untestable and would surprise any future caller
    /// that passes a path.</summary>
    public string ConfigPath { get; }

    public string IndexPath { get; }
    public DecisionLog DecisionLog { get; }
    public IIntentResolver? Resolver { get; private set; }
    public string? Unavailable { get; private set; }

    private IEmbeddingProvider? _embedder;
    private SqliteVectorStore? _store;

    public static AiLayer Create(string? configPath = null, string? indexPath = null)
    {
        configPath ??= AiConfig.DefaultPath;
        var config = AiConfig.Load(configPath);
        indexPath ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai-index.db");

        var layer = new AiLayer(config, configPath, indexPath);
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

    /// <summary>A resolver over a caller's own closed vocabulary, sharing this layer's
    /// embedder and calibrated gates. Null when AI is off or unavailable, which callers
    /// treat as "no suggestion" — the same degradation as a null <see cref="Resolver"/>.
    ///
    /// Gated on <see cref="Resolver"/> rather than just the embedder: if the user ran
    /// `ai disable`, no surface should be quietly using AI anyway. The embedder stays
    /// private; composing things out of it is this class's job.</summary>
    /// <remarks>Thresholds are the CALLER's, deliberately not this layer's. Task 10
    /// established that gates are calibrated against a specific corpus; a small closed
    /// vocabulary behaves nothing like the 171-phrase intent corpus, and reusing
    /// AiConfig's values there measurably rejected correct matches.</remarks>
    public PhraseResolver? CreatePhraseResolver(
        IReadOnlyList<(string Text, string Label)> phrases,
        int topK, double floor, double minTopScore)
    {
        try
        {
            if (_embedder is null || Resolver is null || phrases.Count == 0)
                return null;

            return new PhraseResolver(_embedder, phrases, topK, floor, minTopScore);
        }
        catch (Exception ex)
        {
            Unavailable = $"{ex.GetType().Name}: {ex.Message}";
            return null;
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
