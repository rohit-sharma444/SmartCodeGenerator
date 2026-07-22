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
        var classNode = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text.Equals(className, StringComparison.OrdinalIgnoreCase));

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

        root = root.ReplaceNode(classNode, classNode.AddMembers(member));
        File.WriteAllText(filePath, root.NormalizeWhitespace().ToFullString());
        return true;
    }
}
