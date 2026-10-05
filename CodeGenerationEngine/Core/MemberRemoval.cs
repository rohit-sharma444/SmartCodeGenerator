using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Removes one member from a class without taking what sits above it but is not its own.
/// </summary>
/// <remarks>
/// <para>
/// <c>RemoveNode(member, KeepNoTrivia)</c> takes the member's whole leading trivia, which is right
/// for its indent and its doc comment, and wrong for anything else that happens to be parsed into
/// it: an <c>#endregion</c> closing the members above — removed, the file no longer compiles
/// (CS1038) — or a section comment the user wrote over a group of members.
/// </para>
/// <para>
/// The leading trivia is read as lines. Walking up from the member, the comment lines touching it
/// (doc or ordinary, no blank line between) are its own and go with it. The first blank line or
/// directive ends that; everything above it is kept, a directive line included, and moved onto
/// the token that follows the member so it stays where it was in the file.
/// </para>
/// </remarks>
internal static class MemberRemoval
{
    public static SyntaxNode Remove(SyntaxNode root, MemberDeclarationSyntax member)
    {
        var kept = KeptTrivia(member.GetLeadingTrivia());

        if (kept.Count == 0)
            return root.RemoveNode(member, SyntaxRemoveOptions.KeepNoTrivia)!;

        var next = member.GetLastToken().GetNextToken();
        if (next.IsKind(SyntaxKind.None))
            return root.RemoveNode(member, SyntaxRemoveOptions.KeepNoTrivia)!;

        var annotation = new SyntaxAnnotation();
        root = root.TrackNodes(member);
        var tracked = root.GetCurrentNode(member)!;
        next = tracked.GetLastToken().GetNextToken();
        root = root.ReplaceToken(next, next.WithAdditionalAnnotations(annotation));
        root = root.RemoveNode(root.GetCurrentNode(member)!, SyntaxRemoveOptions.KeepNoTrivia)!;

        var moved = root.GetAnnotatedTokens(annotation).Single();
        return root.ReplaceToken(moved, moved.WithLeadingTrivia(kept.AddRange(moved.LeadingTrivia)));
    }

    private static SyntaxTriviaList KeptTrivia(SyntaxTriviaList leading)
    {
        // Each complete line ends in an end-of-line, or is a directive or doc comment, whose
        // structure carries its own line break. What is left after the last one is the member's indent.
        var lines = new List<List<SyntaxTrivia>>();
        var current = new List<SyntaxTrivia>();

        foreach (var trivia in leading)
        {
            current.Add(trivia);
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia) || trivia.IsDirective
                || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                || trivia.IsKind(SyntaxKind.DisabledTextTrivia))
            {
                lines.Add(current);
                current = [];
            }
        }

        var keepUpTo = -1;  // index of the last line kept
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (IsBlank(lines[i]))
            {
                keepUpTo = i - 1;
                break;
            }

            if (lines[i].Any(t => t.IsDirective || t.IsKind(SyntaxKind.DisabledTextTrivia)))
            {
                keepUpTo = i;
                break;
            }
        }

        var kept = lines.Take(keepUpTo + 1).SelectMany(l => l).ToList();

        // Only blank lines above the member: those were always the neighbours' to lose, and
        // removing them with it is the layout this has always written.
        if (!kept.Any(t => t.IsDirective || IsComment(t) || t.IsKind(SyntaxKind.DisabledTextTrivia)))
            return [];

        return SyntaxFactory.TriviaList(kept);
    }

    private static bool IsBlank(List<SyntaxTrivia> line) =>
        line.All(t => t.IsKind(SyntaxKind.WhitespaceTrivia) || t.IsKind(SyntaxKind.EndOfLineTrivia));

    private static bool IsComment(SyntaxTrivia t) =>
        t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia)
        || t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
        || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);
}
