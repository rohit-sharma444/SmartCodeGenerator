using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Adds a constructor to a class, record or struct already in the workspace: one parameter for
/// each property it takes, each assigned. The edited file is compiled beside the rest of its
/// project first; a constructor that would not compile (a base class with no parameterless
/// constructor, for one) is refused with the compiler's text and nothing is written.
/// </summary>
public static class ConstructorService
{
    private const string SetsRequired = "[System.Diagnostics.CodeAnalysis.SetsRequiredMembers]";

    /// <summary>
    /// Adds a constructor taking <paramref name="propertyNames"/>, in that order; an empty list means
    /// every instance auto-property the class declares that is not private, in declaration order.
    /// <paramref name="notes"/> says what the success message cannot: the required properties the
    /// constructor does not set.
    /// </summary>
    public static bool AddConstructor(
        string workspaceRoot, string className, IReadOnlyList<string> propertyNames,
        out string message, out IReadOnlyList<string> notes)
    {
        notes = [];

        try
        {
            if (!ClassMethodEditService.TryLoadParts(workspaceRoot, className, out var parts, out message))
                return false;

            var part = parts[0];
            var type = part.Node;
            className = type.Identifier.Text;

            if (KindRefusal(type) is { } refusal)
            {
                message = refusal;
                return false;
            }

            var properties = new List<PropertyDeclarationSyntax>();
            if (propertyNames.Count == 0)
            {
                properties.AddRange(parts.SelectMany(p => p.Node.Members).OfType<PropertyDeclarationSyntax>()
                    .Where(p => IsInstanceAuto(p) && !IsPrivate(p)));
            }
            else
            {
                foreach (var wanted in propertyNames)
                {
                    var found = Named(parts, wanted);
                    if (found == null)
                    {
                        message = $"'{wanted}' is not a property of '{className}'"
                            + (AnyNamed(parts, wanted)
                                ? " that a constructor can set: it is static or computed."
                                : ".");
                        return false;
                    }

                    if (properties.Contains(found))
                    {
                        message = $"'{found.Identifier.Text}' is listed twice.";
                        return false;
                    }

                    properties.Add(found);
                }
            }

            var parameters = properties
                .Select(p => (Type: p.Type.ToString(), Name: ParameterName(p.Identifier.Text), Property: p.Identifier.Text))
                .ToList();

            var typeList = string.Join(", ", parameters.Select(p => p.Type));
            if (parts.SelectMany(p => p.Node.Members).OfType<ConstructorDeclarationSyntax>()
                .Any(c => !c.Modifiers.Any(SyntaxKind.StaticKeyword)
                    && string.Join(", ", c.ParameterList.Parameters.Select(x => x.Type?.ToString())) == typeList))
            {
                message = $"'{className}' already has a constructor taking ({typeList}). It was not added.";
                return false;
            }

            // A required property the constructor sets all of makes the attribute right; otherwise
            // the caller still has to set the rest, and the note says which.
            var required = parts.SelectMany(p => p.Node.Members).OfType<PropertyDeclarationSyntax>()
                .Where(p => p.Modifiers.Any(SyntaxKind.RequiredKeyword) && !p.Modifiers.Any(SyntaxKind.StaticKeyword))
                .Select(p => p.Identifier.Text)
                .ToList();
            var unset = required.Where(r => parameters.All(p => p.Property != r)).ToList();

            var code = (required.Count > 0 && unset.Count == 0 ? SetsRequired + Environment.NewLine : string.Empty)
                + $"public {className}({string.Join(", ", parameters.Select(p => $"{p.Type} {p.Name}"))}) "
                + "{ " + string.Concat(parameters.Select(p => $"{p.Property} = {p.Name}; ")) + "}";

            if (CompileRefusal(workspaceRoot, part, code) is { } compileError)
            {
                message = compileError;
                return false;
            }

            if (!ClassMemberEditor.TryAddMember(part.File, className, code, out var error))
            {
                message = error;
                return false;
            }

            if (unset.Count > 0)
                notes =
                [
                    $"{string.Join(" and ", unset.Select(u => $"'{u}'"))} {(unset.Count == 1 ? "is" : "are")} required "
                    + "and not set by this constructor; callers must set "
                    + $"{(unset.Count == 1 ? "it" : "them")} in an object initializer."
                ];

            message = $"Constructor {className}({typeList}) added to '{className}'.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = ex.Message;
            return false;
        }
    }

    // A kind that cannot take the constructor, in the kind's own terms. An enum is refused by
    // TryLoadParts with its own message.
    private static string? KindRefusal(TypeDeclarationSyntax type)
    {
        var name = type.Identifier.Text;

        if (type is InterfaceDeclarationSyntax)
            return $"'{name}' is an interface; an interface has no constructor.";

        if (type.Modifiers.Any(SyntaxKind.StaticKeyword))
            return $"'{name}' is a static class; a static class has no instance constructor.";

        if (type is RecordDeclarationSyntax { ParameterList: not null })
            return $"'{name}' is a positional record; its own constructor already sets its properties.";

        return null;
    }

    // A compiled instance auto-property: { get; set; }, { get; init; } or { get; }.
    private static bool IsInstanceAuto(PropertyDeclarationSyntax p) =>
        p.ExplicitInterfaceSpecifier == null
        && p.ExpressionBody == null
        && p.AccessorList != null
        && p.AccessorList.Accessors.All(a => a.Body == null && a.ExpressionBody == null)
        && !p.Modifiers.Any(SyntaxKind.StaticKeyword)
        && !p.Modifiers.Any(SyntaxKind.AbstractKeyword);

    // Private by keyword, or by default: a member of a class or struct with no access word.
    private static bool IsPrivate(PropertyDeclarationSyntax p) =>
        p.Modifiers.Any(SyntaxKind.PrivateKeyword)
        || !p.Modifiers.Any(m => m.Kind() is SyntaxKind.PublicKeyword or SyntaxKind.InternalKeyword
            or SyntaxKind.ProtectedKeyword);

    // The auto-property called wanted: the exact spelling before another case.
    private static PropertyDeclarationSyntax? Named(List<ClassPart> parts, string wanted)
    {
        var all = parts.SelectMany(p => p.Node.Members).OfType<PropertyDeclarationSyntax>()
            .Where(IsInstanceAuto)
            .Where(p => p.Identifier.Text.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return all.FirstOrDefault(p => p.Identifier.Text == wanted) ?? all.FirstOrDefault();
    }

    private static bool AnyNamed(List<ClassPart> parts, string wanted) =>
        parts.SelectMany(p => p.Node.Members).OfType<PropertyDeclarationSyntax>()
            .Any(p => p.Identifier.Text.Equals(wanted, StringComparison.OrdinalIgnoreCase));

    private static string ParameterName(string property)
    {
        var name = char.ToLowerInvariant(property[0]) + property[1..];
        return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
    }

    // The file with the constructor in it, compiled beside the rest of its project: the compiler's
    // errors inside the new constructor, or null when there are none.
    private static string? CompileRefusal(string workspaceRoot, ClassPart part, string code)
    {
        var source = Validation.SourceFileText.Load(part.File, out _, out var newline);
        var tree = CSharpSyntaxTree.ParseText(source);
        var types = tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>().ToList();
        var index = part.Root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToList().IndexOf(part.Node);
        if (index < 0 || index >= types.Count)
            return null;

        var member = SyntaxFactory.ParseMemberDeclaration(code);
        if (member == null || member.ContainsDiagnostics)
            return "Could not parse the generated constructor.";

        var edited = ClassMemberEditor.Insert(source, types[index], member, newline);
        var editedRoot = CSharpSyntaxTree.ParseText(edited).GetRoot();
        var editedType = editedRoot.DescendantNodes().OfType<TypeDeclarationSyntax>().ElementAt(index);
        var added = editedType.Members.OfType<ConstructorDeclarationSyntax>().LastOrDefault();
        if (added == null)
            return null;

        var errors = TypeResolution.ErrorsWithin(workspaceRoot, part.File, editedRoot, added.Span);
        if (errors.Count == 0)
            return null;

        return $"'{part.Node.Identifier.Text}' was not changed: the constructor would not compile."
            + string.Concat(errors.Select(e => Environment.NewLine + $"  {e.Id}: {e.GetMessage()}"));
    }
}
