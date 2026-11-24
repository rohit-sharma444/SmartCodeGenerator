using CodeGenerationEngine.Helper;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SemanticModel = Microsoft.CodeAnalysis.SemanticModel;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Checks a class <c>create class</c> generated beside the rest of its project, and adds the
/// members of the interfaces it implements that it does not have yet.
/// </summary>
/// <remarks>
/// The class used to be compiled alone, so a project interface or base class was reported as
/// "CS0246 could not be found" — and the class was saved anyway, where <c>dotnet build</c> then
/// failed with CS0535 for every interface member it lacked. It is now compiled with the project's
/// own files (as <see cref="TypeResolution"/> binds a type), and each missing interface member is
/// added as a stub: a property as an auto-property, a method that throws
/// <see cref="NotImplementedException"/>, so the project builds and the gaps are named.
/// </remarks>
public static class NewClassCompletion
{
    public sealed record Result(
        string Code,
        string? ClassName,
        IReadOnlyList<string> Interfaces,
        IReadOnlyList<string> Added,
        IReadOnlyList<Diagnostic> Errors);

    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.MinimallyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>Completes and checks <paramref name="code"/> (unformatted, as generated) for <paramref name="target"/>.</summary>
    public static Result Complete(string code, ProjectLocation target)
    {
        var first = Compile(code, target);
        var interfaces = new List<string>();
        var added = new List<string>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var stubs = new List<string>();

        if (first.Class is { } decl
            && first.Model.GetDeclaredSymbol(decl) is INamedTypeSymbol { TypeKind: not TypeKind.Enum } type)
        {
            var position = decl.SpanStart;

            // A class that is not abstract must override every abstract member of its abstract bases.
            if (!type.IsAbstract)
            {
                for (var baseType = type.BaseType; baseType is { IsAbstract: true }; baseType = baseType.BaseType)
                {
                    var any = false;
                    foreach (var member in baseType.GetMembers())
                    {
                        if (member.IsStatic || !member.IsAbstract
                            || IsOverridden(type, member)
                            || keys.Contains(Key(member))
                            || Stub(member, first.Model, position, StubKind.Override) is not { } stub)
                            continue;

                        stubs.Add(stub);
                        added.Add(member.Name);
                        keys.Add(Key(member));
                        any = true;
                    }

                    if (any)
                        interfaces.Add(baseType.Name);
                }
            }

            var interfaceKind = type.IsAbstract ? StubKind.Abstract : StubKind.Implement;
            foreach (var iface in type.AllInterfaces)
            {
                var any = false;
                foreach (var member in iface.GetMembers())
                {
                    if (member.IsStatic || !member.IsAbstract
                        || type.FindImplementationForInterfaceMember(member) is not null
                        || keys.Contains(Key(member))
                        || Stub(member, first.Model, position, interfaceKind) is not { } stub)
                        continue;

                    stubs.Add(stub);
                    added.Add(member.Name);
                    keys.Add(Key(member));
                    any = true;
                }

                if (any)
                    interfaces.Add(iface.Name);
            }
        }

        if (stubs.Count == 0)
            return new Result(code, first.Class?.Identifier.Text, [], [], Errors(first));

        var close = code.LastIndexOf('}');
        var completed = code[..close].TrimEnd() + Environment.NewLine + Environment.NewLine
            + string.Concat(stubs.Select(s => "    " + s + Environment.NewLine))
            + code[close..];

        var second = Compile(completed, target);
        return new Result(completed, first.Class?.Identifier.Text, interfaces, added, Errors(second));
    }

    /// <summary>
    /// Says why a class checked with its project's files was not written: the errors are its own,
    /// as only its own file's diagnostics are kept.
    /// </summary>
    public static void ReportRefused(Result result)
    {
        Output.Error($"{result.ClassName ?? "The class"} was not created: it does not compile in its project.");
        foreach (var d in result.Errors)
            Console.WriteLine("  " + d);
        Output.Detail("  Create the types it names first, or correct the command, then run it again.");
    }

    /// <summary>Says what was added and whether the class compiles, as the old check did.</summary>
    public static void Report(Result result)
    {
        // Only once it has compiled: "Validate succeeded" printed just above "Compilation failed".
        if (result.Errors.Count == 0)
            Output.Success(" \n Validate succeeded. ");

        if (result.Added.Count > 0)
            Console.WriteLine(
                $"{result.ClassName} takes members from {string.Join(" and ", result.Interfaces)}: added "
                + $"{string.Join(", ", result.Added)}, which it lacked. "
                + "A method added this way throws NotImplementedException until you fill it in "
                + "(an abstract class leaves it abstract).");

        if (result.Errors.Count > 0)
        {
            // Saved, as it was checked alone: with no project, a type another file declares
            // looks missing here, and refusing would block a class that does build.
            Output.Warning("Saved, but it does not compile on its own. Diagnostics:");
            foreach (var d in result.Errors)
                Console.WriteLine("  " + d);
            return;
        }

        Output.Success(" \n Compiled. \n");
    }

    private sealed record Probe(CSharpCompilation Compilation, SyntaxTree Tree, SemanticModel Model, BaseTypeDeclarationSyntax? Class);

    private static Probe Compile(string code, ProjectLocation target)
    {
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        var formatted = CodeFormatter.Format(code, ProjectNamespace.For(target.ProjectFile));

        var className = (CSharpSyntaxTree.ParseText(code, parse).GetRoot()
            .DescendantNodes().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault())?.Identifier.Text;
        var path = Path.Combine(target.Directory ?? string.Empty, (className ?? "NewClass") + ".cs");
        var tree = CSharpSyntaxTree.ParseText(formatted, parse, path: Path.GetFileName(path));

        var trees = new List<SyntaxTree> { tree };

        // Only a real project's files: with none, the folder may be a whole workspace of unrelated
        // projects, and the class is checked alone as it always was.
        var csprojFolder = string.IsNullOrWhiteSpace(target.ProjectFile) ? null : Path.GetDirectoryName(target.ProjectFile);
        if (csprojFolder is not null)
        {
            var project = ClassRenameService.Compile("NewClassProbe", csprojFolder, csprojFolder, [], out _);
            trees.AddRange(project.SyntaxTrees.Where(t => !string.Equals(
                t.FilePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)));
        }

        var compilation = CSharpCompilation.Create(
            "NewClassProbe", trees, Compiler.FrameworkReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var decl = tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == className);
        return new Probe(compilation, tree, compilation.GetSemanticModel(tree), decl);
    }

    private static List<Diagnostic> Errors(Probe probe) =>
        [.. probe.Model.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)];

    // A method is told apart by its parameter types, so overloads each get a stub.
    private static string Key(ISymbol member) => member is IMethodSymbol m
        ? m.Name + "(" + string.Join(",", m.Parameters.Select(p => p.RefKind + p.Type.ToDisplayString())) + ")"
        : member.Name;

    private enum StubKind { Implement, Abstract, Override }

    // Whether a class already overrides an abstract member with a body, anywhere up to the member's owner.
    private static bool IsOverridden(INamedTypeSymbol type, ISymbol member)
    {
        for (var t = type; t is not null && !SymbolEqualityComparer.Default.Equals(t, member.ContainingType); t = t.BaseType)
            foreach (var m in t.GetMembers(member.Name))
            {
                if (m.IsAbstract || !m.IsOverride)
                    continue;
                var overridden = m switch
                {
                    IMethodSymbol ms => (ISymbol?)ms.OverriddenMethod,
                    IPropertySymbol ps => ps.OverriddenProperty,
                    IEventSymbol es => es.OverriddenEvent,
                    _ => null
                };
                if (overridden is not null
                    && SymbolEqualityComparer.Default.Equals(overridden.OriginalDefinition, member.OriginalDefinition))
                    return true;
            }

        return false;
    }

    private static string Access(Accessibility a) => a switch
    {
        Accessibility.Protected => "protected",
        Accessibility.Internal => "internal",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => "public"
    };

    // A public member that implements the interface's one implicitly (or an abstract one, or an
    // override of a base class's abstract member). Null for what has no plain stub (an indexer,
    // a by-ref return), which the compile then reports.
    private static string? Stub(ISymbol member, SemanticModel model, int position, StubKind kind = StubKind.Implement)
    {
        string Type(ITypeSymbol t) => t.ToMinimalDisplayString(model, position, TypeFormat);
        static string Name(string n) => SyntaxFacts.GetKeywordKind(n) != SyntaxKind.None ? "@" + n : n;
        var access = kind == StubKind.Override ? Access(member.DeclaredAccessibility) : "public";
        var modifier = kind switch { StubKind.Abstract => "abstract ", StubKind.Override => "override ", _ => "" };
        var body = kind == StubKind.Abstract ? ";" : " => throw new System.NotImplementedException();";

        switch (member)
        {
            case IPropertySymbol { IsIndexer: false, ReturnsByRef: false, ReturnsByRefReadonly: false } p:
                var accessors = kind == StubKind.Override
                    ? p.IsWriteOnly ? "{ set => throw new System.NotImplementedException(); }"
                        : p.IsReadOnly ? "{ get; }" : "{ get; set; }"
                    : "{ get; set; }";
                return $"{access} {modifier}{Type(p.Type)} {Name(p.Name)} {accessors}";

            case IMethodSymbol { MethodKind: MethodKind.Ordinary, ReturnsByRef: false, ReturnsByRefReadonly: false } m:
                var generics = m.TypeParameters.Length == 0 ? "" : $"<{string.Join(", ", m.TypeParameters.Select(t => t.Name))}>";
                var parameters = string.Join(", ", m.Parameters.Select(p =>
                    (p.IsParams ? "params " : "")
                    + (p.RefKind switch { RefKind.Ref => "ref ", RefKind.Out => "out ", RefKind.In => "in ", _ => "" })
                    + $"{Type(p.Type)} {Name(p.Name)}"));
                var returns = m.ReturnsVoid ? "void" : Type(m.ReturnType);
                return $"{access} {modifier}{returns} {Name(m.Name)}{generics}({parameters}){body}";

            case IEventSymbol e:
                return $"{access} {modifier}event {Type(e.Type)}? {Name(e.Name)};";

            default:
                return null;
        }
    }
}
