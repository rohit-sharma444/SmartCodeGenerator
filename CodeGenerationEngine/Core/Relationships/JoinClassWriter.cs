using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerationEngine.Core.Relationships;

/// <summary>
/// The third class a many-to-many needs, and the collections that point at it.
/// </summary>
/// <remarks>
/// The tool never proposes one of these: a join class it could detect already reads as two
/// ordinary foreign keys, which the detector finishes without needing to know it is a join. This
/// runs only when the user asks for a many-to-many outright.
/// <para>
/// The class it writes has two foreign keys and no date of its own, so the master/transaction
/// classifier reads it as a master and will give it a screen of its own if the project is later
/// built as an app. That is recorded in the design and deliberately not changed here.
/// </para>
/// </remarks>
public static class JoinClassWriter
{
    public static string DefaultName(string a, string b) => a + b;

    public static RelationshipWritten Apply(
        string workspaceRoot, string a, string b, string joinName)
    {
        var classes = ClassSnapshots.Read(workspaceRoot);

        if (classes.Any(c => string.Equals(c.Name, joinName, StringComparison.OrdinalIgnoreCase)))
            return new RelationshipWritten(
                false, $"There is already a class called '{joinName}'.", []);

        var left = Find(classes, a);
        var right = Find(classes, b);

        if (left is null || right is null)
            return new RelationshipWritten(
                false, $"Both '{a}' and '{b}' have to exist first.", []);

        var originals = classes.ToDictionary(c => c.File, c => File.ReadAllText(c.File));
        var created = new List<string>();
        var added = new List<string>();

        try
        {
            var joinFile = Path.Combine(Path.GetDirectoryName(left.File)!, joinName + ".cs");

            File.WriteAllText(joinFile, RoslynClassGenerator.Generate(Join(joinName, left, right), NamespaceOf(left)));
            created.Add(joinFile);
            added.Add($"new class {joinName}");

            var collection = RelationshipConvention.InverseCollection(joinName);

            foreach (var parent in new[] { left, right })
            {
                if (!AttributedPropertyWriter.TryAdd(
                        parent.File, parent.Name, collection,
                        [RelationshipConvention.JsonSerializationNamespace,
                         RelationshipConvention.ComponentModelNamespace],
                        out var error))
                    return RelationshipWriter.Restore(originals, created, error);

                added.Add($"{parent.Name}.{collection.Name} ({collection.Type})");
            }

            var verdict = RelationshipWriter.Verify(workspaceRoot);

            if (!verdict.Ok)
                return RelationshipWriter.Restore(originals, created, verdict.Problem);

            return new RelationshipWritten(
                true, $"{left.Name} and {right.Name} are now related through {joinName}.", added);
        }
        catch (Exception exception)
        {
            return RelationshipWriter.Restore(originals, created, exception.Message);
        }
    }

    /// <summary>The join class itself: an identity, and a foreign key with its reference for each
    /// side.</summary>
    private static ClassDefinition Join(string joinName, ClassSnapshot left, ClassSnapshot right)
    {
        var join = new ClassDefinition { Name = joinName };

        join.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
        join.Properties.Add(RelationshipConvention.ForeignKey(left.Name));
        join.Properties.Add(RelationshipConvention.Forward(left.Name));
        join.Properties.Add(RelationshipConvention.ForeignKey(right.Name));
        join.Properties.Add(RelationshipConvention.Forward(right.Name));

        join.Usings.Add(RelationshipConvention.JsonSerializationNamespace);
        join.Usings.Add(RelationshipConvention.ComponentModelNamespace);

        return join;
    }

    /// <summary>
    /// The namespace of the class the join class sits beside.
    /// </summary>
    /// <remarks>
    /// Not <see cref="RoslynClassGenerator"/>'s <c>GeneratedCode</c> default. A join class in
    /// <c>GeneratedCode</c> cannot resolve <c>Order</c> and <c>Product</c> in <c>Shop</c>, so the
    /// verification compile would fail on every many-to-many and roll the whole thing back - the
    /// feature would never work once, and the message the user saw would be a compiler complaint
    /// about their own classes rather than about the namespace the tool chose for them. A
    /// workspace whose classes are spread across several namespaces puts the join class in the
    /// first parent's, which is the same guess <c>ClassMemberEditor</c> already makes.
    /// </remarks>
    private static string NamespaceOf(ClassSnapshot snapshot) =>
        NamespaceExtractor.GetNamespace(CSharpSyntaxTree.ParseText(File.ReadAllText(snapshot.File)))
        ?? "GeneratedCode";

    private static ClassSnapshot? Find(IReadOnlyList<ClassSnapshot> classes, string name) =>
        classes.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}
