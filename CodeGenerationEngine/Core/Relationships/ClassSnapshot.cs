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
/// A file that declares more than one class contributes no snapshot at all, the same as a file
/// with no class in it. <see cref="EntityExtractor.ExtractProperties"/> works per file, not per
/// type: it walks every type declaration in the file - nested ones included - and merges all of
/// their properties into one flat list with no record of which type each property came from. Once
/// a file holds two classes there is no way to tell which properties are whose, so attributing
/// that merged list to either class's name would silently hand the detector a class described
/// with the other class's fields. A class that is skipped is recoverable - split the file and it
/// shows up next run - a class silently described with someone else's fields is not, because
/// nothing downstream can tell it happened. Nesting does not exempt a class from this: a class
/// nested inside another is still a second type declaration in the file, so it counts the same as
/// a second top-level class and the file still contributes nothing.
/// </para>
/// </remarks>
public static class ClassSnapshots
{
    public static IReadOnlyList<ClassSnapshot> Read(string workspaceRoot)
    {
        var snapshots = new List<ClassSnapshot>();

        foreach (var file in ClassFiles.Under(workspaceRoot))
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();

            var classes = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .ToList();

            if (classes.Count != 1)
                continue;

            snapshots.Add(new ClassSnapshot(
                classes[0].Identifier.Text,
                file,
                EntityExtractor.ExtractProperties(file)));
        }

        return snapshots;
    }
}
