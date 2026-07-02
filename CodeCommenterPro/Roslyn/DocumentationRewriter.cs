namespace CodeCommenterPro.Roslyn;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public class DocumentationRewriter : CSharpSyntaxRewriter
{
    private readonly CommentGenerator _generator;
    private readonly Microsoft.CodeAnalysis.SemanticModel? _semanticModel;

    public DocumentationRewriter(
        CommentGenerator generator,
        Microsoft.CodeAnalysis.SemanticModel? semanticModel = null)
    {
        _generator = generator;
        _semanticModel = semanticModel;
    }

    private static bool HasDocumentation(
        SyntaxNode node)
    {
        return node
            .GetLeadingTrivia()
            .Any(t =>
                t.GetStructure()
                is DocumentationCommentTriviaSyntax);
    }

    private static SyntaxTriviaList BuildTrivia(
        string comment,
        SyntaxTriviaList existingTrivia)
    {
        var docs =
            SyntaxFactory.ParseLeadingTrivia(
                comment + Environment.NewLine);

        return docs.AddRange(existingTrivia);
    }

    public override SyntaxNode? VisitClassDeclaration(
        ClassDeclarationSyntax node)
    {
        node =
            (ClassDeclarationSyntax)
            base.VisitClassDeclaration(node)!;

        if (HasDocumentation(node))
            return node;

        string comment =
            _generator.GenerateClassComment(
                node.Identifier.Text);

        return node.WithLeadingTrivia(
            BuildTrivia(
                comment,
                node.GetLeadingTrivia()));
    }

    public override SyntaxNode? VisitStructDeclaration(
        StructDeclarationSyntax node)
    {
        node =
            (StructDeclarationSyntax)
            base.VisitStructDeclaration(node)!;

        if (HasDocumentation(node))
            return node;

        string comment =
$@"/// <summary>
/// Represents {node.Identifier.Text}.
/// </summary>";

        return node.WithLeadingTrivia(
            BuildTrivia(
                comment,
                node.GetLeadingTrivia()));
    }

    public override SyntaxNode? VisitInterfaceDeclaration(
        InterfaceDeclarationSyntax node)
    {
        node =
            (InterfaceDeclarationSyntax)
            base.VisitInterfaceDeclaration(node)!;

        if (HasDocumentation(node))
            return node;

        string comment =
$@"/// <summary>
/// Defines {node.Identifier.Text}.
/// </summary>";

        return node.WithLeadingTrivia(
            BuildTrivia(
                comment,
                node.GetLeadingTrivia()));
    }

    public override SyntaxNode? VisitRecordDeclaration(
        RecordDeclarationSyntax node)
    {
        node =
            (RecordDeclarationSyntax)
            base.VisitRecordDeclaration(node)!;

        if (HasDocumentation(node))
            return node;

        string comment =
$@"/// <summary>
/// Represents {node.Identifier.Text}.
/// </summary>";

        return node.WithLeadingTrivia(
            BuildTrivia(
                comment,
                node.GetLeadingTrivia()));
    }

    public override SyntaxNode? VisitEnumDeclaration(
        EnumDeclarationSyntax node)
    {
        node =
            (EnumDeclarationSyntax)
            base.VisitEnumDeclaration(node)!;

        if (HasDocumentation(node))
            return node;

        string comment =
$@"/// <summary>
/// Represents {node.Identifier.Text}.
/// </summary>";

        return node.WithLeadingTrivia(
            BuildTrivia(
                comment,
                node.GetLeadingTrivia()));
    }

    public override SyntaxNode? VisitEnumMemberDeclaration(
        EnumMemberDeclarationSyntax node)
    {
        node =
            (EnumMemberDeclarationSyntax)
            base.VisitEnumMemberDeclaration(node)!;

        if (HasDocumentation(node))
            return node;

        string comment =
$@"/// <summary>
/// {node.Identifier.Text}.
/// </summary>";

        return node.WithLeadingTrivia(
            BuildTrivia(
                comment,
                node.GetLeadingTrivia()));
    }

    public override SyntaxNode? VisitPropertyDeclaration(
        PropertyDeclarationSyntax node)
    {
        node =
            (PropertyDeclarationSyntax)
            base.VisitPropertyDeclaration(node)!;

        if (HasDocumentation(node))
            return node;

        string comment =
            _generator.GeneratePropertyComment(
                node.Identifier.Text);

        return node.WithLeadingTrivia(
            BuildTrivia(
                comment,
                node.GetLeadingTrivia()));
    }

    public override SyntaxNode? VisitConstructorDeclaration(
        ConstructorDeclarationSyntax node)
    {
        node =
            (ConstructorDeclarationSyntax)
            base.VisitConstructorDeclaration(node)!;

        if (HasDocumentation(node))
            return node;

        string comment =
            _generator.GenerateConstructorComment(
                node, _semanticModel);

        return node.WithLeadingTrivia(
            BuildTrivia(
                comment,
                node.GetLeadingTrivia()));
    }

    public override SyntaxNode? VisitMethodDeclaration(
        MethodDeclarationSyntax node)
    {
        node =
            (MethodDeclarationSyntax)
            base.VisitMethodDeclaration(node)!;

        if (HasDocumentation(node))
            return node;

        string comment =
            _generator.GenerateMethodComment(
                node, _semanticModel);

        return node.WithLeadingTrivia(
            BuildTrivia(
                comment,
                node.GetLeadingTrivia()));
    }
}