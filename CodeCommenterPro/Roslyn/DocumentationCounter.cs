using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeCommenterPro.Roslyn;

public class DocumentationCounter
    : CSharpSyntaxWalker
{
    public int DocumentedClasses { get; private set; }

    public int DocumentedMethods { get; private set; }

    public int DocumentedProperties { get; private set; }

    public int DocumentedConstructors { get; private set; }

    private bool HasDocumentation(
        SyntaxNode node)
    {
        return node
            .GetLeadingTrivia()
            .Any(t =>
                t.GetStructure()
                is DocumentationCommentTriviaSyntax);
    }

    public override void VisitClassDeclaration(
        ClassDeclarationSyntax node)
    {
        if (HasDocumentation(node))
            DocumentedClasses++;

        base.VisitClassDeclaration(node);
    }

    public override void VisitMethodDeclaration(
        MethodDeclarationSyntax node)
    {
        if (HasDocumentation(node))
            DocumentedMethods++;

        base.VisitMethodDeclaration(node);
    }

    public override void VisitPropertyDeclaration(
        PropertyDeclarationSyntax node)
    {
        if (HasDocumentation(node))
            DocumentedProperties++;

        base.VisitPropertyDeclaration(node);
    }

    public override void VisitConstructorDeclaration(
        ConstructorDeclarationSyntax node)
    {
        if (HasDocumentation(node))
            DocumentedConstructors++;

        base.VisitConstructorDeclaration(node);
    }
}