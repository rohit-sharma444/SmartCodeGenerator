namespace CodeGenerationEngine.Models;

public class MethodDefinition
{
    public string Name { get; set; } = string.Empty;
    public string ReturnType { get; set; } = "void";

    public List<ParameterDefinition> Parameters { get; set; } = [];
}

public class ParameterDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}