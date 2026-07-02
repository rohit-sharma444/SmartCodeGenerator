namespace CodeGenerationEngine.Models;

public class PropertyDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    // Inline rules parsed from the natural-language token (e.g. "Name string required min length 3")
    public List<PropertyRule> Rules { get; set; } = new List<PropertyRule>();

    public override string ToString() => $"{Type} {Name}";
}