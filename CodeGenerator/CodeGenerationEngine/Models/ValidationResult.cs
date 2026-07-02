using Microsoft.CodeAnalysis;

namespace CodeGenerationEngine.Models;

public class ValidationResult
{
    public ValidationResult() { }

    public bool IsValid { get; set; }

    public List<Diagnostic> Diagnostics { get; set; } = [];
}


public class ValidationResults
{
    public bool IsValid { get; set; }

    public string Message { get; set; } = string.Empty;

    public static ValidationResults Success()
    {
        return new ValidationResults
        {
            IsValid = true
        };
    }

    public static ValidationResults Fail(string message)
    {
        return new ValidationResults
        {
            IsValid = false,
            Message = message
        };
    }
}