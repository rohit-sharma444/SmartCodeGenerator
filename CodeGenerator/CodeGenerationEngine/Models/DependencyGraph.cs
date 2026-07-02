namespace CodeGenerationEngine.Models;

public class DependencyGraph
{
    public Dictionary<string, HashSet<string>> Dependencies
    { get; } = [];
}