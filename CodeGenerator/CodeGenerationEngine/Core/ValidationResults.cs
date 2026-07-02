using System;

namespace CodeGenerationEngine.Core;

public class ValidationResults
{
    public bool IsValid { get; private set; }
    public string Message { get; private set; } = string.Empty;

    private ValidationResults(bool isValid, string message = "")
    {
        IsValid = isValid;
        Message = message ?? string.Empty;
    }

    public static ValidationResults Success() => new ValidationResults(true);

    public static ValidationResults Fail(string message) => new ValidationResults(false, message ?? string.Empty);
}