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
            if (!ClassMethodEditService.TryLoadParts(workspaceRoot, className, out var parts, out message))
                return false;

            // Exact name first, then another case of it, in any of the class's partial files, as
            // remove property finds it.
            if (ClassMethodEditService.PropertyNamed(parts, oldName) is not (var part, var property))
            {
                message = ClassMethodEditService.NotFound(parts, oldName, "Property");
                return false;
            }

            // As declared, for the messages: "item" found Item.
            className = part.Node.Identifier.Text;

            var name = property.Identifier.Text;

            if (MemberNames.NotAnIdentifier(newName, "property") is { } bad)
            {
                message = bad;
                return false;
            }

            if (parts.Any(p => p.Node.Members.OfType<PropertyDeclarationSyntax>()
                    .Any(x => x != property && x.Identifier.Text.Equals(newName, StringComparison.Ordinal))))
            {
                message = $"Property '{newName}' already exists in '{className}'.";
                return false;
            }

            if (MemberNames.Taken(parts, newName, [property]) is { } taken)
            {
                message = taken;
                return false;
            }

            var renamed = property.WithIdentifier(
                SyntaxFactory.Identifier(newName).WithTriviaFrom(property.Identifier));

            var root = part.Root.ReplaceNode(property, renamed);

            // ToFullString, not NormalizeWhitespace: renaming one identifier must not
            // reformat the user's whole file.
            Validation.SourceFileText.Rewrite(part.File, root.ToFullString());

            message = $"Property '{name}' renamed to '{newName}' in '{className}'. "
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
