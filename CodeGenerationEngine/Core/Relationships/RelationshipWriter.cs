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
        // A many-to-many is a different shape, not a variation on this one: neither side carries a
        // foreign key, a third class does, and it is the only kind that creates a file.
        if (proposal.Kind == RelationshipKind.ManyToMany)
            return JoinClassWriter.Apply(
                workspaceRoot, proposal.Owner, proposal.Target,
                JoinClassWriter.DefaultName(proposal.Owner, proposal.Target));

        var classes = ClassSnapshots.Read(workspaceRoot);

        var owner = Find(classes, proposal.Owner);
        var target = Find(classes, proposal.Target);

        if (owner is null)
            return new RelationshipWritten(false, $"Class '{proposal.Owner}' not found.", []);

        if (target is null)
            return new RelationshipWritten(false, $"Class '{proposal.Target}' not found.", []);

        if (Blocking(owner, target) is { } blocker)
            return new RelationshipWritten(false, blocker, []);

        var parts = Parts(owner, target, proposal.Kind).ToList();

        if (Unreadable(parts.Select(p => p.File)) is { } refusal)
            return new RelationshipWritten(false, refusal, []);

        var originals = Originals(parts.Select(p => p.File));

        var added = new List<string>();

        try
        {
            foreach (var (file, className, property) in parts)
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

    /// <summary>
    /// Why this relationship cannot be made, or <c>null</c> if it can.
    /// </summary>
    /// <remarks>
    /// <see cref="Parts"/> skips a property the class already has, matching on name. That is right
    /// when the property is the one the convention wanted, and silently wrong when the name is
    /// held by something else: a class carrying a <c>string CustomerId</c> gets no
    /// <c>int CustomerId</c> written, nothing is added for the compiler to object to, the
    /// verification passes, and the user is told the two classes are related while
    /// <c>ForeignKeys.TargetsOf</c> - which requires exactly <c>int</c> - still sees no
    /// relationship, and so do the seeder, the validators, the console picker and the screen
    /// split. A refusal naming the property is the only honest answer.
    /// <para>
    /// Only the two properties <c>TargetsOf</c> reads are checked: the foreign key and the forward
    /// reference, both on the owner. The inverse is not, because <c>TargetsOf</c> never looks at
    /// it - an existing collection under a different type is a cosmetic mismatch, not a
    /// relationship that fails to exist, and refusing over it would block pairs that work.
    /// </para>
    /// </remarks>
    private static string? Blocking(ClassSnapshot owner, ClassSnapshot target) =>
        Clash(owner, RelationshipConvention.ForeignKey(target.Name))
        ?? Clash(owner, RelationshipConvention.Forward(target.Name));

    private static string? Clash(ClassSnapshot owner, PropertyDefinition wanted)
    {
        var existing = owner.Properties.FirstOrDefault(
            p => string.Equals(p.Name, wanted.Name, StringComparison.OrdinalIgnoreCase));

        if (existing is null || string.Equals(existing.Type, wanted.Type, StringComparison.Ordinal))
            return null;

        return $"{owner.Name}.{existing.Name} is a {existing.Type}, not a {wanted.Type}, so it "
             + "cannot carry this relationship. Nothing was changed.";
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
    /// The bytes of each file a relationship is about to write, keyed without regard to case.
    /// </summary>
    /// <remarks>
    /// Only those files. Every class in the workspace used to be read here and written back on a
    /// rollback, so a refused relationship rewrote files it never touched - and through a UTF-8
    /// decode, which turned every Windows-1252 byte above 0x7F into U+FFFD. Keyed by file, not by
    /// class: a file that declares two classes (Order and OrderLine) is one entry, where
    /// <c>ToDictionary</c> over the classes threw "An item with the same key has already been
    /// added". Raw bytes, so a rollback is exact whatever the encoding and line ends.
    /// </remarks>
    internal static Dictionary<string, byte[]> Originals(IEnumerable<string> files)
    {
        var originals = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
            originals.TryAdd(file, File.ReadAllBytes(file));

        return originals;
    }

    /// <summary>
    /// Why one of <paramref name="files"/> cannot be edited - it is not UTF-8 - or <c>null</c>.
    /// Asked before anything is written, so the refusal leaves the workspace untouched.
    /// </summary>
    internal static string? Unreadable(IEnumerable<string> files)
    {
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                global::CodeGenerationEngine.Core.Validation.SourceFileText.Load(file);
            }
            catch (global::CodeGenerationEngine.Core.Validation.NotUtf8Exception ex)
            {
                return ex.Message;
            }
        }

        return null;
    }

    /// <summary>
    /// Deletes every newly-created file in <paramref name="created"/>, then puts back the bytes of
    /// every file in <paramref name="originals"/>, and reports the failure.
    /// </summary>
    /// <remarks>
    /// Deletion happens before restoration: a path in <paramref name="created"/> was never in
    /// <paramref name="originals"/>, and a file that should not have survived the failure must not
    /// be left behind just because it was written before a pre-existing file was.
    /// </remarks>
    internal static RelationshipWritten Restore(
        Dictionary<string, byte[]> originals, IReadOnlyList<string> created, string why)
    {
        foreach (var file in created)
        {
            if (File.Exists(file))
                File.Delete(file);
        }

        foreach (var (file, bytes) in originals)
            File.WriteAllBytes(file, bytes);

        return new RelationshipWritten(false, why + " Nothing was changed.", []);
    }

    private static bool Has(ClassSnapshot snapshot, string propertyName) =>
        snapshot.Properties.Any(
            p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));

    private static ClassSnapshot? Find(IReadOnlyList<ClassSnapshot> classes, string name) =>
        classes.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}
