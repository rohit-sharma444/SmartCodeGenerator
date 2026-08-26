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

    [TestMethod]
    public void A_file_with_more_than_one_class_contributes_no_snapshot()
    {
        // EntityExtractor.ExtractProperties works per file, not per type: it walks every type
        // declaration in the file and merges all their properties into one flat list with no
        // record of which type a property came from. Attributing that merged list to either
        // class's name would silently hand the detector a class described with the other
        // class's fields, so the whole file is skipped instead - the same as a file with no
        // class at all.
        var root = NewRoot();
        Write(root, "Two.cs", """
            namespace Shop;
            public class Order
            {
                public int Id { get; set; }
                public int CustomerId { get; set; }
            }
            public class OrderNote
            {
                public string Text { get; set; }
                public int Rating { get; set; }
            }
            """);

        Assert.IsEmpty(ClassSnapshots.Read(root));
    }

    [TestMethod]
    public void A_class_nested_inside_another_counts_toward_more_than_one_and_yields_no_snapshot()
    {
        // EntityExtractor.ExtractProperties deliberately walks nested types too, so a class
        // nested inside another is exactly as much a source of misattribution as a second
        // top-level class in the same file: its properties land in the same merged, unattributed
        // list. So nesting does not exempt a class from the "more than one class" rule above -
        // Outer + Inner is two classes, and the file contributes no snapshot.
        var root = NewRoot();
        Write(root, "Outer.cs", """
            namespace Shop;
            public class Outer
            {
                public int Id { get; set; }

                public class Inner
                {
                    public string Text { get; set; }
                }
            }
            """);

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
