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

    /// <summary>
    /// The phrasings this parser accepts, each a complete rule fragment.
    /// </summary>
    /// <remarks>
    /// Stated by the parser rather than listed anywhere else, because the validation page shows
    /// this list to someone whose sentence was refused. A hand-kept list beside a growing catalog
    /// is a list that tells people to type things the tool no longer understands.
    /// </remarks>
    IReadOnlyList<string> Examples { get; }

    bool TryParse(ValidationParseContext ctx, out IValidationRule? rule);
}
