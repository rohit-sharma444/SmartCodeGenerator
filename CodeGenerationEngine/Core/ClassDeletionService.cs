using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Deletes a class: moves its files into the project's <c>DeletedClasses</c> folder, where
/// <c>restore class</c> finds them again.
/// </summary>
/// <remarks>
/// A class is its <c>&lt;Name&gt;.cs</c> file and the other partial declarations of it in the same
/// project and namespace - <c>generate validator</c> writes <c>&lt;Name&gt;.Validation.cs</c>. Only the
/// main file used to be moved, which left the validator as a partial of a class that no longer
/// existed, shown by <c>list classes</c> as a class of its own. A file that declares anything else
/// as well is left where it is and named. The class's validation rules go with it, through
/// <see cref="ValidationRegistry.RemoveClass"/>.
/// </remarks>
public static class ClassDeletionService
{
    public static (bool Success, string Message) DeleteClass(string workspaceRoot, string className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return (false, "Class name not provided.");

        if (!Directory.Exists(workspaceRoot))
            return (false, "Workspace does not exist.");

        // One class, in the selected project: every file of the name in the workspace was moved,
        // so deleting Beta's Customer deleted Alpha's as well.
        if (!ClassFiles.TryFind(workspaceRoot, className, out var found, out var ambiguous))
            return (false, ambiguous);

        if (found == null)
            return (false, $"Class '{className}' not found.");

        List<string> files = [found];

        // The file is moved whole, so whatever else it declares would go with the class: deleting
        // Customer also deleted the CustomerStatus enum and the Money record struct beside it, and
        // said only that Customer was deleted. Refused, with nothing moved, rather than guessed at.
        foreach (var file in files)
        {
            var others = OtherTypesIn(file);
            if (others.Count > 0)
                return (false, $"{Path.GetFileName(file)} also declares {string.Join(", ", others)}, so '{className}' was not deleted: "
                    + "deleting it would delete them too. Move them to files of their own, then delete the class again.");
        }

        var errors = new List<string>();
        var notes = new List<string>();
        var deletedNames = new List<string>();
        var declared = className;

        foreach (var file in files)
        {
            try
            {
                var projectDir = ProjectFileHelper.FindProjectDirectory(file);
                if (string.IsNullOrWhiteSpace(projectDir))
                {
                    errors.Add($"Unable to locate project for '{file}'.");
                    continue;
                }

                var name = DeclaredName(file) ?? Path.GetFileNameWithoutExtension(file);
                declared = name;
                var (parts, kept) = PartsOf(projectDir, file, name);

                var deletedFolder = Path.Combine(projectDir, "DeletedClasses");
                Directory.CreateDirectory(deletedFolder);

                var now = DateTime.UtcNow;
                var entry = new DeletedClassInfo
                {
                    ClassName = name,
                    OriginalPath = Path.GetFullPath(file),
                    DeletedPath = KeptPath(deletedFolder, file, now),
                    DeletedAt = now,
                };
                foreach (var part in parts)
                    entry.Parts.Add(new DeletedClassPart
                    {
                        OriginalPath = Path.GetFullPath(part),
                        DeletedPath = KeptPath(deletedFolder, part, now),
                    });

                CsProjUpdater.RemoveCompileEntry(projectDir, className);

                File.Move(entry.OriginalPath, entry.DeletedPath);
                foreach (var part in entry.Parts)
                    File.Move(part.OriginalPath, part.DeletedPath);

                DeletedClassRegistry.Register(workspaceRoot, entry);

                deletedNames.Add(Path.GetFileName(file));
                deletedNames.AddRange(entry.Parts.Select(p => Path.GetFileName(p.OriginalPath)));

                foreach (var other in kept)
                    notes.Add($"{Path.GetRelativePath(workspaceRoot, other)} declares part of '{name}' and other code too, "
                        + "so it was not deleted; remove that part by hand or the project will not compile.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                errors.Add($"{file}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
            return (false, "Delete completed with errors:" + Environment.NewLine + string.Join(Environment.NewLine, errors));

        var rules = ValidationRegistry.RemoveClass(declared);
        if (!declared.Equals(className, StringComparison.Ordinal))
            rules += ValidationRegistry.RemoveClass(className);

        var message = $"Class '{declared}' deleted ({string.Join(", ", deletedNames)})."
            + (rules > 0 ? $" Its {rules} validation rule{(rules == 1 ? " was" : "s were")} dropped." : string.Empty)
            + $" 'restore class {declared}' brings the file{(deletedNames.Count == 1 ? "" : "s")} back.";

        return (true, notes.Count == 0 ? message : message + Environment.NewLine + string.Join(Environment.NewLine, notes));
    }

    /// <summary>The extension a kept copy gets after <c>.cs</c>.</summary>
    /// <remarks>
    /// The copies were kept as <c>.cs</c> inside the project, and an SDK-style project compiles every
    /// <c>.cs</c> under its folder: the deleted class went on being compiled, and a new class of the
    /// same name failed with CS0101, or CS0260 beside a kept validation partial.
    /// </remarks>
    public const string KeptExtension = ".deleted";

    // DeletedClasses\<stem>_<UTC stamp>.cs.deleted, never over a copy already kept.
    private static string KeptPath(string deletedFolder, string file, DateTime now)
    {
        var stem = Path.GetFileNameWithoutExtension(file);
        var path = Path.Combine(deletedFolder, $"{stem}_{now:yyyyMMddHHmmssfff}.cs{KeptExtension}");
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(deletedFolder, $"{stem}_{now:yyyyMMddHHmmssfff}_{n}.cs{KeptExtension}");
        return path;
    }

    /// <summary>The top-level types <paramref name="file"/> declares besides the one its name says,
    /// with their kind: "enum CustomerStatus". Types nested in the class are part of it.</summary>
    private static List<string> OtherTypesIn(string file)
    {
        var stem = Path.GetFileNameWithoutExtension(file);

        return
        [
            .. CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot()
                .DescendantNodes(n => n is not BaseTypeDeclarationSyntax)
                .OfType<BaseTypeDeclarationSyntax>()
                .Where(t => !t.Identifier.Text.Equals(stem, StringComparison.OrdinalIgnoreCase))
                .Select(t => $"{KindOf(t)} {t.Identifier.Text}")
                .Distinct(StringComparer.Ordinal),
        ];
    }

    private static string KindOf(BaseTypeDeclarationSyntax type) => type switch
    {
        EnumDeclarationSyntax => "enum",
        InterfaceDeclarationSyntax => "interface",
        StructDeclarationSyntax => "struct",
        RecordDeclarationSyntax r => r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record struct" : "record",
        _ => "class",
    };

    private static string? DeclaredName(string file)
    {
        var stem = Path.GetFileNameWithoutExtension(file);
        return CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot()
            .DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.Text.Equals(stem, StringComparison.OrdinalIgnoreCase))
            ?.Identifier.Text;
    }

    /// <summary>
    /// The other files in the project that declare <paramref name="name"/> as a partial type in the
    /// same namespace: those that declare nothing else are deleted with it, the rest only named.
    /// </summary>
    internal static (List<string> Parts, List<string> Kept) PartsOf(string projectDir, string mainFile, string name)
    {
        var parts = new List<string>();
        var kept = new List<string>();

        var mainRoot = CSharpSyntaxTree.ParseText(File.ReadAllText(mainFile)).GetRoot();
        var main = mainRoot.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.Text == name);
        if (main == null)
            return (parts, kept);

        var ns = NamespaceOf(main);

        foreach (var other in ClassFiles.Under(projectDir))
        {
            if (string.Equals(Path.GetFullPath(other), Path.GetFullPath(mainFile), StringComparison.OrdinalIgnoreCase))
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

            var root = CSharpSyntaxTree.ParseText(text).GetRoot();
            var types = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
                .Where(t => t.Parent is not BaseTypeDeclarationSyntax)
                .ToList();

            var isPart = types.OfType<TypeDeclarationSyntax>().Any(t =>
                t.Identifier.Text == name
                && t.Kind() == main.Kind()
                && t.Modifiers.Any(SyntaxKind.PartialKeyword)
                && NamespaceOf(t) == ns);
            if (!isPart)
                continue;

            if (types.All(t => t.Identifier.Text == name))
                parts.Add(other);
            else
                kept.Add(other);
        }

        return (parts, kept);
    }

    private static string NamespaceOf(SyntaxNode node) =>
        string.Join(".", node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString()));
}
