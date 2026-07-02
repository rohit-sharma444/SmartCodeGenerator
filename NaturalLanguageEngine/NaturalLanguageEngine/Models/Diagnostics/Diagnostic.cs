namespace NaturalLanguageEngine.Models.Diagnostics;

/// <summary>
/// Represents a diagnostic produced during NLP analysis.
/// </summary>
public sealed class Diagnostic
{
    /// <summary>
    /// Unique diagnostic identifier (e.g. NLP0001).
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Diagnostic severity.
    /// </summary>
    public DiagnosticSeverity Severity { get; init; }

    /// <summary>
    /// Human-readable diagnostic message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Character position in the original text, if applicable.
    /// </summary>
    public int? Position { get; init; }

    /// <summary>
    /// Length of the affected text, if applicable.
    /// </summary>
    public int? Length { get; init; }
}