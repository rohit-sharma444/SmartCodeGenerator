namespace CodeGenerationEngine.Models;

public class TypeInferenceRule
{
    public string TypeName { get; set; } = string.Empty;

    public List<string> ExactMatches { get; set; } = [];

    public List<string> StartsWith { get; set; } = [];

    public List<string> EndsWith { get; set; } = [];

    public List<string> Contains { get; set; } = [];
}