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

        var file = Directory
            .GetFiles(workspaceRoot, $"{className}.cs", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (file == null)
        {
            message = $"Class '{className}' not found.";
            return false;
        }

        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();

        // By name: a file may hold several types, and the base type belongs to the one
        // the user named, not to whichever is declared first.
        var classNode = root
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text.Equals(className, StringComparison.OrdinalIgnoreCase));

        if (classNode == null)
        {
            message = $"Class '{className}' not found in {Path.GetFileName(file)}.";
            return false;
        }

        if (classNode.BaseList is not null && classNode.BaseList.Types.Any(t =>
                t.Type.ToString().Equals(baseClass, StringComparison.OrdinalIgnoreCase)))
        {
            message = $"Class '{className}' already inherits from '{baseClass}'.";
            return false;
        }

        var baseType = SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(baseClass));

        // Insert first, never append: `class Order : IAuditable, AuditableBase` is CS1722.
        // WithBaseList used to REPLACE the list, silently dropping existing interfaces.
        var baseList = classNode.BaseList is null
            ? SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(baseType))
            : classNode.BaseList.WithTypes(classNode.BaseList.Types.Insert(0, baseType));

        root = root.ReplaceNode(classNode, classNode.WithBaseList(baseList));

        File.WriteAllText(file, root.NormalizeWhitespace().ToFullString());

        message = $"Class '{className}' now inherits from '{baseClass}'.";
        return true;
    }
}