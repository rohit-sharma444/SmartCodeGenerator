using CodeGenerationEngine.Models;

namespace CodeIntelligence.Abstractions;

/// <summary>One corpus entry that supported a resolution, with its cosine score.</summary>
public sealed record RetrievedExample(long DocumentId, string Text, IntentType Intent, double Score);

/// <summary>An AI-proposed reading of input the rule-based parser could not recognize.
/// <paramref name="Confidence"/> is the neighbour-vote margin in [0,1]; Evidence is the
/// retrieved support, kept so `ai why` can explain the decision after the fact.</summary>
public sealed record AiResolution(
    IntentType Intent,
    double Confidence,
    IReadOnlyList<RetrievedExample> Evidence,
    string? Rationale);
