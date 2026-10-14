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
/// Every top-level class in a file is a snapshot, with only its own properties
/// (<see cref="EntityExtractor.ExtractProperties(string, string)"/>), from all its partial parts.
/// A file with a second type used to contribute no snapshot at all, because the per-file reader
/// merged every type's properties into one list. That made a class with a nested class added by
/// hand look absent - New - so an add to a generated app treated it as a class to write.
/// A nested class is not a snapshot of its own: it is part of its parent.
/// </para>
/// <para>
/// A partial class declared in several files is one snapshot, in the file named after it when
/// there is one.
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

            // Classes only, and not nested ones: a record or an interface is a type the detector
            // has no convention for, and a nested class belongs to its parent.
            var names = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(c => c.Parent is not TypeDeclarationSyntax)
                .Select(c => c.Identifier.Text)
                .Distinct(StringComparer.Ordinal);

            foreach (var name in names)
            {
                var snapshot = new ClassSnapshot(name, file, EntityExtractor.ExtractProperties(file, name));

                // One per class: its partial parts in other files describe the same class.
                var same = snapshots.FindIndex(s => s.Name.Equals(name, StringComparison.Ordinal));

                if (same < 0)
                    snapshots.Add(snapshot);
                else if (!NamedAfter(snapshots[same]) && NamedAfter(snapshot))
                    snapshots[same] = snapshot;
            }
        }

        return snapshots;
    }

    private static bool NamedAfter(ClassSnapshot snapshot)
        => Path.GetFileNameWithoutExtension(snapshot.File).Equals(snapshot.Name, StringComparison.OrdinalIgnoreCase);

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
