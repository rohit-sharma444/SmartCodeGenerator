using CodeGenerationEngine.Core.Relationships;

namespace CodeGenerator.Tests.Core.Relationships;

/// <summary>
/// Writing a relationship into real files, and putting them back when it does not compile.
/// </summary>
/// <remarks>
/// A half-written relationship — a foreign key with no navigation property, or the reverse — is
/// invisible to `ForeignKeys.TargetsOf` and therefore to the seeder, the validators, the console
/// picker and the screen split. It is worse than no relationship, so rollback is part of the
/// feature rather than error handling bolted on.
/// </remarks>
[TestClass]
public class RelationshipWriterTests
{
    [TestMethod]
    public void Both_sides_are_written_and_the_result_compiles()
    {
        var root = NewRoot();
        Write(root, "Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n    public int CustomerId { get; set; }\n}\n");
        Write(root, "Customer.cs", "namespace Shop;\n\npublic class Customer\n{\n    public int Id { get; set; }\n}\n");

        var result = RelationshipWriter.Apply(
            root,
            new RelationshipProposal("Order", "Customer", RelationshipKind.OneToMany, "because"));

        Assert.IsTrue(result.Ok, result.Message);

        var order = File.ReadAllText(Path.Combine(root, "Order.cs"));
        var customer = File.ReadAllText(Path.Combine(root, "Customer.cs"));

        Assert.Contains("Customer Customer", order);
        Assert.Contains("JsonIgnore", order);
        Assert.Contains("List<Order> Orders", customer);
        Assert.Contains("using System.ComponentModel;", customer);
    }

    [TestMethod]
    public void The_missing_foreign_key_is_written_when_only_the_navigation_property_exists()
    {
        var root = NewRoot();
        Write(root, "Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n    public Customer Customer { get; set; } = new();\n}\n");
        Write(root, "Customer.cs", "namespace Shop;\n\npublic class Customer\n{\n    public int Id { get; set; }\n}\n");

        var result = RelationshipWriter.Apply(
            root,
            new RelationshipProposal("Order", "Customer", RelationshipKind.OneToMany, "because"));

        Assert.IsTrue(result.Ok, result.Message);
        Assert.Contains("int CustomerId", File.ReadAllText(Path.Combine(root, "Order.cs")));
    }

    [TestMethod]
    public void A_one_to_one_gets_a_single_reference_back_not_a_collection()
    {
        var root = NewRoot();
        Write(root, "Employee.cs", "namespace Shop;\n\npublic class Employee\n{\n    public int Id { get; set; }\n}\n");
        Write(root, "Locker.cs", "namespace Shop;\n\npublic class Locker\n{\n    public int Id { get; set; }\n}\n");

        var result = RelationshipWriter.Apply(
            root,
            new RelationshipProposal("Employee", "Locker", RelationshipKind.OneToOne, "because"));

        Assert.IsTrue(result.Ok, result.Message);

        var locker = File.ReadAllText(Path.Combine(root, "Locker.cs"));
        Assert.Contains("Employee Employee", locker);

        // The whole point of the kind: a one-to-one puts a single reference back, not a
        // collection. Proved non-vacuous rather than assumed - a `List<int> Tags` added to the
        // Locker fixture turned this line red and reported `Assert.DoesNotContain failed`, so it
        // is reading the file and not the argument-order mistake that passes green either way.
        // Flipping the kind is NOT the probe: that reddens the `Employee Employee` line above and
        // never reaches this one.
        Assert.DoesNotContain("List<", locker);
    }

    [TestMethod]
    public void A_relationship_that_does_not_compile_leaves_every_file_byte_for_byte()
    {
        var root = NewRoot();

        // Customer is a static class, so a `List<Order> Orders` on it is CS0708 — a field
        // in a static class. The write succeeds, the compile does not, and both files must
        // come back.
        Write(root, "Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n    public int CustomerId { get; set; }\n}\n");
        Write(root, "Customer.cs", "namespace Shop;\n\npublic static class Customer\n{\n}\n");

        var before = Snapshot(root);

        var result = RelationshipWriter.Apply(
            root,
            new RelationshipProposal("Order", "Customer", RelationshipKind.OneToMany, "because"));

        Assert.IsFalse(result.Ok, "a relationship that does not compile must not be reported as written");
        CollectionAssert.AreEquivalent(before, Snapshot(root));
    }

    [TestMethod]
    public void A_target_class_that_is_not_in_the_workspace_is_refused_before_anything_is_written()
    {
        var root = NewRoot();
        Write(root, "Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n}\n");

        var before = Snapshot(root);

        var result = RelationshipWriter.Apply(
            root,
            new RelationshipProposal("Order", "Nowhere", RelationshipKind.OneToMany, "because"));

        Assert.IsFalse(result.Ok);
        CollectionAssert.AreEquivalent(before, Snapshot(root));
    }

    [TestMethod]
    public void A_foreign_key_name_held_by_another_type_is_refused_not_reported_as_related()
    {
        // Order already has a `string CustomerId`. `int CustomerId` is what makes the pair
        // visible to ForeignKeys.TargetsOf, and that name is taken by something that is not an
        // int, so this relationship cannot be made. The dangerous outcome is not a crash: it is
        // reporting success, because everything here compiles - the writer skips a property whose
        // name is already present, so nothing is written that the compiler could object to, and
        // the user is told the classes are related while every consumer of TargetsOf still sees
        // no relationship at all.
        var root = NewRoot();
        Write(root, "Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n    public string CustomerId { get; set; } = string.Empty;\n}\n");
        Write(root, "Customer.cs", "namespace Shop;\n\npublic class Customer\n{\n    public int Id { get; set; }\n}\n");

        var before = Snapshot(root);

        var result = RelationshipWriter.Apply(
            root,
            new RelationshipProposal("Order", "Customer", RelationshipKind.OneToMany, "because"));

        Assert.IsFalse(result.Ok, "a relationship the foreign key cannot carry must not be reported as written");
        Assert.Contains("CustomerId", result.Message);
        CollectionAssert.AreEquivalent(before, Snapshot(root));
    }

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetFileName(f), File.ReadAllText);

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "rw-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string directory, string name, string code)
        => File.WriteAllText(Path.Combine(directory, name), code);
}
