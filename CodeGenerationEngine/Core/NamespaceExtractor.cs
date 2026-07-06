using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Service responsible for extracting the namespace from a C# syntax tree.
/// </summary>
public static class NamespaceExtractor
{
    public static string? GetNamespace(SyntaxTree tree)
    {
        var root = tree.GetRoot();

        var namespaceNode =
            root.DescendantNodes()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .FirstOrDefault();

        return namespaceNode?.Name.ToString();
    }
}