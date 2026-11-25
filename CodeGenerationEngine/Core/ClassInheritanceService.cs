using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// <c>&lt;class&gt; inherits|extends|derives from &lt;type&gt;</c>: adds a base class or an interface.
/// </summary>
/// <remarks>
/// The type used to be inserted first in the base list, whatever it was, and the edit always said
/// "now inherits": an interface before an existing base class (CS1722), a second base class
/// (CS1721), the class itself (CS0146) and a type that does not exist (CS0246) were all written.
/// The type is now bound in the class's project, as <see cref="ClassRenameService"/> compiles it -
/// its own files, the runtime's assemblies and its usings - so a project class and a framework type
/// are both known. A base class goes first and an interface after the rest; a type that does not
/// bind, the class itself, one of its own subclasses, a sealed or static class, a struct, an enum,
/// and a second base class are refused with the file left as it was.
/// The edited type may be a class, interface, struct, record or record struct. The members it then
/// lacks used to be only named, so the project no longer built (CS0535); they are now stubbed by
/// <see cref="MemberStubs"/>, as <c>create class</c> stubs them.
/// </remarks>
public static class ClassInheritanceService
{
    public static bool AddInheritance(
        string workspaceRoot,
        string className,
        string baseClass,
        out string message) =>
        AddInheritance(workspaceRoot, className, [baseClass], interfacesOnly: false, out message);

    /// <summary>
    /// Adds every type in <paramref name="types"/> to <paramref name="className"/>'s base list, then
    /// stubs the members it now lacks. All or nothing: the first name refused is the message, and the
    /// file is left as it was. With <paramref name="interfacesOnly"/> (<c>implements</c>) a class is refused.
    /// </summary>
    public static bool AddInheritance(
        string workspaceRoot,
        string className,
        IReadOnlyList<string> types,
        bool interfacesOnly,
        out string message)
    {
        message = string.Empty;

        // By declaration when no file has its name: an interface declared in another type's file.
        if (!ClassFiles.TryFind(workspaceRoot, className, out var file, out message))
            return false;

        if (file == null)
        {
            if (!ClassMethodEditService.TryLoad(workspaceRoot, className, out var declaring, out _, out _, out message))
                return false;

            file = declaring;
        }

        if (types.Count == 0)
        {
            message = $"No type was named, so '{className}' was not changed.";
            return false;
        }

        try
        {
            return Add(workspaceRoot, file, className, [.. types.Select(t => (t ?? string.Empty).Trim())], interfacesOnly, out message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = ex.Message;
            return false;
        }
    }

    private static bool Add(
        string workspaceRoot, string file, string className, IReadOnlyList<string> types, bool interfacesOnly, out string message)
    {
        var csprojFolder = TypeResolution.ProjectFolderOf(workspaceRoot, file);
        var compilation = ClassRenameService.Compile("InheritProbe", csprojFolder ?? workspaceRoot, csprojFolder, [], out var trees);

        var tree = trees.FirstOrDefault(t => string.Equals(t.FilePath, Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase));
        if (tree == null)
        {
            message = $"Could not read {file}.";
            return false;
        }

        // By name: a file may hold several types, and the base type belongs to the one the user
        // named, not to whichever is declared first.
        var node = tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(c => c is ClassDeclarationSyntax or InterfaceDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax
                && c.Identifier.Text.Equals(className, StringComparison.OrdinalIgnoreCase));
        if (node == null)
        {
            message = $"Class '{className}' not found in {Path.GetFileName(file)}.";
            return false;
        }

        // Every edit is after the type's first character, so it is found again at the same place.
        var start = node.SpanStart;
        var text = tree.GetRoot().ToFullString();
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var done = new List<string>();

        // Each name is checked against the text the names before it made, so nothing is written
        // until all of them pass.
        foreach (var type in types)
        {
            if (!Check(compilation, tree, node, type, interfacesOnly, out var insertAt, out var insert, out var sentence))
            {
                message = sentence;
                return false;
            }

            done.Add(sentence);
            text = text.Insert(insertAt, insert);
            (compilation, tree, node) = Reparse(compilation, tree, text, start);
        }

        // The members the type now lacks, written as create class writes them, so it still compiles.
        var stubs = new List<MemberStub>();
        var isAbstract = false;
        if (node is not InterfaceDeclarationSyntax)
        {
            var model = compilation.GetSemanticModel(tree);
            if (model.GetDeclaredSymbol(node) is INamedTypeSymbol symbol)
            {
                isAbstract = symbol.IsAbstract;
                var position = HasBody(node) ? node.OpenBraceToken.Span.End : node.Identifier.SpanStart;
                stubs.AddRange(MemberStubs.Missing(symbol, model, position));
            }
        }

        if (stubs.Count > 0)
        {
            (text, var spans) = WithStubs(text, node, [.. stubs.Select(s => s.Code)], newline);
            (compilation, tree, node) = Reparse(compilation, tree, text, start);

            // A stub that only failed to match a member the type already has would declare that
            // name twice (CS0102, CS0111): refused, so the file is never left uncompilable.
            if (Clash(compilation, tree, node, stubs, spans) is { } clash)
            {
                message = clash;
                return false;
            }
        }

        Validation.SourceFileText.Rewrite(file, text);

        var names = string.Join(", ", stubs.Select(s => s.MemberName).Distinct());
        message = string.Join(" ", done)
            + (stubs.Count == 0 ? string.Empty
                : isAbstract ? $" Added {names}, which it lacked, as abstract declarations."
                : $" Added {names}, which it lacked; a method added this way throws NotImplementedException until you fill it in.")
            + Missing(compilation, tree, node);
        return true;
    }

    // Checks one name against the type as it now reads; on success, where and what to insert.
    private static bool Check(
        CSharpCompilation compilation, SyntaxTree tree, TypeDeclarationSyntax node, string baseClass, bool interfacesOnly,
        out int insertAt, out string insert, out string message)
    {
        insertAt = 0;
        insert = string.Empty;

        var name = node.Identifier.Text;
        var kind = KindOf(node);
        var model = compilation.GetSemanticModel(tree);
        var self = model.GetDeclaredSymbol(node);

        var typeSyntax = SyntaxFactory.ParseTypeName(baseClass);
        if (typeSyntax.ContainsDiagnostics || string.IsNullOrWhiteSpace(baseClass))
        {
            message = $"'{baseClass}' is not a type name.";
            return false;
        }

        // Bound where the base list would be, so the file's usings and namespace apply.
        var position = node.Identifier.SpanStart;
        var bound = model.GetSpeculativeTypeInfo(position, typeSyntax, SpeculativeBindingOption.BindAsTypeOrNamespace).Type;
        if (bound is not INamedTypeSymbol type || type.TypeKind == TypeKind.Error)
        {
            message = $"'{baseClass}' is not a type this project knows, so '{name}' was not changed. "
                + "Name a class or interface in the project, or a .NET type such as Exception or IDisposable.";
            return false;
        }

        var shown = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var existing = node.BaseList?.Types.Select(t => (Syntax: t, Type: model.GetTypeInfo(t.Type).Type)).ToList() ?? [];

        if (existing.Any(e => SymbolEqualityComparer.Default.Equals(e.Type?.OriginalDefinition, type.OriginalDefinition)
                || e.Syntax.Type.ToString().Equals(baseClass, StringComparison.OrdinalIgnoreCase)))
        {
            message = $"{kind} '{name}' already inherits from '{shown}'.";
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, self))
        {
            message = $"{kind} '{name}' cannot inherit from itself.";
            return false;
        }

        if (self != null && DerivesFrom(type, self))
        {
            message = $"'{shown}' already derives from '{name}', so '{name}' cannot inherit from it: that would be a cycle.";
            return false;
        }

        // implements names interfaces only: a class there is a slip for inherits.
        if (interfacesOnly && type.TypeKind != TypeKind.Interface)
        {
            message = $"'{shown}' is a {Describe(type)}, not an interface; use '{name} inherits {typeSyntax}'.";
            return false;
        }

        // The edit is made in the text, at one place, so nothing else in the file moves: the whole
        // file used to be reformatted (NormalizeWhitespace), which took out blank lines, moved
        // #region to column 0 and split one-line statements.
        var written = typeSyntax.ToString();
        var afterName = node.ParameterList?.Span.End ?? node.TypeParameterList?.Span.End ?? node.Identifier.Span.End;

        if (type.TypeKind == TypeKind.Interface)
        {
            // After everything already there: a base class must stay first.
            (insertAt, insert) = node.BaseList is null
                ? (afterName, " : " + written)
                : (node.BaseList.Types.Last().Span.End, ", " + written);
            message = node is InterfaceDeclarationSyntax
                ? $"Interface '{name}' now extends interface '{shown}'."
                : $"{kind} '{name}' now implements interface '{shown}'.";
            return true;
        }

        if (node is InterfaceDeclarationSyntax)
        {
            message = $"'{shown}' is a {Describe(type)}, and an interface can only extend interfaces.";
            return false;
        }

        if (IsStruct(node))
        {
            message = $"'{shown}' is a {Describe(type)}, and a struct can only implement interfaces.";
            return false;
        }

        if (type.TypeKind != TypeKind.Class)
        {
            message = $"'{shown}' is a {Describe(type)}, so a class cannot inherit from it.";
            return false;
        }

        // CS8864 and CS8865: a record and a class cannot inherit from each other.
        if (node is RecordDeclarationSyntax && !type.IsRecord)
        {
            message = $"'{shown}' is not a record, and a record can inherit only from a record.";
            return false;
        }

        if (node is ClassDeclarationSyntax && type.IsRecord)
        {
            message = $"'{shown}' is a record, and only a record can inherit from a record.";
            return false;
        }

        if (type.IsSealed || type.IsStatic)
        {
            message = $"'{shown}' is {(type.IsStatic ? "static" : "sealed")}, so no class can inherit from it.";
            return false;
        }

        if (existing.FirstOrDefault(e => e.Type is { TypeKind: TypeKind.Class }) is { Type: { } current })
        {
            message = $"{kind} '{name}' already inherits from '{current.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}', "
                + $"and a {kind.ToLowerInvariant()} can have only one base class. '{shown}' was not added.";
            return false;
        }

        // A base class goes first: class Order : IAuditable, AuditableBase is CS1722.
        (insertAt, insert) = node.BaseList is null
            ? (afterName, " : " + written)
            : (node.BaseList.Types[0].SpanStart, written + ", ");
        message = $"{kind} '{name}' now inherits from '{shown}'.";
        return true;
    }

    private static (CSharpCompilation, SyntaxTree, TypeDeclarationSyntax) Reparse(
        CSharpCompilation compilation, SyntaxTree tree, string text, int start)
    {
        var edited = CSharpSyntaxTree.ParseText(text, (CSharpParseOptions)tree.Options, tree.FilePath);
        var node = edited.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>().First(t => t.SpanStart == start);
        return (compilation.ReplaceSyntaxTree(tree, edited), edited, node);
    }

    private static string KindOf(TypeDeclarationSyntax node) => node switch
    {
        InterfaceDeclarationSyntax => "Interface",
        StructDeclarationSyntax => "Struct",
        RecordDeclarationSyntax r when r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) => "Record struct",
        RecordDeclarationSyntax => "Record",
        _ => "Class",
    };

    private static bool IsStruct(TypeDeclarationSyntax node) =>
        node is StructDeclarationSyntax
        || node is RecordDeclarationSyntax r && r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword);

    // record Order(int Id); has no braces.
    private static bool HasBody(TypeDeclarationSyntax node) =>
        node.OpenBraceToken.IsKind(SyntaxKind.OpenBraceToken) && !node.OpenBraceToken.IsMissing;

    // Each stub on its own line before the type's closing brace, indented like the type's first
    // member (the type's indent and four spaces when it has none on a line of its own).
    // Also gives back where each stub's code is in the new text.
    private static (string Text, IReadOnlyList<Microsoft.CodeAnalysis.Text.TextSpan> Spans) WithStubs(
        string text, TypeDeclarationSyntax node, IReadOnlyList<string> stubs, string newline)
    {
        var declared = node.Modifiers.Count > 0 ? node.Modifiers[0].SpanStart : node.Keyword.SpanStart;
        var typeIndent = IndentOf(text, declared);
        var first = node.Members.FirstOrDefault()?.GetFirstToken().SpanStart;
        var memberIndent = first is { } m && FirstOnLine(text, m) ? IndentOf(text, m) : typeIndent + "    ";
        var block = string.Concat(stubs.Select(s => memberIndent + s + newline));

        string before, after;
        if (!HasBody(node))
        {
            var semicolon = node.SemicolonToken.SpanStart;
            (before, after) = (text[..SpacesBefore(text, semicolon)] + " {" + newline, typeIndent + "}" + text[(semicolon + 1)..]);
        }
        else
        {
            var close = node.CloseBraceToken.SpanStart;
            (before, after) = FirstOnLine(text, close)
                ? (text[..LineStart(text, close)], text[LineStart(text, close)..])
                : (text[..SpacesBefore(text, close)] + newline, typeIndent + text[close..]);
        }

        var spans = new List<Microsoft.CodeAnalysis.Text.TextSpan>();
        var at = before.Length;
        foreach (var stub in stubs)
        {
            spans.Add(new Microsoft.CodeAnalysis.Text.TextSpan(at + memberIndent.Length, stub.Length));
            at += memberIndent.Length + stub.Length + newline.Length;
        }

        return (before + block + after, spans);
    }

    // The first stub whose name the type already declares (in any of its parts) for a member it
    // does not replace: a property, field or event of that name, or a method with the same
    // parameter types. A method with other parameter types is an overload and fine.
    private static string? Clash(
        CSharpCompilation compilation, SyntaxTree tree, TypeDeclarationSyntax node,
        IReadOnlyList<MemberStub> stubs, IReadOnlyList<Microsoft.CodeAnalysis.Text.TextSpan> spans)
    {
        if (compilation.GetSemanticModel(tree).GetDeclaredSymbol(node) is not INamedTypeSymbol type)
            return null;

        bool InStub(ISymbol s, int i) =>
            s.DeclaringSyntaxReferences.Any(r => r.SyntaxTree == tree && spans[i].Contains(r.Span));

        bool InAnyStub(ISymbol s) => Enumerable.Range(0, spans.Count).Any(i => InStub(s, i));

        for (var i = 0; i < stubs.Count; i++)
        {
            var members = type.GetMembers(stubs[i].MemberName);
            var stub = members.FirstOrDefault(s => InStub(s, i));
            if (stub == null)
                continue;

            var other = members.FirstOrDefault(s => !InAnyStub(s) && s switch
            {
                IMethodSymbol m when stub is IMethodSymbol sm => SameParameters(m, sm),
                IMethodSymbol { MethodKind: not MethodKind.Ordinary } => false,
                _ => true,
            });
            if (other == null)
                continue;

            return $"'{type.Name}' already has a member '{other.Name}' that does not match "
                + $"{stubs[i].From}.{stubs[i].MemberName} ({Signature(stub)}); change or remove it, then run the command again.";
        }

        return null;
    }

    private static bool SameParameters(IMethodSymbol a, IMethodSymbol b) =>
        a.Parameters.Length == b.Parameters.Length
        && a.TypeParameters.Length == b.TypeParameters.Length
        && a.Parameters.Zip(b.Parameters).All(p => p.First.RefKind == p.Second.RefKind
            && p.First.Type.ToDisplayString() == p.Second.Type.ToDisplayString());

    private static int LineStart(string text, int position) =>
        position == 0 ? 0 : text.LastIndexOf('\n', position - 1) + 1;

    private static string IndentOf(string text, int position)
    {
        var start = LineStart(text, position);
        var end = start;
        while (end < text.Length && text[end] is ' ' or '\t')
            end++;
        return text[start..end];
    }

    private static bool FirstOnLine(string text, int position) =>
        text[LineStart(text, position)..position].All(c => c is ' ' or '\t');

    private static int SpacesBefore(string text, int position)
    {
        while (position > 0 && text[position - 1] is ' ' or '\t')
            position--;
        return position;
    }

    // What the type still lacks after the stubs: a member MemberStubs cannot write (an indexer, a
    // by-ref return). The edit compiles otherwise, so this is the whole of what is left to do.
    private static string Missing(CSharpCompilation compilation, SyntaxTree tree, TypeDeclarationSyntax node)
    {
        var name = node.Identifier.Text;
        if (node is InterfaceDeclarationSyntax
            || compilation.GetSemanticModel(tree).GetDeclaredSymbol(node) is not INamedTypeSymbol { IsAbstract: false } symbol)
            return string.Empty;

        var missing = new List<string>();
        foreach (var iface in symbol.AllInterfaces)
            foreach (var member in iface.GetMembers().Where(m => m.IsAbstract && m is IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol or IEventSymbol))
                if (symbol.FindImplementationForInterfaceMember(member) == null)
                    missing.Add(Signature(member));

        for (var b = symbol.BaseType; b is { IsAbstract: true }; b = b.BaseType)
            foreach (var member in b.GetMembers().Where(m => m.IsAbstract))
                if (!Overridden(symbol, member))
                    missing.Add(Signature(member));

        if (missing.Count == 0)
            return string.Empty;

        var list = missing.Distinct().ToList();
        return $" '{name}' does not implement {string.Join(", ", list.Take(5))}{(list.Count > 5 ? $" and {list.Count - 5} more" : "")} yet; "
            + "add them (add method / add property) or the project will not compile.";
    }

    private static bool Overridden(INamedTypeSymbol type, ISymbol member)
    {
        for (var t = type; t != null && !SymbolEqualityComparer.Default.Equals(t, member.ContainingType); t = t.BaseType)
            if (t.GetMembers(member.Name).Any(m => m.IsOverride))
                return true;
        return false;
    }

    private static string Signature(ISymbol member) => member switch
    {
        IMethodSymbol m => $"{m.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {m.Name}("
            + string.Join(", ", m.Parameters.Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))) + ")",
        IPropertySymbol p => $"{p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {p.Name}",
        _ => member.Name,
    };

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol ancestor)
    {
        for (var b = type.BaseType; b != null; b = b.BaseType)
            if (SymbolEqualityComparer.Default.Equals(b.OriginalDefinition, ancestor))
                return true;
        return type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, ancestor));
    }

    private static string Describe(INamedTypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Struct => "struct",
        TypeKind.Enum => "enum",
        TypeKind.Delegate => "delegate",
        TypeKind.Class => "class",
        _ => type.TypeKind.ToString().ToLowerInvariant(),
    };
}
