using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Adds a value to, or removes one from, an enum already in the workspace. The file is written back
/// with <c>ToFullString</c> so the user's layout survives. The value's name is checked by the caller
/// (the bridge's <c>EnumValueParser</c>); this service checks the enum.
/// </summary>
public static class EnumValueService
{
    /// <summary>
    /// Adds <paramref name="value"/> after the last value. An enum that numbers its values gives it
    /// the next number (the highest plus one); a <c>[Flags]</c> enum is refused, as the next bit is
    /// the user's choice.
    /// </summary>
    public static bool AddValue(string workspaceRoot, string enumName, string value, out string message)
    {
        if (!TryLoadEnum(workspaceRoot, enumName, out var file, out var root, out var node, out message))
            return false;

        enumName = node.Identifier.Text;

        if (IsFlags(node))
        {
            message = $"'{enumName}' is a [Flags] enum; give the new value its bit by hand.";
            return false;
        }

        if (node.Members.Any(m => m.Identifier.Text.Equals(value, StringComparison.OrdinalIgnoreCase)))
        {
            message = $"Value '{value}' already exists in '{enumName}'.";
            return false;
        }

        var member = SyntaxFactory.EnumMemberDeclaration(SyntaxFactory.Identifier(value));
        if (node.Members.Any(m => m.EqualsValue != null))
        {
            if (NextNumber(node) is not { } next)
            {
                message = $"'{enumName}' gives a value a number that is not a plain integer, so the number of '{value}' "
                    + "cannot be worked out. Add it by hand.";
                return false;
            }

            var number = next is >= int.MinValue and <= int.MaxValue
                ? SyntaxFactory.Literal((int)next)
                : SyntaxFactory.Literal(next);
            member = SyntaxFactory.EnumMemberDeclaration(SyntaxFactory.Identifier(value).WithTrailingTrivia(SyntaxFactory.Space))
                .WithEqualsValue(SyntaxFactory.EqualsValueClause(
                    SyntaxFactory.Token(SyntaxKind.EqualsToken).WithTrailingTrivia(SyntaxFactory.Space),
                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, number)));
        }

        var newRoot = root.ReplaceNode(node, node.WithMembers(Appended(node, member)));
        return ClassMethodEditService.TryWrite(file, newRoot, $"Value '{value}' added to '{enumName}'.", out message);
    }

    /// <summary>Removes <paramref name="value"/>. Code that still uses it is the caller's to warn of.</summary>
    public static bool RemoveValue(string workspaceRoot, string enumName, string value, out string message) =>
        RemoveValue(workspaceRoot, enumName, value, out message, out _);

    /// <summary>As above, and the removed value's name as the enum declares it (<c>pending</c> found <c>Pending</c>).</summary>
    public static bool RemoveValue(
        string workspaceRoot, string enumName, string value, out string message, out string removedName)
    {
        removedName = value;
        if (!TryLoadEnum(workspaceRoot, enumName, out var file, out var root, out var node, out message))
            return false;

        enumName = node.Identifier.Text;

        var matches = ClassMethodEditService.Named(node.Members, m => m.Identifier.Text, value);
        if (matches.Count == 0)
        {
            message = $"'{value}' is not a value of '{enumName}'.";
            return false;
        }

        var removed = matches[0];
        removedName = removed.Identifier.Text;
        var newRoot = root.ReplaceNode(node, node.WithMembers(Without(node.Members, removed)));
        return ClassMethodEditService.TryWrite(
            file, newRoot, $"Value '{removed.Identifier.Text}' removed from '{enumName}'.", out message);
    }

    /// <summary>
    /// The refusal for a member edit (property, method, validation) on the enum called
    /// <paramref name="name"/>, or null when there is no such enum. The one source of the text.
    /// </summary>
    public static string? MemberEditRefusalFor(string workspaceRoot, string name) =>
        FindEnum(workspaceRoot, name, out _, out _, out var node, out _)
            ? $"'{node.Identifier.Text}' is an enum; use add value <name> to {node.Identifier.Text}."
            : null;

    /// <summary>
    /// The enum called <paramref name="name"/>: in its own file first, else in any file of the
    /// search root; the exact spelling before another case. False with the reason when there is
    /// none: a class, record, struct or interface of the name is "not an enum".
    /// </summary>
    private static bool TryLoadEnum(
        string workspaceRoot, string name,
        out string file, out SyntaxNode root, out EnumDeclarationSyntax node, out string message)
    {
        file = string.Empty;
        root = null!;
        node = null!;

        if (FindEnum(workspaceRoot, name, out var foundFile, out var foundRoot, out var foundNode, out message))
        {
            file = foundFile;
            root = foundRoot;
            node = foundNode;
            return true;
        }

        if (message.Length > 0)
            return false;

        message = ClassMethodEditService.TryLoad(workspaceRoot, name, out _, out _, out var type, out _)
            ? $"'{type.Identifier.Text}' is not an enum."
            : $"Enum '{name}' not found.";
        return false;
    }

    /// <summary>
    /// Looks for an enum called <paramref name="name"/>. False with an empty message when there is
    /// none; false with a message when a file could not be read or the name is ambiguous.
    /// </summary>
    internal static bool FindEnum(
        string workspaceRoot, string name,
        out string file, out SyntaxNode root, out EnumDeclarationSyntax node, out string message)
    {
        file = string.Empty;
        root = null!;
        node = null!;
        message = string.Empty;

        try
        {
            if (!ClassFiles.TryFind(workspaceRoot, name, out var named, out message))
                return false;

            var candidates = new List<string>();
            if (named != null)
                candidates.Add(named);
            candidates.AddRange(ClassFiles.Under(ClassFiles.SearchRoot(workspaceRoot))
                .Where(f => !string.Equals(f, named, StringComparison.OrdinalIgnoreCase)));

            foreach (var candidate in candidates)
            {
                string text;
                try
                {
                    text = Validation.SourceFileText.Load(candidate);
                }
                catch (Validation.NotUtf8Exception) when (candidate != named)
                {
                    continue;
                }

                if (!text.Contains(name, StringComparison.OrdinalIgnoreCase))
                    continue;

                var tree = CSharpSyntaxTree.ParseText(text).GetRoot();
                var found = tree.DescendantNodes().OfType<EnumDeclarationSyntax>()
                    .Where(e => e.Identifier.Text.Equals(name, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.Identifier.Text.Equals(name, StringComparison.Ordinal) ? 0 : 1)
                    .FirstOrDefault();

                if (found == null)
                    continue;

                file = candidate;
                root = tree;
                node = found;
                return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = ex.Message;
            return false;
        }
    }

    private static bool IsFlags(EnumDeclarationSyntax node) =>
        node.AttributeLists.SelectMany(l => l.Attributes).Any(a =>
            a.Name.ToString() is "Flags" or "System.Flags" or "FlagsAttribute" or "System.FlagsAttribute");

    // The highest number the values have, plus one, counting from the last explicit number: a value
    // with none is the one before it plus one. Null when a number is not a plain integer.
    private static long? NextNumber(EnumDeclarationSyntax node)
    {
        long current = -1;
        long max = long.MinValue;

        foreach (var member in node.Members)
        {
            if (member.EqualsValue != null)
            {
                if (!TryInteger(member.EqualsValue.Value, out current))
                    return null;
            }
            else
            {
                current++;
            }

            max = Math.Max(max, current);
        }

        return max + 1;
    }

    private static bool TryInteger(ExpressionSyntax expression, out long number)
    {
        number = 0;
        var negative = expression is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.UnaryMinusExpression };
        if (negative)
            expression = ((PrefixUnaryExpressionSyntax)expression).Operand;

        if (expression is not LiteralExpressionSyntax { Token.Value: var value } || value is not (int or long or uint or ulong))
            return false;

        number = negative ? -Convert.ToInt64(value) : Convert.ToInt64(value);
        return true;
    }

    // The new member after the last one, in its layout: the same indent and line end, and the comma
    // the list needs. A list that already ends in a comma keeps one.
    private static SeparatedSyntaxList<EnumMemberDeclarationSyntax> Appended(
        EnumDeclarationSyntax node, EnumMemberDeclarationSyntax member)
    {
        var members = node.Members;

        if (members.Count == 0)
        {
            // `{` then a line end: the member on its own line, indented one level, as create enum writes it.
            var lineEnd = node.OpenBraceToken.TrailingTrivia.FirstOrDefault(t => t.IsKind(SyntaxKind.EndOfLineTrivia));
            return lineEnd.IsKind(SyntaxKind.EndOfLineTrivia)
                ? SyntaxFactory.SingletonSeparatedList(member
                    .WithLeadingTrivia(SyntaxFactory.Whitespace("    "))
                    .WithTrailingTrivia(lineEnd))
                : SyntaxFactory.SingletonSeparatedList(member.WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(SyntaxFactory.Space));
        }

        var last = members[^1];
        var leading = last.GetLeadingTrivia();
        var trailing = last.GetTrailingTrivia();
        var items = members.GetWithSeparators().ToList();

        if (members.SeparatorCount == members.Count)
        {
            // A trailing comma: its own trivia holds the line end.
            var comma = (SyntaxToken)items[^1];
            items.Add(member.WithLeadingTrivia(leading).WithTrailingTrivia(comma.TrailingTrivia));
            items.Add(SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(comma.TrailingTrivia));
        }
        else
        {
            items[^1] = last.WithTrailingTrivia(SyntaxTriviaList.Empty);
            items.Add(SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(trailing));
            items.Add(member.WithLeadingTrivia(leading).WithTrailingTrivia(trailing));
        }

        return SyntaxFactory.SeparatedList<EnumMemberDeclarationSyntax>(items);
    }

    // The values but one, and the comma that went with it. The last value takes the one before it as
    // the last, which keeps the line end the removed one had.
    private static SeparatedSyntaxList<EnumMemberDeclarationSyntax> Without(
        SeparatedSyntaxList<EnumMemberDeclarationSyntax> members, EnumMemberDeclarationSyntax removed)
    {
        var at = members.IndexOf(removed);
        var items = members.GetWithSeparators().ToList();
        var itemAt = at * 2;

        if (at < members.Count - 1)
        {
            items.RemoveRange(itemAt, 2);
        }
        else if (members.SeparatorCount == members.Count)
        {
            // Last, with its own comma: both go, and the comma before keeps the line end.
            items.RemoveRange(itemAt, 2);
        }
        else if (at == 0)
        {
            items.RemoveAt(itemAt);
        }
        else
        {
            // Last, with no comma: the comma before it goes, and the value before takes its line end.
            var before = (EnumMemberDeclarationSyntax)items[itemAt - 2].AsNode()!;
            items.RemoveRange(itemAt - 1, 2);
            items[itemAt - 2] = before.WithTrailingTrivia(removed.GetTrailingTrivia());
        }

        return SyntaxFactory.SeparatedList<EnumMemberDeclarationSyntax>(items);
    }
}
