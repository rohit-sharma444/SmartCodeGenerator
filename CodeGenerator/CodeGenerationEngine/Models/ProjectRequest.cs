namespace CodeGenerationEngine.Models;

public class ProjectRequest
{
    public string ProjectName { get; set; } = string.Empty;

    public string OutputFolder { get; set; } = string.Empty;

    public DotNetProjectType ProjectType { get; set; }

    public string Framework { get; set; } = "net8.0";
}