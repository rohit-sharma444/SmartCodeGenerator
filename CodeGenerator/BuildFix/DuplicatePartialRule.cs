using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerator.BuildFix;

/// <summary>
/// CS0260: a type is declared without <c>partial</c> beside a partial half. <c>create class</c>
/// writes <c>public class X</c>; the validator half is <c>public partial class X</c>. Current
/// output marks the declaring half partial already (<c>ClassValidationWriter.EnsurePartial</c>),
/// so this repairs folders made earlier.
/// </summary>
internal sealed class DuplicatePartialRule : IBuildFixRule
{
    private static readonly Regex TypeName = new("type '(?<n>[^']+)'", RegexOptions.Compiled);

    public string Name => "make the class partial";

    public bool Fix(BuildDiagnostic d, FixSession session)
    {
        if (d.Code != "CS0260" || d.File.Length == 0 || !File.Exists(d.File))
            return false;

        if (TypeName.Match(d.Message) is not { Success: true } m)
            return false;

        var source = session.Read(d.File);

        var type = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot()
            .DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.ValueText == m.Groups["n"].Value
                                 && !t.Modifiers.Any(x => x.IsKind(SyntaxKind.PartialKeyword)));

        if (type is null)
            return false;

        session.Write(d.File, source.Insert(type.Keyword.SpanStart, "partial "), Name);

        return true;
    }
}
