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
        => AddProperty(workspaceRoot, className, propertyName, propertyType,
            PropertyModifiers.None, null, out message);

    /// <summary>
    /// Adds a property with the modifiers the command gave and, when <paramref name="defaultValue"/>
    /// is not null, that value as typed: it must be a value of the type, or the property is refused
    /// and nothing is written. The kind of the class still applies (a static class, a record or a
    /// readonly struct), with the command's modifiers added to it.
    /// </summary>
    public static bool AddProperty(string workspaceRoot, string className,
        string propertyName, string propertyType, PropertyModifiers modifiers, string? defaultValue,
        out string message)
    {
        message = string.Empty;
        var typed = modifiers;

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

            // A static class holds only static members (CS0708).
            if (part.Node.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword))
                modifiers |= PropertyModifiers.Static;

            if (PropertyModifierRules.Refusal(modifiers, propertyName) is { } refusal)
            {
                message = refusal;
                return false;
            }

            // A readonly struct cannot have a set accessor (CS8341); init is what it allows.
            // A record's properties are init too, as create record writes them. An init accessor
            // cannot be static, so a static property keeps set.
            if ((part.Node is RecordDeclarationSyntax
                    || part.Node.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ReadOnlyKeyword))
                && !modifiers.HasFlag(PropertyModifiers.Static))
                modifiers |= PropertyModifiers.ReadOnly;

            string? initial = null;
            if (defaultValue is not null)
            {
                // C# 11 refuses an initializer in a struct that declares no constructor (CS8983).
                if (part.Node is StructDeclarationSyntax)
                {
                    message = DefaultValues.StructRefusal(className);
                    return false;
                }

                var folder = ProjectFileHelper.FindProjectDirectory(part.File);
                if (!DefaultValues.TryExpression(propertyType, defaultValue, folder, out var expression, out var valueError))
                {
                    message = DefaultValues.ValueRefusal(valueError!, propertyName);
                    return false;
                }

                initial = expression;
            }

            // With nullable reference types on, a string or a list with no initial value is
            // CS8618: it starts as null.
            var initializer = initial is not null
                ? $" = {initial};"
                : PropertyInitializers.CanHaveInitializer(part.Node)
                    && PropertyInitializers.NullableOn(workspaceRoot, part.File, part.Root,
                        PropertyInitializers.InsertionPoint(part.Node))
                    && PropertyInitializers.SafeFor(type, bound) is { } value
                        ? $" = {value};"
                        : string.Empty;

            var propertyCode = $"{PropertyModifierRules.Prefix(modifiers)}{propertyType} {propertyName} "
                + $"{PropertyModifierRules.Accessors(modifiers)}{initializer}";

            if (!ClassMemberEditor.TryAddMember(part.File, className, propertyCode, out var error))
            {
                message = error;
                return false;
            }

            var words = string.Join(' ', Enum.GetValues<PropertyModifiers>()
                .Where(m => m != PropertyModifiers.None && typed.HasFlag(m))
                .Select(m => m.ToString().ToLowerInvariant()));
            message = $"Property '{propertyName}' of type '{propertyType}' added to '{className}'"
                + (initial is null ? string.Empty : $" (default {initial})")
                + (words.Length == 0 ? string.Empty : $" ({words})")
                + ".";
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