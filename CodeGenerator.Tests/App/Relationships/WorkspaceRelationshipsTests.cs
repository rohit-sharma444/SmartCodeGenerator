using CodeGenerator.App.Relationships;
using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Tests.App.Relationships;

/// <summary>
/// The classes on disk, in the shape the diagram draws.
/// </summary>
/// <remarks>
/// Read through <c>ForeignKeys.TargetsOf</c> rather than by looking for an id or a navigation
/// property here. That is the one definition of a relationship the seeder, the generated
/// validators, the console picker and the screen split all use, so a picture built from it cannot
/// claim two classes are related when nothing else in the tool agrees.
/// </remarks>
[TestClass]
public class WorkspaceRelationshipsTests
{
    [TestMethod]
    public void A_complete_relationship_is_read_from_the_files()
    {
        var root = NewRoot();
        Write(root, "Order.cs", """
            namespace Shop;
            public class Order
            {
                public int Id { get; set; }
                public int CustomerId { get; set; }
                public Customer Customer { get; set; }
            }
            """);
        Write(root, "Customer.cs", "namespace Shop; public class Customer { public int Id { get; set; } }");

        var sources = WorkspaceRelationships.Of(root);

        CollectionAssert.AreEquivalent(
            new[] { "Order", "Customer" }, sources.Select(s => s.Name).ToList());

        CollectionAssert.AreEqual(
            new[] { "Customer" },
            sources.Single(s => s.Name == "Order").Targets.ToList());

        Assert.IsEmpty(sources.Single(s => s.Name == "Customer").Targets);
    }

    [TestMethod]
    public void A_foreign_key_with_no_navigation_property_is_not_a_relationship()
    {
        // Deliberately shown as unrelated. Half a relationship is invisible to everything
        // downstream, so a diagram that drew it would be the only place in the tool claiming the
        // two are joined - and it is exactly the case the relationship offer exists to finish.
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

        Assert.IsEmpty(WorkspaceRelationships.Of(root).SelectMany(s => s.Targets).ToList());
    }

    [TestMethod]
    public void A_nullable_foreign_key_is_not_a_relationship_either()
    {
        // TargetsOf requires exactly `int`. A picture that accepted `int?` would disagree with
        // every consumer of it.
        var root = NewRoot();
        Write(root, "Order.cs", """
            namespace Shop;
            public class Order
            {
                public int Id { get; set; }
                public int? CustomerId { get; set; }
                public Customer Customer { get; set; }
            }
            """);
        Write(root, "Customer.cs", "namespace Shop; public class Customer { public int Id { get; set; } }");

        Assert.IsEmpty(WorkspaceRelationships.Of(root).SelectMany(s => s.Targets).ToList());
    }

    [TestMethod]
    public void Keys_named_for_a_role_are_read_one_by_one()
    {
        // The shape an import from a database writes: two keys from Payment to Staff, and Staff's
        // own ManagerId. TargetsOf sees none of them; the validators, the delete refusal and the
        // seeder act on all three.
        var root = NewRoot();
        Write(root, "Payment.cs", """
            namespace Shop;
            public class Payment
            {
                public int Id { get; set; }
                public int PaidById { get; set; }
                public Staff PaidBy { get; set; }
                public int ApprovedById { get; set; }
                public Staff ApprovedBy { get; set; }
            }
            """);
        Write(root, "Staff.cs", """
            namespace Shop;
            public class Staff
            {
                public int Id { get; set; }
                public int ManagerId { get; set; }
                public Staff Manager { get; set; }
            }
            """);

        var sources = WorkspaceRelationships.Of(root);

        CollectionAssert.AreEquivalent(
            new[] { new RelationshipLink("PaidById", "Staff"), new RelationshipLink("ApprovedById", "Staff") },
            sources.Single(s => s.Name == "Payment").Keys.ToList());
        CollectionAssert.AreEqual(
            new[] { new RelationshipLink("ManagerId", "Staff") },
            sources.Single(s => s.Name == "Staff").Keys.ToList());
    }

    [TestMethod]
    public void A_workspace_with_no_classes_reads_as_nothing()
        => Assert.IsEmpty(WorkspaceRelationships.Of(NewRoot()));

    [TestMethod]
    public void The_classes_come_back_in_a_settled_order()
    {
        var root = NewRoot();
        Write(root, "Zebra.cs", "namespace Shop; public class Zebra { public int Id { get; set; } }");
        Write(root, "Apple.cs", "namespace Shop; public class Apple { public int Id { get; set; } }");

        CollectionAssert.AreEqual(
            new[] { "Apple", "Zebra" },
            WorkspaceRelationships.Of(root).Select(s => s.Name).ToList());
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "wr-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string directory, string name, string code)
        => File.WriteAllText(Path.Combine(directory, name), code);
}
