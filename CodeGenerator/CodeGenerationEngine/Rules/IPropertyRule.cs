using CodeGenerationEngine.Models;

public interface IPropertyRule
{
    bool IsMatch(PropertyDefinition property);

    IEnumerable<string> Validate(PropertyDefinition property);
}
