using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeCommenterPro.Roslyn;

public class SemanticBusinessAnalyzer
{
    private readonly SemanticModel _model;

    public SemanticBusinessAnalyzer(SemanticModel model)
    {
        _model = model;
    }

    public List<string> AnalyzeMethod(MethodDeclarationSyntax method)
    {
        var findings = new List<string>();

        // Analyze invocations
        var invocations = method.DescendantNodes()
            .OfType<InvocationExpressionSyntax>();

        foreach (var inv in invocations)
        {
            var symbol = _model.GetSymbolInfo(inv).Symbol as IMethodSymbol;
            if (symbol == null)
                continue;

            var containing = symbol.ContainingType?.Name ?? string.Empty;

            if (containing.Contains("Repository", StringComparison.OrdinalIgnoreCase)
                || symbol.Name.Contains("Get")
                || symbol.Name.Contains("Find")
                || symbol.Name.Contains("Save")
                || symbol.Name.Contains("Delete"))
            {
                findings.Add($"Invokes {symbol.Name} on {containing}");
            }
        }

        // Detect throw statements
        var throws = method.DescendantNodes()
            .OfType<ThrowStatementSyntax>();

        if (throws.Any())
            findings.Add("Contains throw statements - consider validating inputs or handling errors");

        // Null checks and comparisons
        var binary = method.DescendantNodes().OfType<BinaryExpressionSyntax>();
        foreach (var b in binary)
        {
            if (b.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.EqualsExpression) ||
                b.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NotEqualsExpression))
            {
                if (b.Right?.ToString() == "null" || b.Left?.ToString() == "null")
                {
                    findings.Add("Performs null comparison - ensure null-safe behavior");
                    break;
                }
            }
        }

        // Return type semantics
        var returnType = _model.GetTypeInfo(method.ReturnType).Type;
        if (returnType != null)
        {
            if (returnType.Name == "Task")
                findings.Add("Returns Task - asynchronous operation");
            else if (returnType.SpecialType == SpecialType.System_Boolean)
                findings.Add("Returns boolean - indicates success/failure");
        }

        return findings.Distinct().ToList();
    }

    public List<string> AnalyzeConstructor(ConstructorDeclarationSyntax ctor)
    {
        var findings = new List<string>();

        // Detect throw statements
        var throws = ctor.DescendantNodes()
            .OfType<ThrowStatementSyntax>();

        if (throws.Any())
            findings.Add("Contains throw statements - consider validating inputs or handling errors");

        // Null checks and comparisons
        var binary = ctor.DescendantNodes().OfType<BinaryExpressionSyntax>();
        foreach (var b in binary)
        {
            if (b.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.EqualsExpression) ||
                b.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NotEqualsExpression))
            {
                if (b.Right?.ToString() == "null" || b.Left?.ToString() == "null")
                {
                    findings.Add("Performs null comparison - ensure null-safe behavior");
                    break;
                }
            }
        }

        return findings.Distinct().ToList();
    }
}
