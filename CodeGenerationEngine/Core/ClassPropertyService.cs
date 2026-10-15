using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Provides services for adding properties to existing classes.
/// Symmetric to ClassMethodService.
/// </summary>
public static class ClassPropertyService
{
    public static bool AddProperty(string workspaceRoot, string className,
        string propertyName, string propertyType, out string message)
    {
        message = string.Empty;

        // With no usable name the exists-check below built its pattern from an empty string,
        // matched any property at all and said "Property '' already exists."
        if (MemberNames.NotAnIdentifier(propertyName, "property") is { } bad)
        {
            message = bad.Replace("A new property", "A property", StringComparison.Ordinal)
                + " Type it as: add property Title string to Book";
            return false;
        }

        try
        {
            if (!ClassMethodEditService.TryLoadParts(workspaceRoot, className, out var parts, out message))
                return false;

            var part = parts[0];
            className = part.Node.Identifier.Text;

            // Every member of every partial declaration, in any case: a property named like a
            // method was CS0102, one named like the class CS0542.
            if (MemberNames.Taken(parts, propertyName, []) is { } taken)
            {
                message = taken + $" Property '{propertyName}' was not added.";
                return false;
            }

            // Bound in the class's project, as retype binds it: badtype was written (CS0246).
            var type = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseTypeName(propertyType ?? string.Empty);
            if (string.IsNullOrWhiteSpace(propertyType) || type.ContainsDiagnostics || type.ToString() != propertyType.Trim())
            {
                message = $"'{propertyType}' is not a C# type. Type it as: add property Title string to Book";
                return false;
            }

            if (!TypeResolution.Resolves(workspaceRoot, part.File, part.Node, type, out message, out var bound))
                return false;

            // A readonly struct cannot have a set accessor (CS8341); init is what it allows.
            var setter = part.Node.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ReadOnlyKeyword) ? "init" : "set";

            // With nullable reference types on, a string or a list with no initial value is
            // CS8618: it starts as null.
            var initializer = PropertyInitializers.CanHaveInitializer(part.Node)
                && PropertyInitializers.NullableOn(workspaceRoot, part.File, part.Root)
                && PropertyInitializers.SafeFor(type, bound) is { } value
                    ? $" = {value};"
                    : string.Empty;

            var propertyCode = $@"public {propertyType} {propertyName} {{ get; {setter}; }}{initializer}";

            if (!ClassMemberEditor.TryAddMember(part.File, className, propertyCode, out var error))
            {
                message = error;
                return false;
            }

            message = $"Property '{propertyName}' of type '{propertyType}' added to '{className}'.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = ex.Message;
            return false;
        }
    }

    public static bool RemoveProperty(string workspaceRoot, string className,
        string propertyName, out string message)
    {
        // Through the syntax tree, not the regex this once was: that regex matched the name in
        // any class in the file, left an initial value (`= "";`) behind as code that does not
        // compile, and its trailing \s* ate the next member's indentation.
        if (!ClassMethodEditService.TryLoadParts(workspaceRoot, className, out var parts, out message))
            return false;

        // Exact name first; the regex was case-insensitive, so a differently cased name still works.
        // In any of the class's partial files.
        if (ClassMethodEditService.PropertyNamed(parts, propertyName) is not (var part, var property))
        {
            message = ClassMethodEditService.NotFound(parts, propertyName, "Property");
            return false;
        }

        // As declared, for the messages: "item" found Item.
        className = part.Node.Identifier.Text;

        // Its own indent, line break and doc comment go with it; a directive or section
        // comment above it stays (see MemberRemoval).
        var root = MemberRemoval.Remove(part.Root, property);

        return ClassMethodEditService.TryWrite(part.File, root,
            $"Property '{property.Identifier.Text}' removed from '{className}'.", out message);
    }
}