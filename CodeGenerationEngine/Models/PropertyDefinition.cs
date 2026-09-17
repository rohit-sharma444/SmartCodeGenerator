namespace CodeGenerationEngine.Models;

public class PropertyDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    // Inline rules parsed from the natural-language token (e.g. "Name string required min length 3")
    public List<PropertyRule> Rules { get; set; } = new List<PropertyRule>();

    /// <summary>
    /// Attribute names to emit on this property, without brackets (e.g. <c>JsonIgnore</c>). The
    /// owning <see cref="ClassDefinition"/> must declare the matching using, or the generated file
    /// will not compile.
    /// </summary>
    public List<string> Attributes { get; set; } = new List<string>();

    /// <summary>
    /// What the source declared about this property's column, or null when nothing is known.
    /// Read by DomainModelMapper.InferValidation to derive rules from the type rather than the
    /// name.
    /// </summary>
    public TypeFacts? Facts { get; set; }

    /// <summary>Rules the data supports, carried from the candidate field so the accepted ones
    /// can be applied where every other rule is recorded.</summary>
    public List<RuleProposal> Proposals { get; set; } = new();

    public override string ToString() => $"{Type} {Name}";
}