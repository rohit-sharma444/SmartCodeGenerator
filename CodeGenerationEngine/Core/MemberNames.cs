using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// The checks a new name for a class or one of its members must pass before it is written.
/// </summary>
/// <remarks>
/// The command patterns capture names as <c>\w+</c>, which takes <c>9x</c> and <c>class</c>
/// alike, and the rename services wrote whatever arrived — a file that no longer parses.
/// </remarks>
internal static class MemberNames
{
    /// <summary>Null when <paramref name="name"/> can be written as an identifier, else why not.</summary>
    public static string? NotAnIdentifier(string? name, string what)
    {
        if (string.IsNullOrWhiteSpace(name))
            return $"A new {what} name is required.";

        if (SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None)
            return $"'{name}' is a C# keyword, so it cannot be a {what} name.";

        if (!SyntaxFacts.IsValidIdentifier(name))
            return $"'{name}' is not a valid {what} name: use letters, digits and _, starting with a letter.";

        return null;
    }

    /// <summary>
    /// Null when no member of <paramref name="classNode"/> other than <paramref name="except"/>
    /// already uses <paramref name="name"/>, and it is not the class's own name; else why not.
    /// </summary>
    /// <remarks>
    /// Any member kind: a method and a property of one name is CS0102, and a member named like its
    /// class is CS0542. Compared ignoring case, so no two members differ only in case. Overloads of the member being renamed are passed in <paramref name="except"/>.
    /// </remarks>
    public static string? Taken(TypeDeclarationSyntax classNode, string name, IEnumerable<MemberDeclarationSyntax> except)
    {
        if (classNode.Identifier.Text.Equals(name, StringComparison.OrdinalIgnoreCase))
            return $"'{name}' is the {(classNode is InterfaceDeclarationSyntax ? "interface" : "class")}'s own name; "
                + "a member cannot share it.";

        var skip = except.ToHashSet();
        foreach (var member in classNode.Members)
        {
            if (skip.Contains(member))
                continue;

            // Any case: Email beside email compiles, but a name typed for either then fits both
            // and every later edit has to refuse it as ambiguous.
            if (NamesOf(member).FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } existing)
                return $"'{classNode.Identifier.Text}' already has a {KindOf(member)} named '{existing}'"
                    + (existing == name ? "." : $"; '{name}' differs from it only in case.");
        }

        return null;
    }

    /// <summary>As above, across every partial declaration of the class.</summary>
    public static string? Taken(IEnumerable<ClassPart> parts, string name, IEnumerable<MemberDeclarationSyntax> except)
    {
        var skip = except.ToList();
        return parts.Select(p => Taken(p.Node, name, skip)).FirstOrDefault(r => r != null);
    }

    private static IEnumerable<string> NamesOf(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => [m.Identifier.Text],
        PropertyDeclarationSyntax p => [p.Identifier.Text],
        EventDeclarationSyntax e => [e.Identifier.Text],
        BaseTypeDeclarationSyntax t => [t.Identifier.Text],
        DelegateDeclarationSyntax d => [d.Identifier.Text],
        BaseFieldDeclarationSyntax f => f.Declaration.Variables.Select(v => v.Identifier.Text),
        _ => [],
    };

    private static string KindOf(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax => "method",
        PropertyDeclarationSyntax => "property",
        EventDeclarationSyntax or EventFieldDeclarationSyntax => "event",
        FieldDeclarationSyntax => "field",
        _ => "member",
    };
}
