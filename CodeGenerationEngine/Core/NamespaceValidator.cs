using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Service responsible for validating namespace declarations in the source code.
/// </summary>
public static class NamespaceValidator
{
    public static IEnumerable<NamespaceValidationIssue> Validate(Compilation compilation)
    {
        var issues = new List<NamespaceValidationIssue>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var ns = NamespaceExtractor.GetNamespace(tree);

            if (string.IsNullOrWhiteSpace(ns))
            {
                issues.Add(new NamespaceValidationIssue
                {
                    Message = "Missing namespace declaration",

                    Severity = "Error"
                });
            }
        }

        return issues;
    }
}