using System.Text.Json;

namespace CodeIntelligence;

/// <summary>ai.json, kept beside intents.json in the binary folder — the same place
/// and pattern IntentRepository already uses. Defaults are ON: phase A shipped dark, which
/// left every customer's copy never using it, nor any phrase learned into its corpus. It
/// cannot act on a reading without asking (AiConfirmPrompt), and a piped run never uses it.
/// A file that says Enabled false, or cannot be read, keeps it off.</summary>
public sealed class AiConfig
{
    /// <summary>The shipped thresholds, as constants so `GoldenSetTests` can measure
    /// the values that actually ship. Written as two literals in two files they would
    /// drift apart silently, and the accuracy gate would then be guarding numbers
    /// nobody runs. Both were calibrated by the golden-set sweep — see the spec's
    /// "On thresholds" section for the measurements behind them.</summary>
    public const double DefaultConfidenceFloor = 0.55;

    /// <summary>Raised from the provisional 0.35 by the golden-set sweep. 0.35 admitted
    /// 14 wrong-and-confident readings out of 69; 0.55 cuts that to 1.</summary>
    public const double DefaultMinTopScore = 0.55;

    /// <summary>2, not 5, and this is the single most important number here — the sweep
    /// found topK dominates both gates.
    ///
    /// The corpus has very uneven per-intent coverage (ListProperties has 2 phrasings,
    /// CreateProperty has many). Summing scores over 5 neighbours lets a
    /// document-rich intent outvote a sparse one whose single nearest neighbour was the
    /// right answer — measured: "what properties does Customer have" retrieves
    /// "show properties" (ListProperties) as its closest match and still returns
    /// CreateProperty. Dropping to 2 removes most of that swamping.
    ///
    /// NOT 1, which scored best on raw accuracy (45/69). With one neighbour the vote is
    /// unanimous by construction, so confidence is always 1.0 and ConfidenceFloor stops
    /// existing — every floor value in the sweep produced identical results at topK 1.
    /// That would also destroy the decline-on-ambiguity behaviour the corpus dedup was
    /// deliberately built around: two documents with the same text and different intents
    /// are kept so the vote splits and the resolver backs off. 2 is the smallest k that
    /// keeps both gates alive.</summary>
    public const int DefaultTopK = 2;

    public bool Enabled { get; set; } = true;
    public string ResolverMode { get; set; } = "Retrieval";
    public double ConfidenceFloor { get; set; } = DefaultConfidenceFloor;

    /// <summary>Gate 1: the minimum absolute cosine the nearest neighbour must reach
    /// before any resolution is offered. Independent of ConfidenceFloor, which only
    /// measures neighbour agreement — see NeighbourVote.</summary>
    public double MinTopScore { get; set; } = DefaultMinTopScore;

    public int TopK { get; set; } = DefaultTopK;
    public string EmbeddingModelId { get; set; } = "all-MiniLM-L6-v2-int8";
    public int RetrievalTimeoutMs { get; set; } = 500;

    public static string DefaultPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai.json");

    /// <summary>Never throws. A missing file yields the defaults; an unreadable one yields
    /// them with the layer OFF, because a broken config must leave the CLI working exactly as
    /// it does with AI off - and it may well have been the file that said so.</summary>
    public static AiConfig Load(string? path = null)
    {
        path ??= DefaultPath;

        try
        {
            if (!File.Exists(path))
                return new AiConfig();

            return JsonSerializer.Deserialize<AiConfig>(File.ReadAllText(path)) ?? Off();
        }
        catch (Exception)
        {
            return Off();
        }

        static AiConfig Off() => new() { Enabled = false };
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
