using CodeGenerationEngine.Core.Relationships;

namespace CodeGenerator.Tests.Core.Relationships;

/// <summary>What the detector is given: every class in the workspace with its file and its
/// properties, read once so the detector itself can stay pure.</summary>
[TestClass]
public class ClassSnapshotTests
{
    [TestMethod]
    public void Every_class_is_read_with_its_properties()
    {
        var root = NewRoot();
        Write(root, "Order.cs", """
            namespace Shop;
            public class Order
            {
                public int Id { get; set; }
                public int CustomerId { get; set; }
            }
            """);
        Write(root, "Customer.cs", "namespace Shop; public class Customer { public int Id { get; set; } }");

        var snapshots = ClassSnapshots.Read(root);

        Assert.HasCount(2, snapshots);

        var order = snapshots.Single(s => s.Name == "Order");
        Assert.AreEqual("Order.cs", Path.GetFileName(order.File));
        Assert.Contains("CustomerId", order.Properties.Select(p => p.Name).ToList());
    }

    [TestMethod]
    public void A_snapshot_converts_to_a_class_definition_for_the_convention_to_check()
    {
        var root = NewRoot();
        Write(root, "Customer.cs", "public class Customer { public int Id { get; set; } }");

        var definition = ClassSnapshots.Read(root).Single().ToDefinition();

        Assert.AreEqual("Customer", definition.Name);
        Assert.HasCount(1, definition.Properties);
    }

    [TestMethod]
    public void A_file_with_no_class_in_it_contributes_nothing()
    {
        var root = NewRoot();
        Write(root, "Notes.cs", "// nothing but a comment");

        Assert.IsEmpty(ClassSnapshots.Read(root));
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "cs-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string directory, string name, string code)
        => File.WriteAllText(Path.Combine(directory, name), code);
}
