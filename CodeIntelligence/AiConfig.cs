using System.Text.Json;

namespace CodeIntelligence;

/// <summary>ai.json, kept beside intents.json in the binary folder — the same place
/// and pattern IntentRepository already uses. Defaults are OFF: phase A ships dark and
/// is switched on per machine once its decision log shows real accuracy.</summary>
public sealed class AiConfig
{
    public bool Enabled { get; set; }
    public string ResolverMode { get; set; } = "Retrieval";
    public double ConfidenceFloor { get; set; } = 0.55;

    /// <summary>Gate 1: the minimum absolute cosine the nearest neighbour must reach
    /// before any resolution is offered. Independent of ConfidenceFloor, which only
    /// measures neighbour agreement — see NeighbourVote. Provisional; Task 10 calibrates.</summary>
    public double MinTopScore { get; set; } = 0.35;

    public int TopK { get; set; } = 5;
    public string EmbeddingModelId { get; set; } = "all-MiniLM-L6-v2-int8";
    public int RetrievalTimeoutMs { get; set; } = 500;

    public static string DefaultPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai.json");

    /// <summary>Never throws. A missing or unreadable file yields defaults, because a
    /// broken config must leave the CLI working exactly as it does with AI off.</summary>
    public static AiConfig Load(string? path = null)
    {
        path ??= DefaultPath;

        try
        {
            if (!File.Exists(path))
                return new AiConfig();

            return JsonSerializer.Deserialize<AiConfig>(File.ReadAllText(path)) ?? new AiConfig();
        }
        catch (Exception)
        {
            return new AiConfig();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;

        try
        {
            File.WriteAllText(path,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // Saving config must never take down the REPL.
        }
    }
}
