namespace CodeGenerationEngine.Models;

public sealed class SemanticEntity
{
    public SemanticEntityType Type { get; init; }
    public string ParentName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int Position { get; set; }
    public double Confidence { get; set; }
    public List<string> Evidence { get; } = [];
}