using CodeGenerator.App;
using CodeGenerator.Web.Relationships;
using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Tests.App.Relationships;

/// <summary>
/// Recognising <c>show relationships</c>, and what it says when there is no browser.
/// </summary>
/// <remarks>
/// The page itself is drawn by the same component the review stage uses and is tested there. What
/// is specific to the command is the two things a test can reach: which inputs route to it, and
/// the console answer a piped run gets instead of a page.
/// </remarks>
[TestClass]
public class ShowRelationshipsCommandTests
{
    [TestMethod]
    [DataRow("show relationships")]
    [DataRow("show relationship")]
    [DataRow("list relationships")]
    [DataRow("relationships")]
    [DataRow("  Show Relationships  ")]
    [DataRow("show relationships?")]
    public void The_spellings_people_type_all_route_to_the_page(string input)
    {
        Assert.IsTrue(ShowRelationshipsCommand.IsShowRelationships(input), input);
        Assert.AreEqual(CommandKind.ShowRelationships, CommandRoute.Classify(input), input);
    }

    [TestMethod]
    [DataRow("show class Order")]
    [DataRow("show relationships between Order and Customer")]
    [DataRow("")]
    [DataRow("chat")]
    public void Anything_else_is_left_to_the_pipeline(string input)
    {
        Assert.IsFalse(ShowRelationshipsCommand.IsShowRelationships(input), input);
        Assert.AreNotEqual(CommandKind.ShowRelationships, CommandRoute.Classify(input), input);
    }

    [TestMethod]
    public void The_console_answer_names_each_relationship_and_what_is_joined_to_nothing()
    {
        // The piped path. Unlike `chat`, which has no non-visual form and refuses, a set of
        // relationships reads perfectly well as lines - so a scripted run is answered rather than
        // turned away.
        var view = new RelationshipView("D:\\ws",
        [
            new RelationshipSource("Sale", ["Inventory", "Good"]),
            new RelationshipSource("Inventory", []),
            new RelationshipSource("Good", []),
            new RelationshipSource("Bill", [])
        ]);

        var lines = ShowRelationshipsCommand.Lines(view);

        Assert.IsTrue(lines.Any(l => l.Contains("Sale -> Inventory, Good")), string.Join("|", lines));

        // Inventory and Good are targets, so they are related to something even with no targets of
        // their own. Bill is the only one joined to nothing, and listing the other two would be
        // wrong in the one line people will read fastest.
        var alone = lines.Single(l => l.Contains("Related to nothing"));

        StringAssert.Contains(alone, "Bill");
        Assert.DoesNotContain("Inventory", alone);
        Assert.DoesNotContain("Good", alone);
    }

    [TestMethod]
    public void A_key_named_for_a_role_is_named_beside_its_target()
    {
        var view = new RelationshipView("D:\\ws",
        [
            RelationshipSource.WithLinks("Payment",
                [new("PaidById", "Staff"), new("ApprovedById", "Staff"), new("CurrencyId", "Currency")], []),
            RelationshipSource.WithLinks("Staff", [new("ManagerId", "Staff")], []),
            new RelationshipSource("Currency", [])
        ]);

        var lines = ShowRelationshipsCommand.Lines(view);
        var all = string.Join("|", lines);

        Assert.IsTrue(lines.Any(l => l.Contains("Payment -> Staff (PaidById), Staff (ApprovedById), Currency")), all);
        Assert.IsTrue(lines.Any(l => l.Contains("Staff -> Staff (ManagerId)")), all);
        Assert.IsFalse(lines.Any(l => l.Contains("Related to nothing")), all);
    }

    [TestMethod]
    public void A_workspace_with_no_relationships_at_all_says_so()
    {
        var view = new RelationshipView("D:\\ws",
            [new RelationshipSource("Bill", []), new RelationshipSource("User", [])]);

        var lines = ShowRelationshipsCommand.Lines(view);

        Assert.IsTrue(
            lines.Any(l => l.Contains("Nothing is related to anything yet")),
            string.Join("|", lines));
    }
}
