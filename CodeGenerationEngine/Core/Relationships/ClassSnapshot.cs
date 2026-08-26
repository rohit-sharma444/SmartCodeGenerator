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
/// </remarks>
public static class ClassSnapshots
{
    public static IReadOnlyList<ClassSnapshot> Read(string workspaceRoot)
    {
        var snapshots = new List<ClassSnapshot>();

        foreach (var file in ClassFiles.Under(workspaceRoot))
        {
            var root = CSharpSyntaxTree.ParseText(System.IO.File.ReadAllText(file)).GetRoot();

            var declared = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault();

            if (declared is null)
                continue;

            snapshots.Add(new ClassSnapshot(
                declared.Identifier.Text,
                file,
                EntityExtractor.ExtractProperties(file)));
        }

        return snapshots;
    }
}
