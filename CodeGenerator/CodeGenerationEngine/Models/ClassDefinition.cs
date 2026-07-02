namespace CodeGenerationEngine.Models;

public class ClassDefinition
{
    public string Name { get; set; } = string.Empty;

    public List<PropertyDefinition> Properties { get; set; } = [];

    public List<MethodDefinition> Methods { get; set; } = [];
}