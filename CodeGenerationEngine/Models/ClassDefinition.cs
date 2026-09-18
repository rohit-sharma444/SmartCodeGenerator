using CodeGenerationEngine.Core.Validation;

namespace CodeGenerationEngine.Models;

public class ClassDefinition
{
    public string Name { get; set; } = string.Empty;

    public List<PropertyDefinition> Properties { get; set; } = [];

    public List<MethodDefinition> Methods { get; set; } = [];

    /// <summary>
    /// Namespaces to emit as using directives. Needed because the generator emits none by
    /// default, so an attribute like <c>[JsonIgnore]</c> would otherwise not resolve.
    /// </summary>
    public List<string> Usings { get; set; } = [];

    /// <summary>Rules spanning more than one property — hand-stated via `add business
    /// validation`, or accepted from a Cross-Field/Calculation proposal. Reaches the Business
    /// stage in the emitted validator.</summary>
    public List<IValidationRule> BusinessRules { get; set; } = [];

    /// <summary>Rules on a confirmed status column -- forward-only order, no move from a
    /// final value -- built once the user has ordered an accepted Status proposal. Reaches
    /// the Workflow stage in the emitted validator.</summary>
    public List<IValidationRule> WorkflowRules { get; set; } = [];
}