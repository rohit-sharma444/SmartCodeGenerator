using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Inserts a member into an existing class file.
/// </summary>
/// <remarks>
/// <c>add property</c> and <c>add method</c> both used to splice the new member in at
/// <c>code.LastIndexOf('}')</c>. In a block-scoped namespace that brace closes the
/// *namespace*, not the class, so the member was written after the class body and the
/// file stopped compiling. Locating the class through the syntax tree — the same
/// approach <see cref="RoslynClassGenerator.ApplyInheritance"/> already takes — puts the
/// member in the right place regardless of namespace style, sibling types, or layout.
/// </remarks>
internal static class ClassMemberEditor
{
    public static bool TryAddMember(string filePath, string className, string memberCode, out string error)
    {
        error = string.Empty;

        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(filePath)).GetRoot();

        // Match by name: a file may hold more than one type, and the member belongs to
        // the one the user named, not to whichever is first or last in the file.
        // An interface too: create interface writes one, and add method / add property on it
        // answered "Class 'IShape' not found".
        var classNode = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(c => c is ClassDeclarationSyntax or InterfaceDeclarationSyntax
                && c.Identifier.Text.Equals(className, StringComparison.OrdinalIgnoreCase));

        if (classNode == null)
        {
            error = $"Class '{className}' not found in {Path.GetFileName(filePath)}.";
            return false;
        }

        var member = SyntaxFactory.ParseMemberDeclaration(memberCode);
        if (member == null || member.ContainsDiagnostics)
        {
            error = "Could not parse the generated member.";
            return false;
        }

        if (classNode is InterfaceDeclarationSyntax iface)
            member = InInterfaceForm(member, iface);

        root = root.ReplaceNode(classNode, classNode.AddMembers(member));
        File.WriteAllText(filePath, root.NormalizeWhitespace().ToFullString());
        return true;
    }

    /// <summary>
    /// <paramref name="member"/> as an interface declares it: a method is a signature with no body,
    /// a property an auto-property with no initial value and no attributes. The access modifier
    /// goes, as interface members are public already, unless the interface already writes one.
    /// </summary>
    /// <remarks>
    /// The services build a class's member - <c>public double Area() { return default; }</c> - and
    /// that body in an interface is a default implementation, which is not what was asked for.
    /// Validation attributes on an interface property validate nothing, since a validator reads the
    /// class.
    /// </remarks>
    internal static MemberDeclarationSyntax InInterfaceForm(MemberDeclarationSyntax member, InterfaceDeclarationSyntax iface)
    {
        var writesPublic = iface.Members.Any(m => m.Modifiers.Any(SyntaxKind.PublicKeyword));
        var modifiers = writesPublic
            ? SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            : default;

        return member switch
        {
            MethodDeclarationSyntax method => method
                .WithModifiers(modifiers)
                .WithBody(null)
                .WithExpressionBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),

            PropertyDeclarationSyntax property => property
                .WithModifiers(modifiers)
                .WithAttributeLists(default)
                .WithInitializer(null)
                .WithExpressionBody(null)
                .WithSemicolonToken(default)
                .WithAccessorList(property.AccessorList ?? SyntaxFactory.AccessorList(SyntaxFactory.List(
                [
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                ]))),

            _ => member,
        };
    }
}
