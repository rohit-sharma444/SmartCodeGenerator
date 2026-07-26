using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Renames a property declaration in one class. Scope matches
/// <see cref="ClassRenameService"/>: the declaration only, no reference updates
/// anywhere. The success message says so, because silently renaming a declaration
/// out from under its call sites is worse when it is not stated.
/// </summary>
public static class ClassPropertyRenameService
{
    public static bool RenameProperty(
        string workspaceRoot,
        string className,
        string oldName,
        string newName,
        out string message)
    {
        message = string.Empty;

        try
        {
            var file = Directory
                .GetFiles(workspaceRoot, "*.cs", SearchOption.AllDirectories)
                .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                    .Equals(className, StringComparison.OrdinalIgnoreCase));

            if (file == null)
            {
                message = $"Class '{className}' not found.";
                return false;
            }

            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();

            var classNode = root
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text.Equals(className, StringComparison.OrdinalIgnoreCase));

            if (classNode == null)
            {
                message = $"Class '{className}' not found in {Path.GetFileName(file)}.";
                return false;
            }

            var properties = classNode.Members.OfType<PropertyDeclarationSyntax>().ToList();

            var property = properties.FirstOrDefault(p => p.Identifier.Text.Equals(oldName, StringComparison.Ordinal));

            if (property == null)
            {
                message = $"Property '{oldName}' not found in '{className}'.";
                return false;
            }

            if (properties.Any(p => p.Identifier.Text.Equals(newName, StringComparison.Ordinal)))
            {
                message = $"Property '{newName}' already exists in '{className}'.";
                return false;
            }

            var renamed = property.WithIdentifier(
                SyntaxFactory.Identifier(newName).WithTriviaFrom(property.Identifier));

            root = root.ReplaceNode(property, renamed);

            // ToFullString, not NormalizeWhitespace: renaming one identifier must not
            // reformat the user's whole file.
            File.WriteAllText(file, root.ToFullString());

            message = $"Property '{oldName}' renamed to '{newName}' in '{className}'. "
                + "References elsewhere were not updated.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = ex.Message;
            return false;
        }
    }
}
