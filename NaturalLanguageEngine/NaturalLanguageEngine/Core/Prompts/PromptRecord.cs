using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageEngine.Core.Prompts;

/// <summary>
/// A single prompt (input sentence) submitted to the engine, together with
/// the analysis result produced for it and when it was submitted.
/// </summary>
public sealed class PromptRecord
{
    /// <summary>
    /// The raw sentence/text submitted to <see cref="NaturalLanguageEngine.Analyze"/>.
    /// </summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>
    /// When the prompt was submitted, in UTC.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// The result of analyzing <see cref="Prompt"/>. Null if analysis was not run
    /// (e.g. the prompt was rejected before reaching the engine).
    /// </summary>
    public SemanticResult? Result { get; set; }
}
