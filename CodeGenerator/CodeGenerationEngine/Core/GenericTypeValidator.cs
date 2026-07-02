using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Service responsible for validating generic type usages in the source code.
/// </summary>
public static class GenericTypeValidator
{
    public static List<GenericValidationIssue> Validate(Compilation compilation)
    {
        var issues = new List<GenericValidationIssue>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);

            var root = tree.GetRoot();

            ValidateNodes(root, model, issues);
        }

        return issues;
    }


    private static void ValidateNodes(SyntaxNode root, Microsoft.CodeAnalysis.SemanticModel model, List<GenericValidationIssue> issues)
    {
        var genericTypes = root.DescendantNodes().OfType<GenericNameSyntax>();

        foreach (var genericType in genericTypes)
            ValidateGenericType(genericType, model, issues);
    }


    private static void ValidateGenericType(GenericNameSyntax genericType,
    Microsoft.CodeAnalysis.SemanticModel model, List<GenericValidationIssue> issues)
    {
        var symbol = model.GetSymbolInfo(genericType).Symbol;

        if (symbol == null)
        {
            issues.Add(new GenericValidationIssue
            {
                TypeName = genericType.ToString(),
                Message = "Unable to resolve generic type",
                Severity = "Error"
            });
        }
    }
}