namespace NaturalLanguageEngine.Models.Diagnostics;

/// <summary>
/// Indicates the severity of a diagnostic produced during analysis.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>
    /// Informational message.
    /// </summary>
    Info = 0,

    /// <summary>
    /// Non-critical issue that does not stop analysis.
    /// </summary>
    Warning = 1,

    /// <summary>
    /// An error that prevents part of the analysis from completing.
    /// </summary>
    Error = 2,

    /// <summary>
    /// A critical failure that prevents further processing.
    /// </summary>
    Critical = 3
}