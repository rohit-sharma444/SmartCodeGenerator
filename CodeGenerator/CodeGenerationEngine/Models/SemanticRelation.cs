namespace CodeGenerationEngine.Models;

public sealed class SemanticRelation
{
    public string Source { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public SemanticRelationType RelationType { get; init; }
}