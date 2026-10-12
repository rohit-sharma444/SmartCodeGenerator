using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core.Relationships;

/// <summary>One class as the detector sees it: its name, the file it lives in, and its data.</summary>
public sealed record ClassSnapshot(
    string Name,
    string File,
    IReadOnlyList<PropertyDefinition> Properties)
{
    /// <summary>The same class as a <see cref="ClassDefinition"/>, which is what
    /// <see cref="RelationshipConvention.CanAdd"/> takes.</summary>
    public ClassDefinition ToDefinition()
    {
        var definition = new ClassDefinition { Name = Name };
        definition.Properties.AddRange(Properties);
        return definition;
    }
}

/// <summary>
/// Reads a workspace's classes so the detector does not have to touch the disk.
/// </summary>
/// <remarks>
/// The class name comes from the declaration in the file rather than from the file name, because
/// the detector matches property types against class names and a type is what the compiler saw,
/// not what the file was called. Properties come from
/// <see cref="EntityExtractor.ExtractProperties"/>, the same reader the chat page's tree uses, so
/// the two cannot disagree about what a class holds.
/// <para>
/// A file that declares more than one <em>type</em> contributes no snapshot at all, the same as a
/// file with no class in it. <see cref="EntityExtractor.ExtractProperties"/> works per file, not
/// per type: it walks every type declaration in the file - nested ones included - and merges all
/// of their properties into one flat list with no record of which type each property came from.
/// Once a file holds two types there is no way to tell which properties are whose, so attributing
/// that merged list to either name would silently hand the detector a class described with the
/// other type's fields. A class that is skipped is recoverable - split the file and it shows up
/// next run - a class silently described with someone else's fields is not, because nothing
/// downstream can tell it happened.
/// </para>
/// <para>
/// The rule is deliberately drawn around type <em>declarations</em>, not classes, because that is
/// the scope <see cref="EntityExtractor.ExtractProperties"/> actually merges over. A record, a
/// struct and an interface are all <see cref="TypeDeclarationSyntax"/> siblings of
/// <see cref="ClassDeclarationSyntax"/> rather than subtypes of it, so counting classes alone is
/// blind to them: a file pairing <c>class Order</c> with <c>record OrderNote</c> holds exactly one
/// class and would slip through, carrying the record's properties under the class's name. An enum
/// is not a <see cref="TypeDeclarationSyntax"/> and is correctly outside the rule. Nesting does not
/// exempt a type either: a class nested inside another is still a second declaration in the file.
/// </para>
/// <para>
/// What is counted is distinct type <em>names</em>, not declarations, so that two
/// <c>partial class Order</c> fragments in one file collapse to one name and the file is allowed.
/// There is nothing to misattribute in that case - every property the reader finds genuinely
/// belongs to the one class Order, whichever fragment declares it.
/// </para>
/// </remarks>
public static class ClassSnapshots
{
    public static IReadOnlyList<ClassSnapshot> Read(string workspaceRoot)
    {
        var snapshots = new List<ClassSnapshot>();
        var testFolders = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in ClassFiles.Under(workspaceRoot))
        {
            // A test project's classes are not the app's: the relationship offer was proposing
            // "RepositoryRoundTripTests and Repository look related" out of a generated xunit
            // project, and show relationships listed SeedDataTests beside the entities.
            if (InTestProject(Path.GetDirectoryName(file)!, workspaceRoot, testFolders))
                continue;

            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();

            var types = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .ToList();

            var classes = types.OfType<ClassDeclarationSyntax>().ToList();

            // No class at all is nothing to snapshot; a record or an interface on its own is a
            // type the detector has no convention for.
            if (classes.Count == 0)
                continue;

            // Distinct names, not declarations: partial fragments of one class collapse to one.
            if (types.Select(t => t.Identifier.Text).Distinct(StringComparer.Ordinal).Count() != 1)
                continue;

            snapshots.Add(new ClassSnapshot(
                classes[0].Identifier.Text,
                file,
                EntityExtractor.ExtractProperties(file)));
        }

        return snapshots;
    }

    /// <summary>
    /// Whether the nearest project file at or above <paramref name="folder"/>, up to the
    /// workspace root, is a test project. Cached per folder, since every file in one asks.
    /// </summary>
    private static bool InTestProject(string folder, string workspaceRoot, Dictionary<string, bool> cache)
    {
        if (cache.TryGetValue(folder, out var known))
            return known;

        var csproj = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.csproj").FirstOrDefault()
            : null;

        bool answer;

        if (csproj is not null)
            answer = IsTestProject(csproj);
        else
        {
            var parent = Path.GetDirectoryName(folder);
            var atRoot = parent is null
                || Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar).Equals(
                    Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);

            answer = !atRoot && InTestProject(parent!, workspaceRoot, cache);
        }

        cache[folder] = answer;
        return answer;
    }

    /// <summary>
    /// A test project by what it references, as <c>dotnet test</c> decides: the test SDK, the
    /// MSTest SDK, or an explicit IsTestProject.
    /// </summary>
    private static bool IsTestProject(string csproj)
    {
        string text;

        try
        {
            text = File.ReadAllText(csproj);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return text.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase)
            || text.Contains("MSTest.Sdk", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<IsTestProject>true</IsTestProject>", StringComparison.OrdinalIgnoreCase);
    }
}
