using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Where code in the workspace still names a member that was just removed or renamed.
/// </summary>
/// <remarks>
/// Syntax only, so it reads as "may still use": inside the class, any bare use of the name
/// (<c>Save()</c>, <c>this.Save()</c>, <c>Pages + 1</c>); anywhere else, a member access ending in
/// it (<c>order.Save()</c>), which another type's member of the same name also matches. Run after
/// the edit, so the member's own declaration is not among them.
/// </remarks>
public static class MemberReferences
{
    private const int MaxShown = 5;

    /// <summary>"Order.Use()"-style names of the members that still use <paramref name="name"/>.</summary>
    public static IReadOnlyList<string> UsersOf(
        string workspaceRoot, string className, string name, IReadOnlyCollection<string>? skipFiles = null)
    {
        var users = new List<string>();
        if (string.IsNullOrWhiteSpace(name))
            return users;

        var compilations = new Dictionary<string, (CSharpCompilation, List<SyntaxTree>)>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in ClassFiles.Under(workspaceRoot))
        {
            if (skipFiles?.Any(s => string.Equals(Path.GetFullPath(s), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase)) == true)
                continue;

            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (!text.Contains(name, StringComparison.OrdinalIgnoreCase))
                continue;

            var (tree, model) = Bound(workspaceRoot, file, compilations);
            var root = tree?.GetRoot() ?? CSharpSyntaxTree.ParseText(text).GetRoot();
            foreach (var id in root.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                // Case-insensitive like the commands' own lookup, so a name typed in another case still finds them.
                if (!id.Identifier.Text.Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;

                var owner = id.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                var inClass = owner?.Identifier.Text.Equals(className, StringComparison.OrdinalIgnoreCase) == true;

                var accessed = id.Parent is MemberAccessExpressionSyntax access && access.Name == id;

                // Inside the class any use counts; elsewhere only x.Name, since a bare name
                // there is that type's own member.
                if (!inClass && !accessed)
                    continue;

                // A name that binds to something other than this class's member is not a use of
                // it: EntityBase.DisplayName's GetType().Name was listed as using Customer.Name.
                // The removed or renamed member itself binds to nothing, so a real use stays.
                if (model?.GetSymbolInfo(id).Symbol is { } symbol && !MemberOf(symbol, className))
                    continue;

                var where = Describe(owner, id, file);
                if (!users.Contains(where))
                    users.Add(where);
            }
        }

        return users;
    }

    private static bool MemberOf(ISymbol symbol, string className) =>
        symbol.Kind is SymbolKind.Property or SymbolKind.Method or SymbolKind.Field or SymbolKind.Event
        && symbol.ContainingType?.Name.Equals(className, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// The file's tree in a compilation of its project, and its semantic model; both null when
    /// the file is not in it. One compilation per project, built on first need.
    /// </summary>
    private static (SyntaxTree? Tree, SemanticModel? Model) Bound(
        string workspaceRoot, string file, Dictionary<string, (CSharpCompilation, List<SyntaxTree>)> compilations)
    {
        var csprojFolder = TypeResolution.ProjectFolderOf(workspaceRoot, file);
        var folder = csprojFolder ?? workspaceRoot;

        if (!compilations.TryGetValue(folder, out var entry))
        {
            var compilation = ClassRenameService.Compile("ReferenceProbe", folder, csprojFolder, [], out var trees);
            entry = (compilation, trees);
            compilations[folder] = entry;
        }

        var tree = entry.Item2.FirstOrDefault(t =>
            string.Equals(t.FilePath, Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase));

        return tree is null ? (null, null) : (tree, entry.Item1.GetSemanticModel(tree));
    }

    /// <summary>The warning to print, or null when nothing uses it.</summary>
    public static string? Warning(
        string workspaceRoot, string className, string name, string what, string done,
        IReadOnlyCollection<string>? skipFiles = null)
    {
        var users = UsersOf(workspaceRoot, className, name, skipFiles);
        if (users.Count == 0)
            return null;

        var shown = string.Join(", ", users.Take(MaxShown))
            + (users.Count > MaxShown ? $" and {users.Count - MaxShown} more" : string.Empty);
        return $"{what} '{name}' was {done}, but code may still use it: {shown}. Update those or that code will not compile.";
    }

    private static string Describe(TypeDeclarationSyntax? owner, IdentifierNameSyntax id, string file)
    {
        var member = id.Ancestors().OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(m => m is not BaseTypeDeclarationSyntax);
        var memberName = member switch
        {
            MethodDeclarationSyntax m => m.Identifier.Text + "()",
            PropertyDeclarationSyntax p => p.Identifier.Text,
            ConstructorDeclarationSyntax => "constructor",
            FieldDeclarationSyntax f => f.Declaration.Variables.First().Identifier.Text,
            _ => null,
        };

        var type = owner?.Identifier.Text ?? Path.GetFileNameWithoutExtension(file);
        return memberName == null ? type : $"{type}.{memberName}";
    }
}
