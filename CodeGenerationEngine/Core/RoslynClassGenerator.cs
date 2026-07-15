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
                .AddUsings([.. model.Usings.Select(u => UsingDirective(ParseName(u)))])
                .AddMembers(namespaceDeclaration)
                .NormalizeWhitespace();

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

    /// <summary>
    /// The types that are never null and so need no initializer. Anything not here, and not already
    /// nullable, is a reference type the compiler will warn about (CS8618) unless it is given one.
    /// </summary>
    private static readonly HashSet<string> ValueTypes = new(StringComparer.Ordinal)
    {
        "bool", "byte", "sbyte", "char", "short", "ushort", "int", "uint", "long", "ulong",
        "float", "double", "decimal", "DateTime", "DateTimeOffset", "TimeSpan", "Guid",
    };

    /// <summary>
    /// What a non-nullable reference property must be initialized to, or null when the property
    /// needs no initializer at all.
    /// </summary>
    /// <remarks>
    /// Without this the generated app compiled with a CS8618 warning for EVERY string it declared —
    /// 106 of them on the point-of-sale model. A scaffold that greets its user with a screenful of
    /// warnings is a scaffold they distrust, and the fix is the one the compiler is asking for.
    /// <para>
    /// A string gets an empty string, which is what an unset text field means. An entity reference
    /// is a NAVIGATION property, resolved later from the store by RelationshipResolver and legitimately
    /// absent until then; there is nothing to construct it as, so it takes <c>null!</c> — the same
    /// idiom EF Core's own scaffolder emits, and for the same reason.
    /// </para>
    /// </remarks>
    private static ExpressionSyntax? InitializerFor(string type)
    {
        if (type.EndsWith('?') || ValueTypes.Contains(type))
            return null;

        return type == "string"
            ? MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                PredefinedType(Token(SyntaxKind.StringKeyword)),
                IdentifierName("Empty"))
            : PostfixUnaryExpression(
                SyntaxKind.SuppressNullableWarningExpression,
                LiteralExpression(SyntaxKind.NullLiteralExpression));
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

        if (InitializerFor(property.Type) is { } initializer)
        {
            propertyDeclaration = propertyDeclaration
                .WithInitializer(EqualsValueClause(initializer))
                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
        }

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

        // Attributes named directly on the property, alongside the rule-derived ones above.
        attributes.AddRange(property.Attributes.Select(a => Attribute(IdentifierName(a))));

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