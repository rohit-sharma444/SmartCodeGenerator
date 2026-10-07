using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Changes the declared type of a property in one class. Scope matches
/// <see cref="ClassPropertyRenameService"/>: the declaration only.
/// </summary>
public static class ClassPropertyTypeService
{
    public static bool ChangeType(
        string workspaceRoot, string className, string propertyName, string newType, out string message)
    {
        var type = SyntaxFactory.ParseTypeName(newType ?? string.Empty);

        // ParseTypeName always returns a node; a stray character or an unclosed generic
        // shows as diagnostics or as text it stopped short of, and either would be written
        // into the file as code that does not compile.
        if (string.IsNullOrWhiteSpace(newType) || type.ContainsDiagnostics
            || type.ToString() != newType.Trim())
        {
            message = $"'{newType}' is not a C# type.";
            return false;
        }

        if (!ClassMethodEditService.TryLoadParts(workspaceRoot, className, out var parts, out message))
            return false;

        // Exact name first, then another case of it, in any of the class's partial files, as
        // remove property finds it.
        if (ClassMethodEditService.PropertyNamed(parts, propertyName) is not (var part, var property))
        {
            message = ClassMethodEditService.NotFound(parts, propertyName, "Property");
            return false;
        }

        // As declared, for the messages: "item" found Item.
        className = part.Node.Identifier.Text;

        var name = property.Identifier.Text;

        if (property.Type.ToString() == type.ToString())
        {
            message = $"'{className}.{name}' is already {type}.";
            return true;
        }

        if (!TypeResolution.Resolves(workspaceRoot, part.File, part.Node, type, out message))
            return false;

        var dropped = new List<string>();
        var retyped = WithoutAttributesFor(property.WithType(type.WithTriviaFrom(property.Type)), type, dropped);

        var keepsInitializer = true;
        var computed = property.ExpressionBody != null
            || property.AccessorList?.Accessors.Any(a => a.Body != null || a.ExpressionBody != null) == true;

        // An initial value or a computed body was written for the old type. Compiled to find out
        // whether it still fits, rather than guessed: int to long keeps "= 1", string to int cannot
        // keep "= \"\"", and a computed value that no longer converts is refused, not written.
        if (property.Initializer != null || computed)
        {
            var before = TypeResolution.ErrorsWithin(workspaceRoot, part.File, part.Root, property.Span)
                .Select(d => d.Id).ToList();

            var marker = new SyntaxAnnotation();
            var probeRoot = part.Root.ReplaceNode(property, retyped.WithAdditionalAnnotations(marker));
            var probed = (PropertyDeclarationSyntax)probeRoot.GetAnnotatedNodes(marker).Single();

            var fresh = new List<Diagnostic>();
            foreach (var error in TypeResolution.ErrorsWithin(workspaceRoot, part.File, probeRoot, probed.Span))
                if (!before.Remove(error.Id))
                    fresh.Add(error);

            bool In(SyntaxNode? node, Diagnostic d) => node != null && node.Span.Contains(d.Location.SourceSpan);

            var bodies = new List<SyntaxNode?> { probed.ExpressionBody };
            if (probed.AccessorList != null)
                bodies.AddRange(probed.AccessorList.Accessors.SelectMany(a => new SyntaxNode?[] { a.Body, a.ExpressionBody }));

            if (fresh.FirstOrDefault(d => bodies.Any(b => In(b, d))) is { } bodyError)
            {
                message = $"'{className}.{name}' is computed, and what it computes no longer fits {type} "
                    + $"({bodyError.GetMessage()}). Nothing was changed; change its type and its body by hand.";
                return false;
            }

            keepsInitializer = !fresh.Any(d => In(probed.Initializer, d));
        }

        if (property.Initializer != null && !keepsInitializer)
            retyped = retyped
                .WithInitializer(null)
                .WithSemicolonToken(default)
                // The line break after the `;` would go with it.
                .WithAccessorList(retyped.AccessorList!.WithTrailingTrivia(property.SemicolonToken.TrailingTrivia));

        var root = part.Root.ReplaceNode(property, retyped);

        return ClassMethodEditService.TryWrite(part.File, root,
            $"'{className}.{name}' changed from {property.Type} to {type}."
            + (property.Initializer != null && !keepsInitializer
                ? $" Its initial value ({property.Initializer!.Value}) was removed." : string.Empty)
            + (dropped.Count > 0
                ? $" Removed {string.Join(", ", dropped.Select(d => $"[{d}]"))}, which {(dropped.Count == 1 ? "does" : "do")} not apply to {type}." : string.Empty),
            out message);
    }

    // Validation attributes that apply to text only, and those that also apply to an array or a
    // collection. Left on a number they compile and then throw when the record is validated.
    private static readonly string[] TextOnly = ["StringLength", "EmailAddress", "Phone", "Url", "CreditCard"];
    private static readonly string[] TextOrCollection = ["MaxLength", "MinLength", "Length"];

    private static PropertyDeclarationSyntax WithoutAttributesFor(
        PropertyDeclarationSyntax property, TypeSyntax type, List<string> dropped)
    {
        var bare = type is NullableTypeSyntax nullable ? nullable.ElementType : type;
        var isText = bare is PredefinedTypeSyntax p && p.Keyword.IsKind(SyntaxKind.StringKeyword)
            || bare.ToString() is "String" or "System.String";
        var isCollection = bare is ArrayTypeSyntax or GenericNameSyntax
            || bare is QualifiedNameSyntax { Right: GenericNameSyntax };

        if (isText)
            return property;

        bool Fits(AttributeSyntax attribute)
        {
            var written = attribute.Name switch
            {
                QualifiedNameSyntax q => q.Right.Identifier.Text,
                AliasQualifiedNameSyntax a => a.Name.Identifier.Text,
                SimpleNameSyntax s => s.Identifier.Text,
                _ => attribute.Name.ToString(),
            };
            var bareName = written.EndsWith("Attribute", StringComparison.Ordinal) ? written[..^"Attribute".Length] : written;

            if (TextOnly.Contains(bareName) || (!isCollection && TextOrCollection.Contains(bareName)))
            {
                dropped.Add(bareName);
                return false;
            }

            return true;
        }

        var lists = new List<AttributeListSyntax>();
        SyntaxTriviaList? orphaned = null;

        foreach (var list in property.AttributeLists)
        {
            var keep = list.Attributes.Where(Fits).ToList();

            if (keep.Count == list.Attributes.Count)
                lists.Add(list);
            else if (keep.Count > 0)
                lists.Add(list.WithAttributes(SyntaxFactory.SeparatedList(keep,
                    Enumerable.Repeat(SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space), keep.Count - 1))));
            else if (lists.Count == 0 && orphaned == null)
                // The first list carries the member's doc comment and indentation; they move on.
                orphaned = list.GetLeadingTrivia();
        }

        if (lists.Count == property.AttributeLists.Count && dropped.Count == 0)
            return property;

        var result = property.WithAttributeLists(SyntaxFactory.List(lists));
        return orphaned is { } leading ? result.WithLeadingTrivia(leading) : result;
    }
}
