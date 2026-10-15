using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Deletes or renames a method declared in one class. Scope matches
/// <see cref="ClassPropertyRenameService"/>: the declaration only, no reference updates,
/// and the file is written back with <c>ToFullString</c> so the user's layout survives.
/// </summary>
public static class ClassMethodEditService
{
    /// <summary>
    /// Removes the method. Refuses when the name is overloaded: which one the user meant
    /// cannot be told from a name, and deleting all of them would take more than was asked.
    /// </summary>
    public static bool DeleteMethod(string workspaceRoot, string className, string methodName, out string message)
    {
        if (!TryLoadParts(workspaceRoot, className, out var parts, out message))
            return false;

        // As declared, for the messages: "order" found Order.
        className = parts[0].Node.Identifier.Text;

        var matches = MethodsNamed(parts, methodName);

        if (matches.Count == 0)
        {
            message = NotFound(parts, methodName, "Method");
            return false;
        }

        var name = matches[0].Member.Identifier.Text;

        if (matches.Count > 1)
        {
            message = $"'{className}' has {matches.Count} methods named '{name}', so none was removed:"
                + string.Concat(matches.Select(m => Environment.NewLine + "  " + Signature(m.Member)));
            return false;
        }

        var (part, method) = matches[0];
        var root = MemberRemoval.Remove(part.Root, method);

        return TryWrite(part.File, root, $"Method '{name}' removed from '{className}'.", out message);
    }

    /// <summary>
    /// Renames the method — every overload of it, since they share the one name, in whichever of
    /// the class's partial files each one is.
    /// </summary>
    public static bool RenameMethod(
        string workspaceRoot, string className, string oldName, string newName, out string message)
    {
        if (!TryLoadParts(workspaceRoot, className, out var parts, out message))
            return false;

        // As declared, for the messages: "order" found Order.
        className = parts[0].Node.Identifier.Text;

        var matches = MethodsNamed(parts, oldName);

        if (matches.Count == 0)
        {
            message = NotFound(parts, oldName, "Method");
            return false;
        }

        var name = matches[0].Member.Identifier.Text;

        if (MemberNames.NotAnIdentifier(newName, "method") is { } bad)
        {
            message = bad;
            return false;
        }

        if (parts.Any(p => p.Node.Members.OfType<MethodDeclarationSyntax>()
                .Any(m => m.Identifier.Text.Equals(newName, StringComparison.Ordinal))))
        {
            message = $"Method '{newName}' already exists in '{className}'.";
            return false;
        }

        if (MemberNames.Taken(parts, newName, matches.Select(m => m.Member)) is { } taken)
        {
            message = taken;
            return false;
        }

        foreach (var group in matches.GroupBy(m => m.Part))
        {
            var root = group.Key.Root.ReplaceNodes(group.Select(m => m.Member), (original, _) =>
                original.WithIdentifier(SyntaxFactory.Identifier(newName).WithTriviaFrom(original.Identifier)));

            if (!TryWrite(group.Key.File, root, string.Empty, out message))
                return false;
        }

        var overloads = matches.Count > 1 ? $" ({matches.Count} overloads)" : string.Empty;

        message = $"Method '{name}' renamed to '{newName}' in '{className}'{overloads}. "
            + "References elsewhere were not updated.";
        return true;
    }

    private static List<(ClassPart Part, MethodDeclarationSyntax Member)> MethodsNamed(
        IReadOnlyList<ClassPart> parts, string name) =>
        [.. Named(parts.SelectMany(p => p.Node.Members.OfType<MethodDeclarationSyntax>().Select(m => (Part: p, Member: m))),
            x => x.Member.Identifier.Text, name)];

    /// <summary>
    /// The items called <paramref name="wanted"/>: the exact spelling first, else a different case of
    /// it, as remove property has always matched. A different case that fits two different names
    /// (Save and SAVE) matches neither, since which was meant cannot be told.
    /// </summary>
    internal static List<T> Named<T>(IEnumerable<T> items, Func<T, string> nameOf, string wanted)
    {
        var all = items.ToList();
        var exact = all.Where(i => nameOf(i).Equals(wanted, StringComparison.Ordinal)).ToList();
        if (exact.Count > 0)
            return exact;

        var loose = all.Where(i => nameOf(i).Equals(wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        return loose.Select(nameOf).Distinct(StringComparer.Ordinal).Count() == 1 ? loose : [];
    }

    /// <summary>The declared name of the method called <paramref name="name"/> in any part, or null.</summary>
    internal static string? MethodNamed(IReadOnlyList<ClassPart> parts, string name) =>
        MethodsNamed(parts, name) is { Count: > 0 } found ? found[0].Member.Identifier.Text : null;

    /// <summary>The first property called <paramref name="name"/> in any part, with its part.</summary>
    internal static (ClassPart Part, PropertyDeclarationSyntax Property)? PropertyNamed(
        IReadOnlyList<ClassPart> parts, string name)
    {
        var found = Named(
            parts.SelectMany(p => p.Node.Members.OfType<PropertyDeclarationSyntax>().Select(m => (Part: p, Member: m))),
            x => x.Member.Identifier.Text, name);
        return found.Count > 0 ? (found[0].Part, found[0].Member) : null;
    }

    /// <summary>
    /// Why no <paramref name="kind"/> called <paramref name="wanted"/> was found: none has the name
    /// in any case, or it fits two that differ only in case (Email and email), which
    /// <see cref="Named"/> refuses to choose between.
    /// </summary>
    internal static string NotFound(IReadOnlyList<ClassPart> parts, string wanted, string kind)
    {
        var className = parts.Count > 0 ? parts[0].Node.Identifier.Text : wanted;
        var fits = parts.SelectMany(p => p.Node.Members)
            .Where(m => kind == "Method" ? m is MethodDeclarationSyntax : m is PropertyDeclarationSyntax)
            .Select(m => m is MethodDeclarationSyntax md ? md.Identifier.Text : ((PropertyDeclarationSyntax)m).Identifier.Text)
            .Where(n => n.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return fits.Count > 1
            ? $"'{wanted}' is ambiguous in '{className}': it fits {string.Join(" and ", fits)}. Type the name with its exact case."
            : $"{kind} '{wanted}' not found in '{className}'.";
    }

    private static string Signature(MethodDeclarationSyntax method) =>
        $"{method.ReturnType} {method.Identifier.Text}{method.ParameterList}";

    /// <summary>
    /// The class's declaration in <c>&lt;Class&gt;.cs</c> first, then, when it is partial, its other
    /// partial declarations in the workspace: a member added by hand to Order.Logic.cs is as much the
    /// class's as one in Order.cs. One per file, in the same namespace.
    /// </summary>
    internal static bool TryLoadParts(
        string workspaceRoot, string className, out List<ClassPart> parts, out string message)
    {
        parts = [];
        if (!TryLoad(workspaceRoot, className, out var file, out var root, out var classNode, out message))
            return false;

        parts.Add(new ClassPart(file, root, classNode));
        if (!classNode.Modifiers.Any(SyntaxKind.PartialKeyword))
            return true;

        var name = classNode.Identifier.Text;
        var ns = NamespaceOf(classNode);

        foreach (var other in ClassFiles.Under(workspaceRoot))
        {
            if (string.Equals(Path.GetFullPath(other), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase))
                continue;

            string text;
            try
            {
                text = File.ReadAllText(other);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (!text.Contains("partial", StringComparison.Ordinal) || !text.Contains(name, StringComparison.Ordinal))
                continue;

            var otherRoot = CSharpSyntaxTree.ParseText(text).GetRoot();
            var part = otherRoot.DescendantNodes().OfType<TypeDeclarationSyntax>()
                .FirstOrDefault(c => c.Kind() == classNode.Kind()
                    && c.Identifier.Text.Equals(name, StringComparison.Ordinal)
                    && c.Modifiers.Any(SyntaxKind.PartialKeyword)
                    && NamespaceOf(c) == ns);

            if (part != null)
                parts.Add(new ClassPart(other, otherRoot, part));
        }

        return true;
    }

    // The namespace and any enclosing types: a nested partial Order is not the top-level one.
    private static string NamespaceOf(TypeDeclarationSyntax node) =>
        string.Join(".", node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString()))
        + "/" + string.Join(".", node.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Select(t => t.Identifier.Text));

    // Find the class's file by name, then the class in it - or the interface: create interface
    // writes one, and its members are edited by the same commands.
    internal static bool TryLoad(
        string workspaceRoot, string className,
        out string file, out SyntaxNode root, out TypeDeclarationSyntax classNode, out string message)
    {
        file = string.Empty;
        root = null!;
        classNode = null!;
        message = string.Empty;

        try
        {
            var found = Directory
                .GetFiles(workspaceRoot, "*.cs", SearchOption.AllDirectories)
                .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                    .Equals(className, StringComparison.OrdinalIgnoreCase));

            if (found != null)
            {
                file = found;
                root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();

                if (DeclarationOf(root, className) is { } node)
                {
                    classNode = node;
                    return true;
                }
            }

            // Not in a file of its name: a record, struct or interface declared in another type's
            // file (OrderLine in Order.cs) is listed by show properties, and was "not found" here.
            foreach (var other in ClassFiles.Under(workspaceRoot))
            {
                string text;
                try
                {
                    text = File.ReadAllText(other);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (!text.Contains(className, StringComparison.OrdinalIgnoreCase))
                    continue;

                var otherRoot = CSharpSyntaxTree.ParseText(text).GetRoot();
                if (DeclarationOf(otherRoot, className) is { } node)
                {
                    file = other;
                    root = otherRoot;
                    classNode = node;
                    return true;
                }
            }

            message = found == null
                ? $"Class '{className}' not found."
                : $"Class '{className}' not found in {Path.GetFileName(found)}.";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// The class, interface, record or struct called <paramref name="name"/> in
    /// <paramref name="root"/>: the exact spelling before another case, a top-level one before a
    /// nested one.
    /// </summary>
    private static TypeDeclarationSyntax? DeclarationOf(SyntaxNode root, string name) =>
        root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(t => t.Identifier.Text.Equals(name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Identifier.Text.Equals(name, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(t => t.Parent is TypeDeclarationSyntax ? 1 : 0)
            .FirstOrDefault();

    internal static bool TryWrite(string file, SyntaxNode root, string success, out string message)
    {
        try
        {
            Validation.SourceFileText.Rewrite(file, root.ToFullString());
            message = success;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = ex.Message;
            return false;
        }
    }
}

/// <summary>
/// One declaration of a class or an interface: the file, its parsed root, and the type node in it.
/// </summary>
internal sealed record ClassPart(string File, SyntaxNode Root, TypeDeclarationSyntax Node);
