namespace CodeGenerationEngine.Semantics.Models;

public sealed class SemanticIssue
{
    public string Message { get; init; } = string.Empty;

    public SemanticIssueSeverity Severity { get; init; }
}