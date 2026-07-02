namespace CodeGenerationEngine.Models;

public sealed class SemanticContext
{
    public string? CurrentProject { get; set; }
    public string? CurrentClass { get; set; }
    public string? CurrentNamespace { get; set; }
    public string? CurrentFile { get; set; }
    public string? LastEntity { get; set; }
}