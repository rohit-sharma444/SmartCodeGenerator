using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core.Relationships;

/// <summary>
/// Adds a property with attributes, and the usings those attributes need, to a file that already
/// exists.
/// </summary>
/// <remarks>
/// <see cref="ClassPropertyService.AddProperty"/> writes
/// <c>public T Name { get; set; }</c> and nothing else — no attributes, and no usings — so a
/// navigation property written through it would not compile once <c>[JsonIgnore]</c> was on it.
/// <c>AddProperty</c> is deliberately left alone: it is on the <c>add property</c> command's path
/// and nothing about that command is changing.
/// </remarks>
public static class AttributedPropertyWriter
{
    public static bool TryAdd(
        string filePath,
        string className,
        PropertyDefinition property,
        IEnumerable<string> usings,
        out string error)
    {
        var attributes = property.Attributes.Count == 0
            ? string.Empty
            : string.Concat(property.Attributes.Select(a => $"[{a}]{Environment.NewLine}"));

        var member = $"{attributes}public {property.Type} {property.Name} {{ get; set; }}";

        if (!ClassMemberEditor.TryAddMember(filePath, className, member, out error))
            return false;

        EnsureUsings(filePath, usings);
        return true;
    }

    /// <summary>
    /// Adds any missing using directives to the top of the file.
    /// </summary>
    /// <remarks>
    /// Written on the compilation unit rather than inside the namespace, so it works the same for a
    /// file-scoped namespace and a block-scoped one. A directive already present — at either
    /// level — is left alone rather than duplicated, which would be CS0105 under
    /// <c>-warnaserror</c>.
    /// </remarks>
    private static void EnsureUsings(string filePath, IEnumerable<string> usings)
    {
        var wanted = usings
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (wanted.Length == 0)
            return;

        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(filePath));

        if (tree.GetRoot() is not CompilationUnitSyntax unit)
            return;

        var present = unit.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Select(u => u.Name?.ToString())
            .Where(n => n is not null)
            .ToHashSet(StringComparer.Ordinal);

        var missing = wanted
            .Where(u => !present.Contains(u))
            .Select(u => SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(u)))
            .ToArray();

        if (missing.Length == 0)
            return;

        var updated = unit.AddUsings(missing);

        File.WriteAllText(filePath, updated.NormalizeWhitespace().ToFullString());
    }
}
