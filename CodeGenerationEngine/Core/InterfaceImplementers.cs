using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// The classes in the workspace that implement an interface, for the warnings after one of its
/// members is added, removed, renamed or retyped.
/// </summary>
/// <remarks>
/// The member edits change the declaration only and say so, so the implementers are named rather
/// than edited: a member added to <c>IShape</c> leaves <c>class Circle : IShape</c> failing with
/// CS0535, a rename or retype with CS0535 or CS0738, and what Circle should do about it - a body,
/// a different name, a conversion - is not something the command can know. Syntax only, by the
/// name in the base list, so a same-named interface in another namespace also matches; that reads
/// as a warning, not a claim.
/// </remarks>
public static class InterfaceImplementers
{
    private const int MaxShown = 5;

    /// <summary>The interface's name as declared, or null when <paramref name="typeName"/> is not an interface.</summary>
    public static string? InterfaceNamed(string workspaceRoot, string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
            return null;

        return ClassMethodEditService.TryLoad(workspaceRoot, typeName, out _, out _, out var node, out _)
            && node is InterfaceDeclarationSyntax
                ? node.Identifier.Text
                : null;
    }

    /// <summary>The classes, structs and records whose base list names <paramref name="interfaceName"/>.</summary>
    public static IReadOnlyList<TypeDeclarationSyntax> Of(string workspaceRoot, string interfaceName)
    {
        var found = new List<TypeDeclarationSyntax>();

        foreach (var file in ClassFiles.Under(workspaceRoot))
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (!text.Contains(interfaceName, StringComparison.Ordinal))
                continue;

            found.AddRange(CSharpSyntaxTree.ParseText(text).GetRoot().DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .Where(t => t is not InterfaceDeclarationSyntax
                    && t.BaseList?.Types.Any(b => NameOf(b.Type) == interfaceName) == true));
        }

        return found;
    }

    /// <summary>
    /// After <paramref name="member"/> was added to the interface: the implementers that do not
    /// declare it yet, or null when there are none or <paramref name="typeName"/> is not an interface.
    /// </summary>
    public static string? AddedWarning(string workspaceRoot, string typeName, string member)
    {
        if (!ClassMethodEditService.TryLoad(workspaceRoot, typeName, out _, out _, out var node, out _)
            || node is not InterfaceDeclarationSyntax declared)
            return null;

        var iface = declared.Identifier.Text;
        var wanted = declared.Members.Where(m => DeclaredName(m) == member).Select(SignatureOf).ToList();

        // By name, so a partial class with the member in its other file is not named.
        var lacking = new List<TypeDeclarationSyntax>();
        var mismatched = new List<string>();
        foreach (var group in Of(workspaceRoot, iface).GroupBy(t => t.Identifier.Text))
        {
            var have = group.SelectMany(t => t.Members).Where(m => DeclaredName(m) == member).ToList();
            if (have.Count == 0)
            {
                lacking.AddRange(group);
                continue;
            }

            // Same name, another signature: the class has double Perimeter() and the interface now
            // asks for void Perimeter(), which is CS0738, or CS0535 for other parameters.
            var signatures = have.Select(SignatureOf).ToList();
            if (wanted.Count > 0 && !wanted.All(w => signatures.Contains(w)))
                mismatched.Add($"{group.Key} has {string.Join(" and ", signatures.Distinct())}");
        }

        var warnings = new List<string>();
        if (lacking.Count > 0)
        {
            var one = lacking.Count == 1;

            // By its signature when there is one: "has no Scale" did not say what to add once a
            // method could take parameters.
            var what = wanted.Count == 1 ? wanted[0] : member;
            warnings.Add($"{Names(lacking)} {(one ? "implements" : "implement")} {iface} but {(one ? "has" : "have")} no {what} yet: "
                + $"add it there, or {(one ? "that class" : "those classes")} will not compile.");
        }

        if (mismatched.Count > 0)
            warnings.Add($"{iface} now declares {string.Join(" and ", wanted)}, but {string.Join("; ", mismatched.Take(MaxShown))}"
                + (mismatched.Count > MaxShown ? $" and {mismatched.Count - MaxShown} more" : string.Empty)
                + ": change one to match the other, or that code will not compile.");

        return warnings.Count == 0 ? null : string.Join(" ", warnings);
    }

    // The return or property type and the parameter types, as written, with whitespace removed:
    // double Perimeter(), int Count.
    private static string SignatureOf(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => $"{Squeeze(m.ReturnType)} {m.Identifier.Text}"
            + $"({string.Join(", ", m.ParameterList.Parameters.Select(p => p.Type == null ? "" : Squeeze(p.Type)))})",
        PropertyDeclarationSyntax p => $"{Squeeze(p.Type)} {p.Identifier.Text}",
        EventDeclarationSyntax e => $"event {Squeeze(e.Type)} {e.Identifier.Text}",
        _ => member.ToString(),
    };

    private static string Squeeze(TypeSyntax type) =>
        string.Concat(type.ToString().Where(c => !char.IsWhiteSpace(c))).Replace(",", ", ");

    /// <summary>
    /// After a member of the interface was removed, renamed or retyped: the implementers, which
    /// were left as they are, and <paramref name="consequence"/>; null when there are none or
    /// <paramref name="typeName"/> is not an interface.
    /// </summary>
    public static string? ChangedWarning(string workspaceRoot, string typeName, string consequence)
    {
        if (InterfaceNamed(workspaceRoot, typeName) is not { } iface)
            return null;

        var implementers = Of(workspaceRoot, iface);
        if (implementers.Count == 0)
            return null;

        var one = implementers.Count == 1;
        return $"{Names(implementers)} {(one ? "implements" : "implement")} {iface} and {(one ? "was" : "were")} not changed: {consequence}";
    }

    /// <summary>
    /// Why a validation rule cannot go on <paramref name="iface"/>: a validator checks the data an
    /// object holds, and an interface holds none.
    /// </summary>
    public static string ValidationRefusal(string iface, IReadOnlyList<string>? implementers = null)
    {
        var example = implementers is { Count: > 0 }
            ? $", such as {string.Join(" or ", implementers.Distinct(StringComparer.Ordinal).Take(3))}"
            : string.Empty;
        return $"'{iface}' is an interface, so it holds no data to validate. "
            + $"Add the rule to a class that implements it{example}.";
    }

    /// <summary>
    /// <see cref="ValidationRefusal"/> for <paramref name="typeName"/> in the workspace, or null when
    /// it is not an interface.
    /// </summary>
    public static string? ValidationRefusalFor(string workspaceRoot, string typeName) =>
        InterfaceNamed(workspaceRoot, typeName) is { } iface
            ? ValidationRefusal(iface, [.. Of(workspaceRoot, iface).Select(t => t.Identifier.Text)])
            : null;

    private static string Names(IReadOnlyList<TypeDeclarationSyntax> types)
    {
        var names = types.Select(t => t.Identifier.Text).Distinct(StringComparer.Ordinal).ToList();
        return string.Join(", ", names.Take(MaxShown))
            + (names.Count > MaxShown ? $" and {names.Count - MaxShown} more" : string.Empty);
    }

    // IShape, Geo.IShape, global::Geo.IShape and IShape<T> all name IShape.
    private static string NameOf(TypeSyntax type) => type switch
    {
        QualifiedNameSyntax q => NameOf(q.Right),
        AliasQualifiedNameSyntax a => NameOf(a.Name),
        GenericNameSyntax g => g.Identifier.Text,
        IdentifierNameSyntax i => i.Identifier.Text,
        _ => type.ToString(),
    };

    private static string? DeclaredName(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => m.Identifier.Text,
        PropertyDeclarationSyntax p => p.Identifier.Text,
        EventDeclarationSyntax e => e.Identifier.Text,
        _ => null,
    };
}
