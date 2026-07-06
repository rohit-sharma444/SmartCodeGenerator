namespace CodeGenerationEngine.Models;
public class ProjectMetaData
{
    public bool Success { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public string ProjectPath { get; set; } = string.Empty;

    public string CsProjPath { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}