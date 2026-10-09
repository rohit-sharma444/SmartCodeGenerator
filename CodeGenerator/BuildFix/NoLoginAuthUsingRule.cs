using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerator.BuildFix;

/// <summary>
/// CS0234 for a nologin app: a file names <c>using &lt;Root&gt;.Auth;</c> but the app has no
/// <c>Auth</c> folder. Current templates emit that using only when login is on; this repairs older
/// output. Removes the using directive the compiler pointed at and every other Auth using of the same root, and only when each
/// is alone on its line, so no neighbouring text, indent or line ending is touched.
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

        // Every other Auth using of the same root goes in the same write: the line numbers of
        // the compiler's list go stale after one removal, so a file with four of them would need
        // four rounds. Only a directive of exactly the safe shape is removed; the rest stay.
        var root = ((QualifiedNameSyntax)directive.Name).Left.ToString();
        var updated = source;

        var spans = tree.GetRoot().DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Where(u => u == directive || IsRemovable(u, root, text))
            .Select(u => text.Lines.GetLineFromPosition(u.SpanStart))
            .OrderByDescending(l => l.Start)
            .ToList();

        foreach (var l in spans)
            updated = updated.Remove(l.Start, l.SpanIncludingLineBreak.Length);

        session.Write(d.File, updated, Name);

        return true;
    }

    private static bool IsRemovable(UsingDirectiveSyntax u, string root, Microsoft.CodeAnalysis.Text.SourceText text)
    {
        if (u.Alias is not null
            || u.StaticKeyword.RawKind != 0
            || u.Name is not QualifiedNameSyntax { Right.Identifier.ValueText: "Auth" } name
            || name.Left.ToString() != root)
            return false;

        var first = text.Lines.GetLineFromPosition(u.SpanStart);

        return text.Lines.GetLineFromPosition(u.Span.End).LineNumber == first.LineNumber
            && first.ToString().Trim() == u.ToString().Trim();
    }
}
