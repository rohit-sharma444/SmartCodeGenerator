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

    // Read-only: an empty array is one, and needs no allocation per instance.
    private static readonly HashSet<string> ReadOnlyCollectionInterfaces = new(StringComparer.Ordinal)
    {
        "IEnumerable", "IReadOnlyList", "IReadOnlyCollection",
    };

    // Writable: Add on an array throws, so a list.
    private static readonly HashSet<string> CollectionInterfaces = new(StringComparer.Ordinal)
    {
        "IList", "ICollection",
    };

    private static readonly HashSet<string> DictionaryInterfaces = new(StringComparer.Ordinal)
    {
        "IDictionary", "IReadOnlyDictionary",
    };

    /// <summary>
    /// Whether nullable annotations are on at <paramref name="position"/> in
    /// <paramref name="root"/>'s file: the project's <c>Nullable</c> setting, as the
    /// <c>#nullable</c> directives before that point change it.
    /// </summary>
    /// <remarks>
    /// Asked of Roslyn rather than read off the last directive in the file, which was wrong three
    /// ways: a <c>#nullable disable</c> at the end of the file overrode an enable above the class,
    /// a <c>#nullable restore</c> read as off in a project that enables, and an enable after the
    /// class counted for it.
    /// </remarks>
    public static bool NullableOn(string workspaceRoot, string file, SyntaxNode root, int position)
    {
        var compilation = CSharpCompilation.Create(
            "NullableProbe", [root.SyntaxTree], [],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: ProjectNullable(workspaceRoot, file)));

        return compilation.GetSemanticModel(root.SyntaxTree)
            .GetNullableContext(position)
            .AnnotationsEnabled();
    }

    /// <summary>Where a member added to <paramref name="type"/> goes: before its closing brace, or
    /// at the end of a record with no body.</summary>
    public static int InsertionPoint(TypeDeclarationSyntax type) =>
        type.CloseBraceToken.IsKind(SyntaxKind.None) ? type.Span.End : type.CloseBraceToken.SpanStart;

    internal static NullableContextOptions ProjectNullable(string workspaceRoot, string file)
    {
        var folder = TypeResolution.ProjectFolderOf(workspaceRoot, file);
        if (folder is null)
            return NullableContextOptions.Disable;

        try
        {
            var project = Directory.EnumerateFiles(folder, "*.csproj").FirstOrDefault();
            if (project is null)
                return NullableContextOptions.Disable;

            var value = XDocument.Load(project).Descendants()
                .LastOrDefault(e => e.Name.LocalName == "Nullable")?.Value.Trim();

            return value?.ToLowerInvariant() switch
            {
                "enable" => NullableContextOptions.Enable,
                "annotations" => NullableContextOptions.Annotations,
                "warnings" => NullableContextOptions.Warnings,
                _ => NullableContextOptions.Disable,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return NullableContextOptions.Disable;
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

            case ArrayTypeSyntax array:
                return EmptyArray(array);
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

            // The type as written: it binds where the property is, so `new` of it does too, and
            // this is C# 2 where target-typed `new()` is C# 9.
            if (ConcreteCollections.Contains(name))
                return $"new {type}()";

            var arguments = generic.TypeArgumentList.Arguments;

            // `[]` is a C# 12 collection expression, and a net6 or net7 project is C# 10 or 11.
            // Qualified from global::, as nothing says System.Collections.Generic is in scope.
            if (ReadOnlyCollectionInterfaces.Contains(name) && arguments.Count == 1)
                return $"global::System.Array.Empty<{arguments[0]}>()";

            if (CollectionInterfaces.Contains(name) && arguments.Count == 1)
                return $"new global::System.Collections.Generic.List<{arguments[0]}>()";

            if (DictionaryInterfaces.Contains(name) && arguments.Count == 2)
                return $"new global::System.Collections.Generic.Dictionary<{arguments[0]}, {arguments[1]}>()";
        }

        if (type.ToString() is "String" or "System.String")
            return "string.Empty";

        // Any other reference type - a class of the project, most often - starts as null until it
        // is set, as an entity's navigation property does. Said so, rather than inventing an
        // instance with `new()` that may not even have a constructor to call.
        return bound is { IsReferenceType: true } and not ITypeParameterSymbol ? "null!" : null;
    }

    /// <summary>
    /// An empty array of <paramref name="array"/>'s type, in a form C# 6 compiles and that needs no
    /// using: <c>global::System.Array.Empty&lt;T&gt;()</c>, or for a multi-dimensional array
    /// <c>new T[0, 0]</c>.
    /// </summary>
    /// <remarks>
    /// The first rank specifier is the outermost array: <c>int[][,]</c> is a one-dimensional array
    /// of <c>int[,]</c>.
    /// </remarks>
    private static string EmptyArray(ArrayTypeSyntax array)
    {
        var outer = array.RankSpecifiers[0];
        var rest = string.Concat(array.RankSpecifiers.Skip(1).Select(r => r.ToString()));

        if (outer.Rank == 1)
            return $"global::System.Array.Empty<{array.ElementType}{rest}>()";

        var zeros = string.Join(", ", Enumerable.Repeat("0", outer.Rank));
        return $"new {array.ElementType}[{zeros}]{rest}";
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
