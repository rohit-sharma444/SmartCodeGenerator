using CodeGenerator.App.Relationships;

namespace CodeGenerator.Tests.App.Relationships;

/// <summary>
/// A piped run must never be offered anything.
/// </summary>
/// <remarks>
/// This is the mechanism by which every existing scripted test stays green without being edited:
/// with no asker there is no conversation, so a script's next line is never swallowed as an answer
/// to a menu it could not see.
/// </remarks>
[TestClass]
public class ConsoleRelationshipGateTests
{
    [TestMethod]
    public void No_asker_means_no_flow_and_no_writes()
    {
        var root = Path.Combine(Path.GetTempPath(), "gate-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        File.WriteAllText(Path.Combine(root, "Order.cs"),
            "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n    public int CustomerId { get; set; }\n}\n");
        File.WriteAllText(Path.Combine(root, "Customer.cs"),
            "namespace Shop;\n\npublic class Customer\n{\n    public int Id { get; set; }\n}\n");

        var before = File.ReadAllText(Path.Combine(root, "Order.cs"));

        // What the REPL does when input is redirected: it builds no flow at all.
        var flow = RelationshipGate.For(() => root, asker: null, report: _ => { });

        Assert.IsNull(flow, "a redirected run must not get a flow");
        Assert.AreEqual(before, File.ReadAllText(Path.Combine(root, "Order.cs")));
    }

    [TestMethod]
    public void An_asker_means_a_flow()
        => Assert.IsNotNull(RelationshipGate.For(
            () => Path.GetTempPath(), new NeverAsks(), _ => { }));

    [TestMethod]
    [DataRow("create class Order")]
    [DataRow("create a class Order with properties Name string")]
    [DataRow("Add Class Customer")]
    [DataRow("new class Invoice")]
    [DataRow("generate class Invoice")]
    public void A_class_creation_is_recognised_so_a_run_of_them_is_not_interrupted(string input)
        => Assert.IsTrue(RelationshipGate.IsClassCreation(input), input);

    [TestMethod]
    [DataRow("list classes")]
    [DataRow("show class Order")]
    [DataRow("")]
    [DataRow("create")]
    public void Anything_else_is_a_moment_the_offer_may_appear(string input)
        => Assert.IsFalse(RelationshipGate.IsClassCreation(input), input);

    [TestMethod]
    public void Adding_a_property_is_not_a_class_creation()
    {
        // The one that matters: `add property CustomerId int to class Order` is the command most
        // likely to be the moment a relationship becomes detectable. A predicate that matched a
        // creation verb anywhere alongside the word "class" would silence the offer exactly then.
        Assert.IsFalse(RelationshipGate.IsClassCreation("add property CustomerId int to class Order"));
    }

    private sealed class NeverAsks : IChoiceAsker
    {
        public int? Choose(string title, IReadOnlyList<string> options, int defaultIndex) => null;
    }
}
