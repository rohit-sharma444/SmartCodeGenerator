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
    public void Each_class_in_a_file_is_a_snapshot_with_only_its_own_properties()
    {
        // The reader used to merge every type's properties in the file, so such a file was
        // skipped. Read per type, each class is described with its own fields and no other's.
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

        var snapshots = ClassSnapshots.Read(root);

        Assert.HasCount(2, snapshots);
        CollectionAssert.AreEquivalent(
            new[] { "Id", "CustomerId" },
            snapshots.Single(s => s.Name == "Order").Properties.Select(p => p.Name).ToList());
        CollectionAssert.AreEquivalent(
            new[] { "Text", "Rating" },
            snapshots.Single(s => s.Name == "OrderNote").Properties.Select(p => p.Name).ToList());
    }

    [TestMethod]
    public void A_class_with_a_nested_class_is_a_snapshot_without_the_nested_class_properties()
    {
        // A nested class added by hand made its parent vanish from the snapshots, so an add to a
        // generated app took the parent for a New class. The nested class is part of its parent,
        // not a snapshot of its own, and its properties are not the parent's.
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

        var snapshot = ClassSnapshots.Read(root).Single();

        Assert.AreEqual("Outer", snapshot.Name);
        CollectionAssert.AreEqual(new[] { "Id" }, snapshot.Properties.Select(p => p.Name).ToList());
    }

    [TestMethod]
    public void A_class_alongside_a_record_in_one_file_is_a_snapshot_without_the_record()
    {
        // A record is a type the detector has no convention for, and its properties are its own.
        var root = NewRoot();
        Write(root, "Two.cs", """
            namespace Shop;
            public class Order
            {
                public int Id { get; set; }
                public int CustomerId { get; set; }
            }
            public record OrderNote(string Text, int Rating);
            """);

        var snapshot = ClassSnapshots.Read(root).Single();

        Assert.AreEqual("Order", snapshot.Name);
        CollectionAssert.AreEquivalent(
            new[] { "Id", "CustomerId" }, snapshot.Properties.Select(p => p.Name).ToList());
    }

    [TestMethod]
    public void A_class_alongside_a_struct_in_one_file_is_a_snapshot_without_the_struct()
    {
        var root = NewRoot();
        Write(root, "Two.cs", """
            namespace Shop;
            public class Order
            {
                public int Id { get; set; }
                public int CustomerId { get; set; }
            }
            public struct OrderNote
            {
                public string Text { get; set; }
                public int Rating { get; set; }
            }
            """);

        var snapshot = ClassSnapshots.Read(root).Single();

        Assert.AreEqual("Order", snapshot.Name);
        CollectionAssert.AreEquivalent(
            new[] { "Id", "CustomerId" }, snapshot.Properties.Select(p => p.Name).ToList());
    }

    [TestMethod]
    public void A_partial_class_in_two_files_is_one_snapshot_in_its_own_file_with_both_parts()
    {
        var root = NewRoot();
        Write(root, "Order.cs", "namespace Shop; public partial class Order { public int Id { get; set; } }");
        Write(root, "Order.Extra.cs", "namespace Shop; public partial class Order { public string Note { get; set; } }");

        var snapshot = ClassSnapshots.Read(root).Single();

        Assert.AreEqual("Order.cs", Path.GetFileName(snapshot.File));
        CollectionAssert.AreEquivalent(
            new[] { "Id", "Note" }, snapshot.Properties.Select(p => p.Name).ToList());
    }

    [TestMethod]
    public void Two_partial_fragments_of_the_same_class_contribute_one_snapshot_with_both_fragments_properties()
    {
        // Two `partial class Order` fragments declare the same name, so they collapse to one
        // distinct type and the file is allowed - unlike the class+record/class+struct cases
        // above, there is nothing to misattribute here: every property EntityExtractor.
        // ExtractProperties finds genuinely belongs to the one class Order, wherever the fragment
        // that declares it lives.
        var root = NewRoot();
        Write(root, "Order.cs", """
            namespace Shop;
            public partial class Order
            {
                public int Id { get; set; }
            }
            public partial class Order
            {
                public int CustomerId { get; set; }
            }
            """);

        var snapshot = ClassSnapshots.Read(root).Single();

        Assert.AreEqual("Order", snapshot.Name);
        Assert.Contains("Id", snapshot.Properties.Select(p => p.Name).ToList());
        Assert.Contains("CustomerId", snapshot.Properties.Select(p => p.Name).ToList());
    }

    /// <summary>
    /// A test project's classes are not the app's classes.
    /// </summary>
    /// <remarks>
    /// A generated app carries an xunit project, and the relationship offer proposed pairs out of
    /// it: "RepositoryRoundTripTests and Repository look related". <c>show relationships</c> listed
    /// RepositoryRoundTripTests and SeedDataTests beside the entities.
    /// </remarks>
    [TestMethod]
    public void Classes_in_a_test_project_are_left_out()
    {
        var root = NewRoot();
        var app = Directory.CreateDirectory(Path.Combine(root, "Shop")).FullName;
        var tests = Directory.CreateDirectory(Path.Combine(root, "Shop.Tests")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(tests, "Data")).FullName;

        Write(app, "Shop.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Write(app, "Customer.cs", "public class Customer { public int Id { get; set; } }");
        Write(tests, "Shop.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
              </ItemGroup>
            </Project>
            """);
        Write(tests, "SeedDataTests.cs", "public class SeedDataTests { public int Count { get; set; } }");
        Write(nested, "RepositoryRoundTripTests.cs",
            "public class RepositoryRoundTripTests { public int RepositoryId { get; set; } }");

        var names = ClassSnapshots.Read(root).Select(s => s.Name).ToList();

        CollectionAssert.AreEquivalent(new[] { "Customer" }, names);
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
