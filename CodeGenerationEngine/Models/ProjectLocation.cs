namespace CodeGenerationEngine.Models;

public sealed class ProjectLocation
{
    public string Directory { get; set; } = string.Empty;
    public string? ProjectFile { get; set; }
}