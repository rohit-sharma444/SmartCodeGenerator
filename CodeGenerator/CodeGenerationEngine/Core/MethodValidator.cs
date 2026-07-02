using CodeGenerationEngine.Models;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Validates method definitions before code generation.
/// </summary>
public static class MethodValidator
{
    private static readonly HashSet<string> ReservedKeywords =
    [
        "abstract","as","base","bool","break","byte","case","catch",
        "char","checked","class","const","continue","decimal","default",
        "delegate","do","double","else","enum","event","explicit",
        "extern","false","finally","fixed","float","for","foreach",
        "goto","if","implicit","in","int","interface","internal",
        "is","lock","long","namespace","new","null","object",
        "operator","out","override","params","private","protected",
        "public","readonly","ref","return","sbyte","sealed","short",
        "sizeof","stackalloc","static","string","struct","switch",
        "this","throw","true","try","typeof","uint","ulong",
        "unchecked","unsafe","ushort","using","virtual","void",
        "volatile","while"
    ];

    public static ValidationResults Validate(MethodDefinition method)
    {
        if (method == null)
            return ValidationResults.Fail("Method definition missing.");

        if (string.IsNullOrWhiteSpace(method.Name))
            return ValidationResults.Fail("Method name missing.");

        method.Name = method.Name.Trim();

        method.Name = NormalizeMethodName(method.Name);

        if (!Regex.IsMatch(method.Name, @"^[A-Za-z_][A-Za-z0-9_]*$"))
        {
            return ValidationResults.Fail(
                $"Invalid method name '{method.Name}'.");
        }

        if (ReservedKeywords.Contains(method.Name))
        {
            return ValidationResults.Fail(
                $"'{method.Name}' is a reserved C# keyword.");
        }

        if (char.IsLower(method.Name[0]))
        {
            return ValidationResults.Fail(
                $"Method '{method.Name}' should use PascalCase.");
        }

        if (method.Name.Length > 100)
        {
            return ValidationResults.Fail(
                "Method name is too long.");
        }

        if (method.Parameters != null)
        {
            var duplicateParameters =
                method.Parameters
                    .GroupBy(p => p.Name,
                        StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(g => g.Count() > 1);

            if (duplicateParameters != null)
            {
                return ValidationResults.Fail(
                    $"Duplicate parameter '{duplicateParameters.Key}'.");
            }
        }

        return ValidationResults.Success();
    }

    public static string NormalizeMethodName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        name = Regex.Replace(name, @"[^A-Za-z0-9_]", "");

        return char.ToUpper(name[0]) + name[1..];
    }

}