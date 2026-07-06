using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Service responsible for extracting class definitions from natural language input.
/// </summary>
public static class ClassExtractor
{
    public static IEnumerable<ClassInfo> Extract(SyntaxTree tree, string filePath)
    {
        var root = tree.GetRoot();

        var namespaces = root.DescendantNodes().OfType<NamespaceDeclarationSyntax>();

        foreach (var ns in namespaces)
        {
            foreach (var cls in ns.Members.OfType<ClassDeclarationSyntax>())
            {
                yield return new ClassInfo
                {
                    Namespace = ns.Name.ToString(),
                    Name = cls.Identifier.Text,
                    IsPartial = cls.Modifiers.Any(x => x.Text == "partial"),
                    FilePath = filePath
                };
            }
        }
    }
}