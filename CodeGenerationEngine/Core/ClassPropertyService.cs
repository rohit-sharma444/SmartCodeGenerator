using System.Text.RegularExpressions;
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

        try
        {
            var file = Directory.GetFiles(workspaceRoot, "*.cs",
                    SearchOption.AllDirectories).FirstOrDefault(f =>
                    Path.GetFileNameWithoutExtension(f)
                        .Equals(className, StringComparison.OrdinalIgnoreCase));

            if (file == null)
            {
                message = $"Class '{className}' not found.";
                return false;
            }

            var code = File.ReadAllText(file);

            var pattern = $@"\b{Regex.Escape(propertyName)}\b\s*\{{\s*get\s*;\s*set\s*;\s*\}}";
            if (Regex.IsMatch(code, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                message = $"Property '{propertyName}' already exists.";
                return false;
            }

            var propertyCode = $@"public {propertyType} {propertyName} {{ get; set; }}";

            if (!ClassMemberEditor.TryAddMember(file, className, propertyCode, out var error))
            {
                message = error;
                return false;
            }

            message = $"Property '{propertyName}' of type '{propertyType}' added to '{className}'.";
            return true;
        }
        catch (Exception ex)
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
        if (!ClassMethodEditService.TryLoad(workspaceRoot, className, out var file, out var root, out var classNode, out message))
            return false;

        var properties = classNode.Members.OfType<PropertyDeclarationSyntax>().ToList();

        // Exact name first; the regex was case-insensitive, so a differently cased name still works.
        var property = properties.FirstOrDefault(p => p.Identifier.Text.Equals(propertyName, StringComparison.Ordinal))
            ?? properties.FirstOrDefault(p => p.Identifier.Text.Equals(propertyName, StringComparison.OrdinalIgnoreCase));

        if (property == null)
        {
            message = $"Property '{propertyName}' not found in '{className}'.";
            return false;
        }

        // Its own indent, line break and doc comment go with it; a directive or section
        // comment above it stays (see MemberRemoval).
        root = MemberRemoval.Remove(root, property);

        return ClassMethodEditService.TryWrite(file, root,
            $"Property '{property.Identifier.Text}' removed from '{className}'.", out message);
    }
}