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

    [TestMethod]
    public void A_class_alongside_a_record_in_one_file_contributes_no_snapshot()
    {
        // EntityExtractor.ExtractProperties iterates every TypeDeclarationSyntax, and
        // RecordDeclarationSyntax is a sibling of ClassDeclarationSyntax under that base, not a
        // subtype of it - so a record sharing a file with a class is exactly as much a
        // misattribution risk as a second class, even though there is only one
        // ClassDeclarationSyntax in the file.
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

        Assert.IsEmpty(ClassSnapshots.Read(root));
    }

    [TestMethod]
    public void A_class_alongside_a_struct_in_one_file_contributes_no_snapshot()
    {
        // StructDeclarationSyntax is also a TypeDeclarationSyntax that
        // EntityExtractor.ExtractProperties merges over, so the same rule applies as for a record.
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

        Assert.IsEmpty(ClassSnapshots.Read(root));
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
