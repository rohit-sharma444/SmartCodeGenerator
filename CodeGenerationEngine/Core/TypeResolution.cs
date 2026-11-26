using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Whether a type name, written into a given class file, would name a type that exists.
/// </summary>
/// <remarks>
/// <para>
/// Parsing is not enough: <c>integer</c> parses as a type name exactly as <c>Author</c> does, and
/// was written into the file as <c>public integer Qty</c>, which does not compile. So the name is
/// bound for real: a one-field probe class goes into a compilation with the project's own files
/// and the runtime's assemblies, inside the class's namespace and under its file's usings (plus
/// the SDK's implicit usings, when the project turns them on), and any error the binder reports
/// on that field is the refusal.
/// </para>
/// <para>
/// Only the project the class belongs to is compiled — the folder of the nearest <c>.csproj</c>
/// above it, or the workspace when there is none — and no NuGet package is referenced, so a type
/// that lives only in a package is refused; the message says to type it by hand.
/// </para>
/// </remarks>
internal static class TypeResolution
{
    // What `<ImplicitUsings>enable</ImplicitUsings>` adds to a plain SDK project.
    internal static readonly string[] ImplicitUsings =
    [
        "System", "System.Collections.Generic", "System.IO", "System.Linq",
        "System.Net.Http", "System.Threading", "System.Threading.Tasks",
    ];

    // Words people type for a type that is spelt differently in C#.
    private static readonly Dictionary<string, string> Meant = new(StringComparer.OrdinalIgnoreCase)
    {
        ["integer"] = "int", ["int32"] = "int", ["int64"] = "long", ["boolean"] = "bool",
        ["str"] = "string", ["text"] = "string", ["number"] = "decimal", ["money"] = "decimal",
        ["currency"] = "decimal", ["date"] = "DateTime", ["datetime"] = "DateTime",
        ["time"] = "TimeSpan", ["timespan"] = "TimeSpan", ["uuid"] = "Guid", ["guid"] = "Guid",
        ["single"] = "float", ["real"] = "double",
    };

    private static readonly string[] Keywords =
    [
        "bool", "byte", "sbyte", "char", "decimal", "double", "float", "int", "uint", "long",
        "ulong", "short", "ushort", "string", "object", "nint", "nuint",
    ];

    /// <summary>
    /// True when <paramref name="type"/> binds in <paramref name="classFile"/>; otherwise
    /// <paramref name="reason"/> says why, with a suggestion when the name is a known misspelling.
    /// </summary>
    public static bool Resolves(
        string workspaceRoot, string classFile, TypeDeclarationSyntax classNode, TypeSyntax type,
        out string reason)
        => Resolves(workspaceRoot, classFile, classNode, type, out reason, out _);

    /// <summary>As <see cref="Resolves(string, string, TypeDeclarationSyntax, TypeSyntax, out string)"/>,
    /// and gives what <paramref name="type"/> binds to; null for a keyword type, which is not bound.</summary>
    public static bool Resolves(
        string workspaceRoot, string classFile, TypeDeclarationSyntax classNode, TypeSyntax type,
        out string reason, out ITypeSymbol? bound)
    {
        var result = ResolveAll(workspaceRoot, classFile, classNode, [type])[0];
        reason = result.Reason;
        bound = result.Bound;
        return result.Resolves;
    }

    /// <summary>What binding one type gave: whether it binds, why not, and what it binds to (null
    /// for a keyword type, which is not bound).</summary>
    public readonly record struct Resolution(bool Resolves, string Reason, ITypeSymbol? Bound);

    /// <summary>
    /// <see cref="Resolves(string, string, TypeDeclarationSyntax, TypeSyntax, out string, out ITypeSymbol?)"/>
    /// for several types at once, in their order: the project is read and compiled once, with one
    /// probe field for each type, rather than once for each type.
    /// </summary>
    public static IReadOnlyList<Resolution> ResolveAll(
        string workspaceRoot, string classFile, TypeDeclarationSyntax classNode, IReadOnlyList<TypeSyntax> types)
    {
        var results = new Resolution[types.Count];

        // A keyword never needs binding, and this keeps the common case free of a compilation.
        var probed = new List<int>();
        for (var i = 0; i < types.Count; i++)
        {
            if (types[i] is PredefinedTypeSyntax
                || types[i] is NullableTypeSyntax { ElementType: PredefinedTypeSyntax }
                || types[i] is ArrayTypeSyntax { ElementType: PredefinedTypeSyntax })
                results[i] = new Resolution(true, string.Empty, null);
            else
                probed.Add(i);
        }

        if (probed.Count == 0)
            return results;

        var csprojFolder = ProjectFolderOf(workspaceRoot, classFile);
        var projectFolder = csprojFolder ?? workspaceRoot;
        var classTree = classNode.SyntaxTree;
        var unit = (CompilationUnitSyntax)classTree.GetRoot();

        var usings = unit.Usings.Select(u => u.ToString()).ToList();
        if (ImplicitUsingsOn(csprojFolder))
            usings.AddRange(ImplicitUsings.Select(u => $"using {u};"));

        var ns = classNode.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse().Select(n => n.Name.ToString()).ToList();
        var nsName = ns.Count == 0 ? null : string.Join(".", ns);

        var fields = string.Concat(probed.Select(i => $" public {types[i]} __Probe{i} = default!;"));
        var body = $"file class __TypeProbe {{{fields} }}";
        var probeSource = string.Join(Environment.NewLine, usings.Distinct()) + Environment.NewLine
            + (nsName == null ? body : $"namespace {nsName} {{ {body} }}");

        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        var probe = CSharpSyntaxTree.ParseText(probeSource, parse);

        var trees = new List<SyntaxTree> { probe };
        foreach (var file in ClassFiles.Under(projectFolder))
        {
            try
            {
                var text = string.Equals(Path.GetFullPath(file), Path.GetFullPath(classFile), StringComparison.OrdinalIgnoreCase)
                    ? classTree.GetText().ToString()
                    : File.ReadAllText(file);
                trees.Add(CSharpSyntaxTree.ParseText(text, parse, path: file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable neighbour costs only the types it declares.
            }
        }

        var compilation = CSharpCompilation.Create(
            "TypeProbe", trees, Compiler.FrameworkReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var model = compilation.GetSemanticModel(probe);

        var declarations = probe.GetRoot().DescendantNodes().OfType<FieldDeclarationSyntax>().ToList();
        for (var k = 0; k < probed.Count; k++)
        {
            var i = probed[k];
            var probeType = declarations[k].Declaration.Type;
            var errors = model.GetDiagnostics(probeType.Span)
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();

            results[i] = errors.Count == 0
                ? new Resolution(true, string.Empty, model.GetTypeInfo(probeType).Type)
                : new Resolution(false, Refusal(types[i], trees, errors[0]), null);
        }

        return results;
    }

    /// <summary>Why <paramref name="type"/> did not bind, with a suggestion when it is a known
    /// misspelling or a project type in another case.</summary>
    private static string Refusal(TypeSyntax type, List<SyntaxTree> trees, Diagnostic error)
    {
        var written = type.ToString();

        // A project type typed in another case - customer for Customer - is suggested as datetime
        // is: the suggestion is shown, not applied, as for every other near miss.
        var bare = written.TrimEnd('?');
        var projectType = trees.Skip(1)
            .SelectMany(t => t.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            .Select(t => t.Identifier.Text)
            .FirstOrDefault(n => n.Equals(bare, StringComparison.OrdinalIgnoreCase) && n != bare);

        return Meant.TryGetValue(written, out var meant)
            ? $"'{written}' is not a C# type. Did you mean {meant}?"
            : projectType != null
                ? $"'{written}' is not a type this project knows. Did you mean {projectType}{written[bare.Length..]}?"
            : Keywords.FirstOrDefault(k => k.Equals(written, StringComparison.OrdinalIgnoreCase)) is { } keyword
                ? $"'{written}' is not a C# type. Did you mean {keyword}?"
                : $"'{written}' is not a type this project knows ({error.GetMessage()}). "
                    + "Use a C# type, a .NET type such as DateTime or List<string>, or a class in this project; "
                    + "a type from a package has to be typed into the file by hand.";
    }

    /// <summary>
    /// The errors the compiler reports inside <paramref name="span"/> of <paramref name="classRoot"/>,
    /// compiled as <paramref name="classFile"/> beside the rest of its project (as
    /// <see cref="Resolves"/> compiles it). For checking that an edited member still compiles.
    /// </summary>
    public static IReadOnlyList<Diagnostic> ErrorsWithin(
        string workspaceRoot, string classFile, SyntaxNode classRoot, Microsoft.CodeAnalysis.Text.TextSpan span)
    {
        var parse = new CSharpParseOptions(LanguageVersion.Latest);

        // Parsed again from its own text, so the spans are the same and the options match the rest.
        var edited = CSharpSyntaxTree.ParseText(classRoot.ToFullString(), parse, path: classFile);

        var trees = new List<SyntaxTree> { edited };
        var csprojFolder = ProjectFolderOf(workspaceRoot, classFile);
        var projectFolder = csprojFolder ?? workspaceRoot;
        if (ImplicitUsingsOn(csprojFolder))
            trees.Add(CSharpSyntaxTree.ParseText(
                string.Concat(ImplicitUsings.Select(u => $"global using {u};{Environment.NewLine}")), parse));
        foreach (var file in ClassFiles.Under(projectFolder))
        {
            if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(classFile), StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file), parse, path: file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable neighbour costs only the types it declares.
            }
        }

        var compilation = CSharpCompilation.Create(
            "EditProbe", trees, Compiler.FrameworkReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return [.. compilation.GetSemanticModel(edited).GetDiagnostics(span)
            .Where(d => d.Severity == DiagnosticSeverity.Error)];
    }

    /// <summary>The folder of the nearest .csproj at or above the file and within the workspace, or null.</summary>
    internal static string? ProjectFolderOf(string workspaceRoot, string file)
    {
        var top = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar);
        for (var dir = Path.GetDirectoryName(Path.GetFullPath(file));
             dir != null && dir.StartsWith(top, StringComparison.OrdinalIgnoreCase);
             dir = Path.GetDirectoryName(dir))
        {
            try
            {
                if (Directory.EnumerateFiles(dir, "*.csproj").Any())
                    return dir;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    // No project file means nothing says otherwise, so the generous answer: the project files this
    // tool writes all turn them on.
    internal static bool ImplicitUsingsOn(string? folder)
    {
        if (folder == null)
            return true;

        try
        {
            // A folder with no project file is the same as no folder (it threw "Sequence contains
            // no elements" when DefaultValues was given one).
            var project = Directory.EnumerateFiles(folder, "*.csproj").FirstOrDefault();
            if (project is null)
                return true;

            var value = XDocument.Load(project).Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "ImplicitUsings")?.Value.Trim();
            return string.Equals(value, "enable", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return true;
        }
    }
}
