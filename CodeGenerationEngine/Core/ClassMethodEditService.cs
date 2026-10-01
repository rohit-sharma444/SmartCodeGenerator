using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Deletes or renames a method declared in one class. Scope matches
/// <see cref="ClassPropertyRenameService"/>: the declaration only, no reference updates,
/// and the file is written back with <c>ToFullString</c> so the user's layout survives.
/// </summary>
public static class ClassMethodEditService
{
    /// <summary>
    /// Removes the method. Refuses when the name is overloaded: which one the user meant
    /// cannot be told from a name, and deleting all of them would take more than was asked.
    /// </summary>
    public static bool DeleteMethod(string workspaceRoot, string className, string methodName, out string message)
    {
        if (!TryLoad(workspaceRoot, className, out var file, out var root, out var classNode, out message))
            return false;

        var matches = MethodsNamed(classNode, methodName);

        if (matches.Count == 0)
        {
            message = $"Method '{methodName}' not found in '{className}'.";
            return false;
        }

        if (matches.Count > 1)
        {
            message = $"'{className}' has {matches.Count} methods named '{methodName}', so none was removed:"
                + string.Concat(matches.Select(m => Environment.NewLine + "  " + Signature(m)));
            return false;
        }

        root = MemberRemoval.Remove(root, matches[0]);

        return TryWrite(file, root, $"Method '{methodName}' removed from '{className}'.", out message);
    }

    /// <summary>
    /// Renames the method — every overload of it, since they share the one name.
    /// </summary>
    public static bool RenameMethod(
        string workspaceRoot, string className, string oldName, string newName, out string message)
    {
        if (!TryLoad(workspaceRoot, className, out var file, out var root, out var classNode, out message))
            return false;

        var matches = MethodsNamed(classNode, oldName);

        if (matches.Count == 0)
        {
            message = $"Method '{oldName}' not found in '{className}'.";
            return false;
        }

        if (MemberNames.NotAnIdentifier(newName, "method") is { } bad)
        {
            message = bad;
            return false;
        }

        if (MethodsNamed(classNode, newName).Count > 0)
        {
            message = $"Method '{newName}' already exists in '{className}'.";
            return false;
        }

        if (MemberNames.Taken(classNode, newName, matches) is { } taken)
        {
            message = taken;
            return false;
        }

        root = root.ReplaceNodes(matches, (original, _) =>
            original.WithIdentifier(SyntaxFactory.Identifier(newName).WithTriviaFrom(original.Identifier)));

        var overloads = matches.Count > 1 ? $" ({matches.Count} overloads)" : string.Empty;

        return TryWrite(file, root,
            $"Method '{oldName}' renamed to '{newName}' in '{className}'{overloads}. "
            + "References elsewhere were not updated.",
            out message);
    }

    private static List<MethodDeclarationSyntax> MethodsNamed(ClassDeclarationSyntax classNode, string name) =>
        classNode.Members.OfType<MethodDeclarationSyntax>()
            .Where(m => m.Identifier.Text.Equals(name, StringComparison.Ordinal))
            .ToList();

    private static string Signature(MethodDeclarationSyntax method) =>
        $"{method.ReturnType} {method.Identifier.Text}{method.ParameterList}";

    // Shared with ClassPropertyTypeService: find the class's file by name, then the class in it.
    internal static bool TryLoad(
        string workspaceRoot, string className,
        out string file, out SyntaxNode root, out ClassDeclarationSyntax classNode, out string message)
    {
        file = string.Empty;
        root = null!;
        classNode = null!;
        message = string.Empty;

        try
        {
            var found = Directory
                .GetFiles(workspaceRoot, "*.cs", SearchOption.AllDirectories)
                .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                    .Equals(className, StringComparison.OrdinalIgnoreCase));

            if (found == null)
            {
                message = $"Class '{className}' not found.";
                return false;
            }

            file = found;
            root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();

            var node = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text.Equals(className, StringComparison.OrdinalIgnoreCase));

            if (node == null)
            {
                message = $"Class '{className}' not found in {Path.GetFileName(file)}.";
                return false;
            }

            classNode = node;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = ex.Message;
            return false;
        }
    }

    internal static bool TryWrite(string file, SyntaxNode root, string success, out string message)
    {
        try
        {
            File.WriteAllText(file, root.ToFullString());
            message = success;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = ex.Message;
            return false;
        }
    }
}
