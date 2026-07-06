using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

public static class ClassInheritanceService
{
    public static bool AddInheritance(
        string workspaceRoot,
        string className,
        string baseClass,
        out string message)
    {
        message = string.Empty;

        var file = Directory.GetFiles(
                workspaceRoot,
                $"{className}.cs",
                SearchOption.AllDirectories)
            .FirstOrDefault();

        if (file == null)
        {
            message = $"Class '{className}' not found.";
            return false;
        }

        var code = File.ReadAllText(file);

        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        var classNode = root
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault();

        if (classNode == null)
        {
            message = "Class declaration not found.";
            return false;
        }

        var updatedClass = classNode.WithBaseList(
            SyntaxFactory.BaseList(
                SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                    SyntaxFactory.SimpleBaseType(
                        SyntaxFactory.ParseTypeName(baseClass)))));

        root = root.ReplaceNode(classNode, updatedClass);

        File.WriteAllText(
            file,
            root.NormalizeWhitespace().ToFullString());

        message =
            $"Class '{className}' now inherits from '{baseClass}'.";

        return true;
    }
}