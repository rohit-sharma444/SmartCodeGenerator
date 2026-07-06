namespace CodeGenerationEngine.Models;

public class DuplicateClassIssue
{
    public string Namespace { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}