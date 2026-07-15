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
}