using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Renames a class, interface, record, struct or enum, and the code that names it.
/// </summary>
/// <remarks>
/// <para>
/// It used to replace <c>class Old</c> in one file and move that file, and said "renamed": the
/// constructor kept the old name (CS1520), the <c>&lt;Old&gt;.Validation.cs</c> partial declared a
/// second class, every other file that used the type stopped compiling (CS0246), and an interface
/// was not renamed at all, the pattern only knowing the word <c>class</c>.
/// </para>
/// <para>
/// The references are found by binding, not by text: the class's project is compiled (its own
/// files and the runtime's assemblies, as <see cref="TypeResolution"/> compiles it) and an
/// identifier is renamed only when it binds to this type, its constructor or an alias of it. Text
/// matching cannot tell <c>public Member Member { get; set; }</c> - a type and a property of one
/// name, which every generated navigation property is - from two uses of the type, nor
/// <c>loan.Member</c> from <c>new Member()</c>. A project whose <c>.csproj</c> references the
/// class's project (a test project) is compiled against it and renamed the same way. What cannot
/// be bound - a file outside those projects, a <c>.razor</c> or <c>.xaml</c> page, a string - is
/// named in a warning, not edited.
/// </para>
/// </remarks>
public static class ClassRenameService
{
    private const int MaxShown = 5;

    public static bool RenameClass(string workspaceRoot, string oldClassName, string newClassName, out string message) =>
        RenameClass(workspaceRoot, oldClassName, newClassName, out message, out _);

    public static bool RenameClass(
        string workspaceRoot, string oldClassName, string newClassName,
        out string message, out IReadOnlyList<string> warnings)
    {
        message = string.Empty;
        warnings = [];

        // As create class names one: a capital first letter, the rest as typed.
        if (!string.IsNullOrEmpty(newClassName) && char.IsLower(newClassName[0]))
            newClassName = char.ToUpperInvariant(newClassName[0]) + newClassName[1..];

        if (MemberNames.NotAnIdentifier(newClassName, "class") is { } bad)
        {
            message = bad;
            return false;
        }

        try
        {
            var file = ClassFiles.FileOf(workspaceRoot, oldClassName);
            if (file == null)
            {
                message = $"Class '{oldClassName}' not found.";
                return false;
            }

            var declaration = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot()
                .DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
                .FirstOrDefault(t => t.Identifier.Text.Equals(oldClassName, StringComparison.OrdinalIgnoreCase));

            if (declaration == null)
            {
                message = $"Class '{oldClassName}' not found in {Path.GetFileName(file)}.";
                return false;
            }

            var oldName = declaration.Identifier.Text;
            var kind = KindOf(declaration);

            if (oldName.Equals(newClassName, StringComparison.Ordinal))
            {
                message = $"{Capital(kind)} '{oldName}' is already called {newClassName}; nothing was changed.";
                return true;
            }

            // The move below overwrote, so renaming onto a class that exists threw that class
            // away; one in another folder would become a second class of the same name. Only the
            // file being renamed may hold the new name (a rename that changes only the case).
            var taken = ClassFiles.Under(workspaceRoot).FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Equals(newClassName, StringComparison.OrdinalIgnoreCase)
                && !Path.GetFullPath(f).Equals(Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase));

            if (taken != null)
            {
                message = $"Class '{newClassName}' already exists: {taken}. '{oldName}' was not renamed.";
                return false;
            }

            var plan = Plan(workspaceRoot, file, declaration, oldName, newClassName);
            if (plan.Error != null)
            {
                message = plan.Error;
                return false;
            }

            // Every new file name is checked before anything is written, so a refusal leaves no
            // file half-renamed.
            var moves = new List<(string From, string To)>();
            foreach (var part in plan.DeclaringFiles)
            {
                var stem = Path.GetFileNameWithoutExtension(part);
                string? newStem = stem.Equals(oldName, StringComparison.OrdinalIgnoreCase) ? newClassName
                    : stem.StartsWith(oldName + ".", StringComparison.OrdinalIgnoreCase) ? newClassName + stem[oldName.Length..]
                    : null;
                if (newStem == null)
                    continue;

                var to = Path.Combine(Path.GetDirectoryName(part)!, newStem + ".cs");
                if (File.Exists(to) && !Path.GetFullPath(to).Equals(Path.GetFullPath(part), StringComparison.OrdinalIgnoreCase))
                {
                    message = $"'{to}' already exists, so '{oldName}' was not renamed.";
                    return false;
                }

                moves.Add((part, to));
            }

            foreach (var (path, text) in plan.Edits)
                File.WriteAllText(path, text);

            foreach (var (from, to) in moves)
                File.Move(from, to);

            ValidationRegistry.RenameClass(oldName, newClassName);

            var edited = plan.Edits.Select(e => e.Path).Where(p => !plan.DeclaringFiles.Contains(p)).ToList();
            message = $"{Capital(kind)} '{oldName}' renamed to '{newClassName}'."
                + (moves.Count > 0
                    ? $" File{(moves.Count == 1 ? "" : "s")} renamed: {string.Join(", ", moves.Select(m => $"{Path.GetFileName(m.From)} -> {Path.GetFileName(m.To)}"))}."
                    : string.Empty)
                + (edited.Count > 0
                    ? $" References updated in {edited.Count} other file{(edited.Count == 1 ? "" : "s")}: {Shown(edited.Select(Path.GetFileName)!)}."
                    : string.Empty);

            warnings = Warnings(workspaceRoot, plan, oldName, newClassName);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = ex.Message;
            return false;
        }
    }

    private sealed record RenamePlan(
        List<(string Path, string Text)> Edits,
        HashSet<string> DeclaringFiles,
        HashSet<string> Covered,
        List<string> StringUses,
        string? Error = null);

    // Binds every identifier in the class's project, and in the projects referencing it, and
    // collects the new text of each file that changes. Nothing is written here.
    private static RenamePlan Plan(string workspaceRoot, string file, BaseTypeDeclarationSyntax declaration, string oldName, string newName)
    {
        var edits = new List<(string, string)>();
        var declaring = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var strings = new List<string>();

        var csprojFolder = TypeResolution.ProjectFolderOf(workspaceRoot, file);
        var projectFolder = csprojFolder ?? workspaceRoot;

        var compilation = Compile("RenameProbe", projectFolder, csprojFolder, [], out var trees);
        var tree = trees.FirstOrDefault(t => string.Equals(Path.GetFullPath(t.FilePath), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase));
        if (tree == null)
            return new RenamePlan(edits, declaring, covered, strings, $"Could not read {file}.");

        var node = tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.Text == oldName && t.Span == declaration.Span)
            ?? tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault(t => t.Identifier.Text == oldName);
        var symbol = node == null ? null : compilation.GetSemanticModel(tree).GetDeclaredSymbol(node);
        var id = symbol?.GetDocumentationCommentId();
        if (id == null)
            return new RenamePlan(edits, declaring, covered, strings, $"Could not read the declaration of '{oldName}' in {file}.");

        Collect(compilation, trees, id, oldName, newName, edits, declaring, covered, strings);

        // A project that references this one (its tests) names the type too.
        if (csprojFolder != null)
        {
            var reference = compilation.ToMetadataReference();
            foreach (var other in ReferencingProjects(workspaceRoot, csprojFolder))
            {
                var otherCompilation = Compile("RenameProbeRef", other, other, [reference], out var otherTrees);
                Collect(otherCompilation, otherTrees, id, oldName, newName, edits, declaring, covered, strings);
            }
        }

        return new RenamePlan(edits, declaring, covered, strings);
    }

    private static void Collect(
        CSharpCompilation compilation, IReadOnlyList<SyntaxTree> trees, string id, string oldName, string newName,
        List<(string, string)> edits, HashSet<string> declaring, HashSet<string> covered, List<string> strings)
    {
        foreach (var tree in trees)
        {
            if (string.IsNullOrEmpty(tree.FilePath))
                continue;

            covered.Add(Path.GetFullPath(tree.FilePath));

            var text = tree.GetText();
            if (!text.ToString().Contains(oldName, StringComparison.Ordinal))
                continue;

            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var spans = new List<TextSpan>();

            foreach (var n in root.DescendantNodes(descendIntoTrivia: true))
            {
                switch (n)
                {
                    case BaseTypeDeclarationSyntax t when t.Identifier.Text == oldName
                        && model.GetDeclaredSymbol(t)?.GetDocumentationCommentId() == id:
                        spans.Add(t.Identifier.Span);
                        declaring.Add(Path.GetFullPath(tree.FilePath));
                        break;

                    case ConstructorDeclarationSyntax c when c.Identifier.Text == oldName
                        && model.GetDeclaredSymbol(c)?.ContainingType.GetDocumentationCommentId() == id:
                        spans.Add(c.Identifier.Span);
                        break;

                    case DestructorDeclarationSyntax d when d.Identifier.Text == oldName
                        && model.GetDeclaredSymbol(d)?.ContainingType.GetDocumentationCommentId() == id:
                        spans.Add(d.Identifier.Span);
                        break;

                    case SimpleNameSyntax s when s.Identifier.Text == oldName && Names(model, s, id):
                        spans.Add(s.Identifier.Span);
                        break;

                    case LiteralExpressionSyntax l when l.IsKind(SyntaxKind.StringLiteralExpression)
                        && l.Token.ValueText == oldName:
                        var where = Path.GetFileName(tree.FilePath);
                        if (!strings.Contains(where))
                            strings.Add(where);
                        break;
                }
            }

            if (spans.Count == 0)
                continue;

            edits.Add((Path.GetFullPath(tree.FilePath),
                text.WithChanges(spans.Distinct().Select(s => new TextChange(s, newName))).ToString()));
        }
    }

    private static bool Names(SemanticModel model, SimpleNameSyntax name, string id)
    {
        var info = model.GetSymbolInfo(name);
        var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
        if (symbol == null)
        {
            // A using alias's own name, `using M = App.Member;`, binds through the alias.
            symbol = model.GetAliasInfo(name)?.Target;
        }

        symbol = symbol switch
        {
            IAliasSymbol alias => alias.Target,
            IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.Destructor } m => m.ContainingType,
            _ => symbol,
        };

        return symbol is INamedTypeSymbol type
            && type.OriginalDefinition.GetDocumentationCommentId() == id;
    }

    internal static CSharpCompilation Compile(
        string name, string folder, string? csprojFolder, IEnumerable<MetadataReference> extra, out List<SyntaxTree> trees)
    {
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        trees = [];

        foreach (var f in ClassFiles.Under(folder))
        {
            try
            {
                trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(f), parse, path: Path.GetFullPath(f)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable file costs only the references in it.
            }
        }

        var all = new List<SyntaxTree>(trees);
        if (TypeResolution.ImplicitUsingsOn(csprojFolder))
            all.Add(CSharpSyntaxTree.ParseText(
                "global using System; global using System.Collections.Generic; global using System.IO; "
                + "global using System.Linq; global using System.Net.Http; global using System.Threading; "
                + "global using System.Threading.Tasks;", parse));

        return CSharpCompilation.Create(
            name, all, Compiler.FrameworkReferences.Concat(extra),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    // The folders of the other projects in the workspace whose .csproj has a ProjectReference to
    // the project in csprojFolder.
    private static IEnumerable<string> ReferencingProjects(string workspaceRoot, string csprojFolder)
    {
        var target = Directory.EnumerateFiles(csprojFolder, "*.csproj").Select(Path.GetFullPath).ToList();

        foreach (var project in Directory.EnumerateFiles(workspaceRoot, "*.csproj", SearchOption.AllDirectories))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(project))!;
            if (dir.Equals(Path.GetFullPath(csprojFolder), StringComparison.OrdinalIgnoreCase)
                || dir.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || dir.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            bool references;
            try
            {
                references = XDocument.Load(project).Descendants()
                    .Where(e => e.Name.LocalName == "ProjectReference")
                    .Select(e => e.Attribute("Include")?.Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Any(v => target.Contains(Path.GetFullPath(Path.Combine(dir, v!.Replace('\\', Path.DirectorySeparatorChar))),
                        StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                continue;
            }

            if (references)
                yield return dir;
        }
    }

    private static IReadOnlyList<string> Warnings(string workspaceRoot, RenamePlan plan, string oldName, string newName)
    {
        var warnings = new List<string>();
        var word = new Regex($@"\b{Regex.Escape(oldName)}\b");

        // Code the compilations did not cover: another project that does not reference this one,
        // or a page whose markup names the type.
        var uncovered = new List<string>();
        foreach (var f in Directory.EnumerateFiles(workspaceRoot, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext is not (".cs" or ".razor" or ".cshtml" or ".xaml")
                || f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || f.Contains("DeletedClasses", StringComparison.OrdinalIgnoreCase)
                || plan.Covered.Contains(Path.GetFullPath(f)))
                continue;

            string text;
            try
            {
                text = File.ReadAllText(f);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var uses = ext == ".cs"
                ? CSharpSyntaxTree.ParseText(text).GetRoot().DescendantTokens()
                    .Any(t => t.IsKind(SyntaxKind.IdentifierToken) && t.Text == oldName)
                : word.IsMatch(text);
            if (uses)
                uncovered.Add(Path.GetRelativePath(workspaceRoot, f));
        }

        if (uncovered.Count > 0)
            warnings.Add($"These files may still use '{oldName}' and were not changed: {Shown(uncovered)}. "
                + $"Rename it to '{newName}' there, or that code will not compile.");

        if (plan.StringUses.Count > 0)
            warnings.Add($"Text \"{oldName}\" in {Shown(plan.StringUses)} was left as it is: strings are not renamed.");

        return warnings;
    }

    private static string Shown(IEnumerable<string> names)
    {
        var list = names.ToList();
        return string.Join(", ", list.Take(MaxShown))
            + (list.Count > MaxShown ? $" and {list.Count - MaxShown} more" : string.Empty);
    }

    private static string KindOf(BaseTypeDeclarationSyntax t) => t switch
    {
        InterfaceDeclarationSyntax => "interface",
        RecordDeclarationSyntax => "record",
        StructDeclarationSyntax => "struct",
        EnumDeclarationSyntax => "enum",
        _ => "class",
    };

    private static string Capital(string word) => char.ToUpperInvariant(word[0]) + word[1..];
}
