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

        var source = Validation.SourceFileText.Read(filePath, out var hasBom, out var newline);
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();

        // Match by name: a file may hold more than one type, and the member belongs to
        // the one the user named, not to whichever is first or last in the file.
        // An interface too: create interface writes one, and add method / add property on it
        // answered "Class 'IShape' not found". And a record or a struct, which show properties
        // lists as readily as a class.
        var classNode = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(c => c.Identifier.Text.Equals(className, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Identifier.Text.Equals(className, StringComparison.Ordinal) ? 0 : 1)
            .FirstOrDefault();

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

        Validation.SourceFileText.Write(filePath, Insert(source, classNode, member, newline), hasBom);
        return true;
    }

    /// <summary>
    /// <paramref name="source"/> with <paramref name="member"/> as the last member of
    /// <paramref name="type"/>, and nothing else in it changed.
    /// </summary>
    /// <remarks>
    /// Only the new member is formatted, with the file's own indentation and line end. The whole
    /// file used to be put through <c>NormalizeWhitespace</c>, which took out the blank lines
    /// between members, moved <c>#region</c> to column 0, wrote <c>&lt;param name = "x"&gt;</c>,
    /// split one-line statements, turned tabs into spaces and dropped the final newline.
    /// </remarks>
    internal static string Insert(string source, TypeDeclarationSyntax type, MemberDeclarationSyntax member, string newline)
    {
        var unit = IndentUnit(source);
        var typeIndent = IndentOfLineAt(source, type.SpanStart);
        var memberIndent = type.Members.Count > 0
            ? IndentOfLineAt(source, type.Members[0].SpanStart)
            : typeIndent + unit;

        var lines = IndentLoneComments(member.NormalizeWhitespace(unit, newline).ToFullString())
            .Split(newline)
            .Select(l => l.Length == 0 ? l : memberIndent + l);
        var memberText = string.Join(newline, lines);

        // A positional record with no body: `record Money(decimal Amount);` gets one.
        if (type.OpenBraceToken.IsKind(SyntaxKind.None))
        {
            var semicolon = type.SemicolonToken;
            var body = newline + typeIndent + "{" + newline + memberText + newline + typeIndent + "}";
            return source[..semicolon.SpanStart] + body + source[semicolon.Span.End..];
        }

        var close = type.CloseBraceToken.SpanStart;
        var lineStart = source.LastIndexOf('\n', Math.Max(0, close - 1)) + 1;

        // A blank line before it when the members already have them between them, or there is
        // only one; none when they sit line after line.
        var blank = type.Members.Count switch
        {
            0 => false,
            1 => true,
            _ => Enumerable.Range(1, type.Members.Count - 1).Any(i =>
                BlankLineBetween(source, type.Members[i - 1].Span.End, type.Members[i].SpanStart)),
        };

        var lead = blank ? newline : string.Empty;

        // The closing brace on a line of its own: the member goes on the lines above it.
        if (string.IsNullOrWhiteSpace(source[lineStart..close]) && lineStart > type.OpenBraceToken.Span.End)
            return source[..lineStart] + lead + memberText + newline + source[lineStart..];

        // `class A { int X; }` on one line: the brace goes onto a line of its own after the member.
        return source[..close].TrimEnd(' ', '\t') + newline + memberText + newline + typeIndent + source[close..];
    }

    private static bool BlankLineBetween(string source, int from, int to)
    {
        var between = source[from..to].Replace("\r", string.Empty, StringComparison.Ordinal);
        var first = between.IndexOf('\n');
        return first >= 0 && between.IndexOf('\n', first + 1) >= 0
            && string.IsNullOrWhiteSpace(between[first..between.IndexOf('\n', first + 1)]);
    }

    /// <summary>The whitespace a line starts with, for the line that holds <paramref name="position"/>.</summary>
    internal static string IndentOfLineAt(string source, int position)
    {
        var start = source.LastIndexOf('\n', Math.Max(0, position - 1)) + 1;
        if (position == 0)
            start = 0;

        var end = start;
        while (end < source.Length && source[end] is ' ' or '\t')
            end++;

        return source[start..end];
    }

    /// <summary>One level of indentation as the file writes it: a tab when its lines are indented
    /// with tabs, else four spaces.</summary>
    internal static string IndentUnit(string source)
    {
        var tabs = 0;
        var spaces = 0;

        foreach (var line in source.Split('\n'))
        {
            if (line.StartsWith('\t'))
                tabs++;
            else if (line.StartsWith("  ", StringComparison.Ordinal))
                spaces++;
        }

        return tabs > spaces ? "\t" : "    ";
    }

    // NormalizeWhitespace indents a comment by the token it is attached to, and in an empty body
    // that is the closing brace: the stub's "// TODO: implement" sat at the brace's indent. A comment
    // line directly above a closing brace at the same indent is moved one level in.
    private static readonly System.Text.RegularExpressions.Regex LoneComment = new(
        @"(?m)^(?<indent>[ \t]*)(?<comment>//[^\r\n]*)(?<nl>\r?\n)\k<indent>\}");

    internal static string IndentLoneComments(string code) =>
        LoneComment.Replace(code, m => $"{m.Groups["indent"].Value}    {m.Groups["comment"].Value}{m.Groups["nl"].Value}{m.Groups["indent"].Value}}}");

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
