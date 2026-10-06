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
/// </remarks>
public static class ClassInheritanceService
{
    public static bool AddInheritance(
        string workspaceRoot,
        string className,
        string baseClass,
        out string message)
    {
        message = string.Empty;

        var file = ClassFiles.FileOf(workspaceRoot, className);
        if (file == null)
        {
            message = $"Class '{className}' not found.";
            return false;
        }

        try
        {
            return Add(workspaceRoot, file, className, baseClass.Trim(), out message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = ex.Message;
            return false;
        }
    }

    private static bool Add(string workspaceRoot, string file, string className, string baseClass, out string message)
    {
        var csprojFolder = TypeResolution.ProjectFolderOf(workspaceRoot, file);
        var compilation = ClassRenameService.Compile("InheritProbe", csprojFolder ?? workspaceRoot, csprojFolder, [], out var trees);

        var tree = trees.FirstOrDefault(t => string.Equals(t.FilePath, Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase));
        if (tree == null)
        {
            message = $"Could not read {file}.";
            return false;
        }

        var root = tree.GetRoot();

        // By name: a file may hold several types, and the base type belongs to the one the user
        // named, not to whichever is declared first.
        var node = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(c => c is ClassDeclarationSyntax or InterfaceDeclarationSyntax
                && c.Identifier.Text.Equals(className, StringComparison.OrdinalIgnoreCase));
        if (node == null)
        {
            message = $"Class '{className}' not found in {Path.GetFileName(file)}.";
            return false;
        }

        var name = node.Identifier.Text;
        var kind = node is InterfaceDeclarationSyntax ? "Interface" : "Class";
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

        BaseTypeSyntax added = SyntaxFactory.SimpleBaseType(typeSyntax);
        SeparatedSyntaxList<BaseTypeSyntax> types;
        string done;

        if (type.TypeKind == TypeKind.Interface)
        {
            // After everything already there: a base class must stay first.
            types = node.BaseList is null ? SyntaxFactory.SingletonSeparatedList(added) : node.BaseList.Types.Add(added);
            done = node is InterfaceDeclarationSyntax
                ? $"Interface '{name}' now extends interface '{shown}'."
                : $"Class '{name}' now implements interface '{shown}'.";
        }
        else if (node is InterfaceDeclarationSyntax)
        {
            message = $"'{shown}' is a {Describe(type)}, and an interface can only extend interfaces.";
            return false;
        }
        else if (type.TypeKind != TypeKind.Class)
        {
            message = $"'{shown}' is a {Describe(type)}, so a class cannot inherit from it.";
            return false;
        }
        else if (type.IsSealed || type.IsStatic)
        {
            message = $"'{shown}' is {(type.IsStatic ? "static" : "sealed")}, so no class can inherit from it.";
            return false;
        }
        else if (existing.FirstOrDefault(e => e.Type is { TypeKind: TypeKind.Class }) is { Type: { } current })
        {
            message = $"Class '{name}' already inherits from '{current.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}', "
                + $"and a class can have only one base class. '{shown}' was not added.";
            return false;
        }
        else
        {
            // A base class goes first: class Order : IAuditable, AuditableBase is CS1722.
            types = node.BaseList is null ? SyntaxFactory.SingletonSeparatedList(added) : node.BaseList.Types.Insert(0, added);
            done = $"Class '{name}' now inherits from '{shown}'.";
        }

        var newNode = node.WithBaseList(node.BaseList is null ? SyntaxFactory.BaseList(types) : node.BaseList.WithTypes(types));
        var newRoot = root.ReplaceNode(node, newNode).NormalizeWhitespace();

        File.WriteAllText(file, newRoot.ToFullString());

        message = done + Missing(compilation, tree, newRoot, name);
        return true;
    }

    // What the class still has to implement now: an interface's members, or a base class's
    // abstract ones. The edit compiles otherwise, so this is the whole of what is left to do.
    private static string Missing(CSharpCompilation compilation, SyntaxTree before, SyntaxNode newRoot, string name)
    {
        var after = before.WithRootAndOptions(newRoot, before.Options);
        var edited = compilation.ReplaceSyntaxTree(before, after);
        var node = after.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.Text == name);
        if (node is not ClassDeclarationSyntax || edited.GetSemanticModel(after).GetDeclaredSymbol(node) is not { IsAbstract: false } symbol)
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
