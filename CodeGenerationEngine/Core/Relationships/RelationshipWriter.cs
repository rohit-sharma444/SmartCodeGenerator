using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core.Relationships;

/// <summary>What happened when a relationship was applied.</summary>
public sealed record RelationshipWritten(
    bool Ok,
    string Message,
    IReadOnlyList<string> Added);

/// <summary>
/// Writes one relationship into the workspace, or leaves the workspace exactly as it was.
/// </summary>
/// <remarks>
/// The verification is <see cref="Compiler.CompileSources"/> over every class in the workspace,
/// NOT <see cref="Compiler.Compile"/> over the file just edited. A navigation property is typed as
/// another class, so it cannot resolve in a single-file compilation — the per-class check on the
/// <c>create class</c> path would report CS0246 for a relationship that is perfectly correct.
/// <para>
/// A synthetic <c>global using</c> tree is compiled alongside the sources because the real projects
/// have <c>ImplicitUsings</c> enabled and a bare Roslyn compilation does not. Without it a
/// <c>List&lt;T&gt;</c> inverse collection fails to compile here while building fine on disk, and
/// the honest fix is to make the check match the compiler rather than to add a using the
/// <c>build app</c> path does not emit.
/// </para>
/// <para>
/// <see cref="Verify"/> and <see cref="Restore"/> are <c>internal</c> rather than <c>private</c>
/// because a later join-class writer, living in this same assembly, needs to call both without
/// duplicating either — a freshly reviewed rollback path is exactly the kind of seam that should
/// not be re-implemented by a second caller.
/// </para>
/// </remarks>
public static class RelationshipWriter
{
    /// <summary>Mirrors the implicit usings a `net10.0` class library gets, so the verification
    /// compile sees what `dotnet build` sees.</summary>
    private const string ImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    public static RelationshipWritten Apply(string workspaceRoot, RelationshipProposal proposal)
    {
        var classes = ClassSnapshots.Read(workspaceRoot);

        var owner = Find(classes, proposal.Owner);
        var target = Find(classes, proposal.Target);

        if (owner is null)
            return new RelationshipWritten(false, $"Class '{proposal.Owner}' not found.", []);

        if (target is null)
            return new RelationshipWritten(false, $"Class '{proposal.Target}' not found.", []);

        var originals = classes.ToDictionary(c => c.File, c => File.ReadAllText(c.File));

        var added = new List<string>();

        try
        {
            foreach (var (file, className, property) in Parts(owner, target, proposal.Kind))
            {
                if (!AttributedPropertyWriter.TryAdd(
                        file, className, property, UsingsFor(property), out var error))
                {
                    var rolledBack = Restore(originals, [], error);
                    return new RelationshipWritten(rolledBack.Ok, rolledBack.Message, rolledBack.Added);
                }

                added.Add($"{className}.{property.Name} ({property.Type})");
            }

            var verdict = Verify(workspaceRoot);

            if (!verdict.Ok)
            {
                var rolledBack = Restore(originals, [], verdict.Problem);
                return new RelationshipWritten(rolledBack.Ok, rolledBack.Message, rolledBack.Added);
            }

            return new RelationshipWritten(
                true,
                $"{proposal.Owner} and {proposal.Target} are now related.",
                added);
        }
        catch (Exception ex)
        {
            var rolledBack = Restore(originals, [], ex.Message);
            return new RelationshipWritten(rolledBack.Ok, rolledBack.Message, rolledBack.Added);
        }
    }

    /// <summary>The properties this relationship needs, skipping any the class already has.</summary>
    private static IEnumerable<(string File, string ClassName, PropertyDefinition Property)> Parts(
        ClassSnapshot owner, ClassSnapshot target, RelationshipKind kind)
    {
        var foreignKey = RelationshipConvention.ForeignKey(target.Name);
        var forward = RelationshipConvention.Forward(target.Name);

        var inverse = kind == RelationshipKind.OneToOne
            ? RelationshipConvention.InverseReference(owner.Name)
            : RelationshipConvention.InverseCollection(owner.Name);

        if (!Has(owner, foreignKey.Name))
            yield return (owner.File, owner.Name, foreignKey);

        if (!Has(owner, forward.Name))
            yield return (owner.File, owner.Name, forward);

        if (!Has(target, inverse.Name))
            yield return (target.File, target.Name, inverse);
    }

    private static IEnumerable<string> UsingsFor(PropertyDefinition property) =>
        property.Attributes.Count == 0
            ? []
            : [RelationshipConvention.JsonSerializationNamespace,
               RelationshipConvention.ComponentModelNamespace];

    /// <summary>
    /// Compiles every class in the workspace as one assembly, alongside the synthetic implicit
    /// usings tree, so the check matches what `dotnet build` would see on disk.
    /// </summary>
    internal static (bool Ok, string Problem) Verify(string workspaceRoot)
    {
        var sources = new List<(string Path, string Code)>
        {
            ("__ImplicitUsings.cs", ImplicitUsings)
        };

        foreach (var file in ClassFiles.Under(workspaceRoot))
            sources.Add((file, File.ReadAllText(file)));

        var result = Compiler.CompileSources(sources);

        if (result.Success)
            return (true, string.Empty);

        var errors = result.Diagnostics
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .Take(3);

        return (false, "That relationship does not compile: " + string.Join("; ", errors));
    }

    /// <summary>
    /// Deletes every newly-created file in <paramref name="created"/>, then restores every
    /// pre-existing file to its original content, and reports the failure.
    /// </summary>
    /// <remarks>
    /// Deletion happens before restoration: a path in <paramref name="created"/> was never in
    /// <paramref name="originals"/>, and a file that should not have survived the failure must not
    /// be left behind just because it was written before a pre-existing file was.
    /// </remarks>
    internal static RelationshipWritten Restore(
        Dictionary<string, string> originals, IReadOnlyList<string> created, string why)
    {
        foreach (var file in created)
        {
            if (File.Exists(file))
                File.Delete(file);
        }

        foreach (var (file, code) in originals)
            File.WriteAllText(file, code);

        return new RelationshipWritten(false, why + " Nothing was changed.", []);
    }

    private static bool Has(ClassSnapshot snapshot, string propertyName) =>
        snapshot.Properties.Any(
            p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));

    private static ClassSnapshot? Find(IReadOnlyList<ClassSnapshot> classes, string name) =>
        classes.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}
