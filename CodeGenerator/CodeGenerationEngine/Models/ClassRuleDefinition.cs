namespace CodeGenerationEngine.Models;

public class ClassRuleDefinition
{
    public string ClassName { get; set; } = string.Empty;

    public List<PropertyRule> Properties { get; set; } = new List<PropertyRule>();
}