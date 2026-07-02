namespace CodeGenerationEngine.Models;

public class NamespaceValidationIssue
{
    public string FileName { get; set; } = string.Empty;

    public string Namespace { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;
}