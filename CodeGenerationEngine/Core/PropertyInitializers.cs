using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// The initial value a property needs so that a project with nullable reference types on does
/// not warn about it (CS8618).
/// </summary>
/// <remarks>
/// <c>add property Phone string</c> wrote <c>public string Phone { get; set; }</c> into a
/// <c>&lt;Nullable&gt;enable&lt;/Nullable&gt;</c> project, and a retype that could not keep the old
/// initial value (<c>= new();</c> on a list retyped to an array) took it out and put nothing in
/// its place. Both warn, and the warning is true: the property starts as null.
/// </remarks>
internal static class PropertyInitializers
{
    private static readonly HashSet<string> ConcreteCollections = new(StringComparer.Ordinal)
    {
        "List", "HashSet", "Dictionary", "SortedDictionary", "SortedList", "SortedSet", "Queue",
        "Stack", "LinkedList", "Collection", "ObservableCollection", "ConcurrentDictionary",
    };

    private static readonly HashSet<string> CollectionInterfaces = new(StringComparer.Ordinal)
    {
        "IEnumerable", "IList", "ICollection", "IReadOnlyList", "IReadOnlyCollection",
    };

    private static readonly HashSet<string> DictionaryInterfaces = new(StringComparer.Ordinal)
    {
        "IDictionary", "IReadOnlyDictionary",
    };

    /// <summary>
    /// Whether nullable reference types are on where <paramref name="root"/> is: a
    /// <c>#nullable</c> directive in the file decides, else the project's <c>Nullable</c> setting.
    /// </summary>
    public static bool NullableOn(string workspaceRoot, string file, SyntaxNode root)
    {
        var directive = root.DescendantTrivia()
            .Select(t => t.GetStructure())
            .OfType<NullableDirectiveTriviaSyntax>()
            .LastOrDefault();

        if (directive is not null)
            return directive.SettingToken.IsKind(SyntaxKind.EnableKeyword);

        var folder = TypeResolution.ProjectFolderOf(workspaceRoot, file);
        if (folder is null)
            return false;

        try
        {
            var project = Directory.EnumerateFiles(folder, "*.csproj").FirstOrDefault();
            if (project is null)
                return false;

            var value = XDocument.Load(project).Descendants()
                .LastOrDefault(e => e.Name.LocalName == "Nullable")?.Value.Trim();

            return string.Equals(value, "enable", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return false;
        }
    }

    /// <summary>
    /// The initial value a property of <paramref name="type"/> needs, or null when it needs none:
    /// a value type, a nullable type, or a type that could not be bound.
    /// </summary>
    /// <param name="type">The type as written.</param>
    /// <param name="bound">What it binds to, when that is known.</param>
    public static string? SafeFor(TypeSyntax type, ITypeSymbol? bound)
    {
        switch (type)
        {
            case NullableTypeSyntax:
                return null;

            case PredefinedTypeSyntax predefined:
                return predefined.Keyword.Kind() switch
                {
                    SyntaxKind.StringKeyword => "string.Empty",
                    SyntaxKind.ObjectKeyword => "new()",
                    _ => null,
                };

            case ArrayTypeSyntax:
                return "[]";
        }

        var generic = type switch
        {
            GenericNameSyntax g => g,
            QualifiedNameSyntax { Right: GenericNameSyntax g } => g,
            AliasQualifiedNameSyntax { Name: GenericNameSyntax g } => g,
            _ => null,
        };

        if (generic is not null)
        {
            var name = generic.Identifier.Text;

            if (ConcreteCollections.Contains(name))
                return "new()";

            if (CollectionInterfaces.Contains(name))
                return "[]";

            if (DictionaryInterfaces.Contains(name))
                return $"new Dictionary{generic.TypeArgumentList}()";
        }

        if (type.ToString() is "String" or "System.String")
            return "string.Empty";

        // Any other reference type - a class of the project, most often - starts as null until it
        // is set, as an entity's navigation property does. Said so, rather than inventing an
        // instance with `new()` that may not even have a constructor to call.
        return bound is { IsReferenceType: true } and not ITypeParameterSymbol ? "null!" : null;
    }

    /// <summary>
    /// Whether <paramref name="type"/> can take a property initializer: not an interface, and a
    /// struct only with a constructor of its own (CS8983).
    /// </summary>
    public static bool CanHaveInitializer(TypeDeclarationSyntax type) => type switch
    {
        InterfaceDeclarationSyntax => false,
        StructDeclarationSyntax s => s.ParameterList is not null
            || s.Members.OfType<ConstructorDeclarationSyntax>().Any(),
        RecordDeclarationSyntax r when r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword)
            => r.ParameterList is not null || r.Members.OfType<ConstructorDeclarationSyntax>().Any(),
        _ => true,
    };
}
