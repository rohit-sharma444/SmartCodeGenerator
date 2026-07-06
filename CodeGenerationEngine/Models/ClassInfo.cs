namespace CodeGenerationEngine.Models;

public class ClassInfo
{
    public string Namespace { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsPartial { get; set; }

    public string FilePath { get; set; } = string.Empty;
}