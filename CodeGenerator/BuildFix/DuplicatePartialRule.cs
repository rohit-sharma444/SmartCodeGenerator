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

        var typeName = m.Groups["n"].Value;
        // Handle generic types: "Order<T>" -> "Order"
        var nameOnly = typeName.Contains('<') ? typeName[..typeName.IndexOf('<')] : typeName;

        var source = session.Read(d.File);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        TypeDeclarationSyntax? type = null;

        // If a position is given, use it to locate the type
        if (d.Line >= 1)
        {
            var text = tree.GetText();
            // Convert 1-based line/column to 0-based
            var lineIndex = d.Line - 1;
            if (lineIndex < text.Lines.Count)
            {
                var line = text.Lines[lineIndex];

                // Reject if column is invalid (0 or beyond end of line)
                if (d.Column < 1 || d.Column > line.Span.Length)
                    return false;

                var charPosition = line.Start + d.Column - 1;

                // Find types whose identifier span contains this position and match the name
                type = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault(t => t.Identifier.ValueText == nameOnly
                                         && t.Identifier.Span.Contains(charPosition)
                                         && !t.Modifiers.Any(x => x.IsKind(SyntaxKind.PartialKeyword)));

                // If positioned type is already partial or doesn't exist at that position, return false
                if (type is null)
                    return false;
            }
            else
            {
                return false;
            }
        }
        else
        {
            // Fall back to name-only matching when no position given
            type = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
                .FirstOrDefault(t => t.Identifier.ValueText == nameOnly
                                     && !t.Modifiers.Any(x => x.IsKind(SyntaxKind.PartialKeyword)));

            if (type is null)
                return false;
        }

        session.Write(d.File, source.Insert(type.Keyword.SpanStart, "partial "), Name);

        return true;
    }
}
