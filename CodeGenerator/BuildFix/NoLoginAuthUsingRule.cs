using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerator.BuildFix;

/// <summary>
/// CS0234 for a nologin app: a file names <c>using &lt;Root&gt;.Auth;</c> but the app has no
/// <c>Auth</c> folder. Current templates emit that using only when login is on; this repairs older
/// output. Removes exactly the one using directive the compiler pointed at, and only when that
/// directive is alone on its line, so no neighbouring text, indent or line ending is touched.
/// </summary>
internal sealed class NoLoginAuthUsingRule : IBuildFixRule
{
    public string Name => "remove the using for the missing Auth folder";

    public bool Fix(BuildDiagnostic d, FixSession session)
    {
        if (d.Code != "CS0234" || d.Line < 1 || d.File.Length == 0 || !File.Exists(d.File)
            || !d.Message.Contains("'Auth'", StringComparison.Ordinal)
            || Directory.Exists(Path.Combine(session.Root, "Auth")))
            return false;

        var source = session.Read(d.File);
        var tree = CSharpSyntaxTree.ParseText(source);
        var text = tree.GetText();

        if (d.Line > text.Lines.Count)
            return false;

        var line = text.Lines[d.Line - 1];

        var directive = tree.GetRoot().DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .FirstOrDefault(u => text.Lines.GetLineFromPosition(u.SpanStart).LineNumber == d.Line - 1);

        if (directive is null
            || directive.Alias is not null
            || directive.StaticKeyword.RawKind != 0
            || directive.Name is not QualifiedNameSyntax { Right.Identifier.ValueText: "Auth" }
            || line.ToString().Trim() != directive.ToString().Trim())
            return false;

        // The directive must also END on this line (a multi-line using is not our shape).
        if (text.Lines.GetLineFromPosition(directive.Span.End).LineNumber != d.Line - 1)
            return false;

        var updated = source.Remove(line.Start, line.SpanIncludingLineBreak.Length);
        session.Write(d.File, updated, Name);

        return true;
    }
}
