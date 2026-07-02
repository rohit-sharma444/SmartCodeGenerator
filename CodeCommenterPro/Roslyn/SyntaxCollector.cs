using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeCommenterPro.Roslyn;

public class SyntaxCollector : CSharpSyntaxWalker
{
    public int ClassCount { get; private set; }

    public int MethodCount { get; private set; }

    public int PropertyCount { get; private set; }

    public int ConstructorCount { get; private set; }

    public int InterfaceCount { get; private set; }

    public int RecordCount { get; private set; }

    public int EnumCount { get; private set; }

    public override void VisitClassDeclaration(
        ClassDeclarationSyntax node)
    {
        ClassCount++;
        base.VisitClassDeclaration(node);
    }

    public override void VisitMethodDeclaration(
        MethodDeclarationSyntax node)
    {
        MethodCount++;
        base.VisitMethodDeclaration(node);
    }

    public override void VisitPropertyDeclaration(
        PropertyDeclarationSyntax node)
    {
        PropertyCount++;
        base.VisitPropertyDeclaration(node);
    }

    public override void VisitConstructorDeclaration(
        ConstructorDeclarationSyntax node)
    {
        ConstructorCount++;
        base.VisitConstructorDeclaration(node);
    }

    public override void VisitInterfaceDeclaration(
        InterfaceDeclarationSyntax node)
    {
        InterfaceCount++;
        base.VisitInterfaceDeclaration(node);
    }

    public override void VisitRecordDeclaration(
        RecordDeclarationSyntax node)
    {
        RecordCount++;
        base.VisitRecordDeclaration(node);
    }

    public override void VisitEnumDeclaration(
        EnumDeclarationSyntax node)
    {
        EnumCount++;
        base.VisitEnumDeclaration(node);
    }
}