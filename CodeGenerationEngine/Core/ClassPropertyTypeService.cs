using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Changes the declared type of a property in one class. Scope matches
/// <see cref="ClassPropertyRenameService"/>: the declaration only.
/// </summary>
public static class ClassPropertyTypeService
{
    public static bool ChangeType(
        string workspaceRoot, string className, string propertyName, string newType, out string message)
    {
        var type = SyntaxFactory.ParseTypeName(newType ?? string.Empty);

        // ParseTypeName always returns a node; a stray character or an unclosed generic
        // shows as diagnostics or as text it stopped short of, and either would be written
        // into the file as code that does not compile.
        if (string.IsNullOrWhiteSpace(newType) || type.ContainsDiagnostics
            || type.ToString() != newType.Trim())
        {
            message = $"'{newType}' is not a C# type.";
            return false;
        }

        if (!ClassMethodEditService.TryLoad(workspaceRoot, className, out var file, out var root, out var classNode, out message))
            return false;

        var property = classNode.Members.OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault(p => p.Identifier.Text.Equals(propertyName, StringComparison.Ordinal));

        if (property == null)
        {
            message = $"Property '{propertyName}' not found in '{className}'.";
            return false;
        }

        if (property.Type.ToString() == type.ToString())
        {
            message = $"'{className}.{propertyName}' is already {type}.";
            return true;
        }

        var retyped = property.WithType(type.WithTriviaFrom(property.Type));

        // An initial value was written for the old type ("" for a string), and the new type
        // usually cannot hold it — so it goes, and the message says so.
        var dropped = property.Initializer != null;
        if (dropped)
            retyped = retyped
                .WithInitializer(null)
                .WithSemicolonToken(default)
                // The line break after the `;` would go with it.
                .WithAccessorList(retyped.AccessorList!.WithTrailingTrivia(property.SemicolonToken.TrailingTrivia));

        root = root.ReplaceNode(property, retyped);

        return ClassMethodEditService.TryWrite(file, root,
            $"'{className}.{propertyName}' changed from {property.Type} to {type}."
            + (dropped ? $" Its initial value ({property.Initializer!.Value}) was removed." : string.Empty),
            out message);
    }
}
