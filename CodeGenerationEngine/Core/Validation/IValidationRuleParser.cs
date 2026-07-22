namespace CodeGenerationEngine.Core.Validation;

/// <summary>Recognizes one rule type's natural-language phrasing. Implementors are
/// auto-discovered by <see cref="ValidationRuleCatalog"/>; add a new type by
/// dropping a new implementation into this folder — no registration edits.</summary>
public interface IValidationRuleParser
{
    /// <summary>Lower runs first. Keeps a broad catch-all (e.g. allowed-values)
    /// from swallowing a more specific pattern.</summary>
    int Priority { get; }
    ValidationCategory Category { get; }
    bool TryParse(ValidationParseContext ctx, out IValidationRule? rule);
}
