using CodeGenerationEngine.Models;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Service responsible for validating class names against C# naming conventions and reserved keywords.
/// </summary>
public static class ClassNameValidator
{
    private static readonly string[] ReservedKeywords =
    {
        "class", "public", "private", "void", "string",
        "int", "double", "namespace", "using"
    };

    public static ValidationResults Validate(string className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return ValidationResults.Fail("Class name cannot be empty.");

        if (!char.IsLetter(className[0]))
            return ValidationResults.Fail("Class name must start with a letter.");

        if (!Regex.IsMatch(className, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            return ValidationResults.Fail("Invalid class name.");

        if (ReservedKeywords.Contains(className.ToLower()))
            return ValidationResults.Fail("Class name is a reserved C# keyword.");

        return ValidationResults.Success();
    }
}