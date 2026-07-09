using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Service responsible for generating C# class code from a ClassDefinition model using Roslyn syntax APIs.
/// </summary>
public static class RoslynClassGenerator
{
    /// <summary>
    /// Generates C# source for <paramref name="model"/> in <paramref name="namespaceName"/>.
    /// The default keeps the historical namespace used by the engine's `add class` commands;
    /// app scaffolding passes a folder-matching namespace instead.
    /// </summary>
    public static string Generate(ClassDefinition model, string namespaceName = "GeneratedCode")
    {
        var members = new List<MemberDeclarationSyntax>();

        // Properties
        members.AddRange(model.Properties.Select(CreateProperty));

        // Constructor
        members.Add(CreateConstructor(model));

        // Methods
        members.AddRange(model.Methods.Select(m => CreateMethod(model, m)));

        var classDeclaration = ClassDeclaration(model.Name)
                .AddModifiers(Token(SyntaxKind.PublicKeyword))
                .AddMembers([.. members]);

        var namespaceDeclaration = NamespaceDeclaration(
                ParseName(namespaceName)).AddMembers(classDeclaration);

        var compilationUnit = CompilationUnit()
                .AddMembers(namespaceDeclaration).NormalizeWhitespace();

        return compilationUnit.ToFullString();
    }

    public static bool ApplyInheritance(string filePath, string baseClass)
    {
        var code = File.ReadAllText(filePath);
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        var classNode = root
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault();

        if (classNode == null)
            return false;

        var updatedClass =
            classNode.WithBaseList(
                SyntaxFactory.BaseList(
                    SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                        SyntaxFactory.SimpleBaseType(
                            SyntaxFactory.ParseTypeName(baseClass)))));

        root = root.ReplaceNode(classNode, updatedClass);

        File.WriteAllText(
            filePath,
            root.NormalizeWhitespace().ToFullString());

        return true;
    }

    private static PropertyDeclarationSyntax CreateProperty(PropertyDefinition property)
    {
        var propertyDeclaration =
            PropertyDeclaration(
                    ParseTypeName(property.Type),
                    property.Name)
                .AddModifiers(Token(SyntaxKind.PublicKeyword))
                .AddAccessorListAccessors(
                    AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
                    AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));

        var attributes = new List<AttributeSyntax>();

        foreach (var rule in property.Rules)
        {
            switch (rule.Name.ToLowerInvariant())
            {
                case "required":
                    attributes.Add(Attribute(IdentifierName("Required")));
                    break;

                case "maxlength":
                    if (rule.MaxLength.HasValue)
                    {
                        attributes.Add(
                            Attribute(IdentifierName("StringLength"))
                                .AddArgumentListArguments(
                                    AttributeArgument(
                                        LiteralExpression(
                                            SyntaxKind.NumericLiteralExpression,
                                            Literal(rule.MaxLength.Value)))));
                    }
                    break;

                case "minlength":
                    if (rule.MinLength.HasValue)
                    {
                        attributes.Add(
                            Attribute(IdentifierName("MinLength"))
                                .AddArgumentListArguments(
                                    AttributeArgument(
                                        LiteralExpression(
                                            SyntaxKind.NumericLiteralExpression,
                                            Literal(rule.MinLength.Value)))));
                    }
                    break;
            }
        }

        if (attributes.Any())
        {
            propertyDeclaration = propertyDeclaration.AddAttributeLists(
                    AttributeList(SeparatedList(attributes)));
        }

        return propertyDeclaration;
    }

    private static ConstructorDeclarationSyntax CreateConstructor(ClassDefinition model)
    {
        return ConstructorDeclaration(model.Name)
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .WithBody(Block());
    }

    private static MethodDeclarationSyntax CreateMethod(ClassDefinition model, MethodDefinition method)
    {
        // Generate method body lines using MethodBodyGenerator
        var lines = MethodBodyGenerator.Generate(model.Name, method, model.Properties.ToList());

        var statements = new List<StatementSyntax>();

        foreach (var line in lines)
        {
            var trimmed = line?.Trim() ?? string.Empty;

            if (trimmed.StartsWith("//"))
            {
                // Represent comment as leading trivia on an empty statement
                statements.Add(EmptyStatement().WithLeadingTrivia(TriviaList(Comment(line ?? string.Empty))));
            }
            else if (!string.IsNullOrWhiteSpace(trimmed))
            {
                statements.Add(ParseStatement(line ?? string.Empty));
            }
        }

        return MethodDeclaration(ParseTypeName(method.ReturnType),
                method.Name).AddModifiers(
                Token(SyntaxKind.PublicKeyword)).WithBody(Block(statements));
    }

    private static ConstructorDeclarationSyntax CreateParameterizedConstructor(ClassDefinition model)
    {
        var ctor = ConstructorDeclaration(model.Name)
                .AddModifiers(Token(SyntaxKind.PublicKeyword));

        foreach (var property in model.Properties)
        {
            ctor = ctor.AddParameterListParameters(
                Parameter(Identifier(property.Name.ToLower()))
                .WithType(ParseTypeName(property.Type)));
        }

        var assignments = model.Properties.Select(p =>
                ExpressionStatement(AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                IdentifierName(p.Name),
                IdentifierName(p.Name.ToLower()))));

        return ctor.WithBody(Block(assignments));
    }
}