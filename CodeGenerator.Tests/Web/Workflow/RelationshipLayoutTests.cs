using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Tests.Web.Workflow;

/// <summary>
/// Where the boxes and arrows go, worked out without a browser.
/// </summary>
/// <remarks>
/// The layout is pure so that the hard part — layering, cycles, which classes are connected to
/// nothing — is testable as arithmetic rather than as pixels. What a browser is still needed for
/// is whether the result is legible, which no assertion can tell you.
/// </remarks>
[TestClass]
public class RelationshipLayoutTests
{
    [TestMethod]
    public void A_class_that_points_at_another_is_drawn_to_its_left()
    {
        var layout = RelationshipLayout.Of([Entity("Order", "Customer"), Entity("Customer")]);

        var order = layout.Boxes.Single(b => b.Name == "Order");
        var customer = layout.Boxes.Single(b => b.Name == "Customer");

        Assert.IsLessThan(customer.X, order.X, "the class holding the foreign key is on the left");
        Assert.HasCount(1, layout.Arrows);
        Assert.AreEqual("Order", layout.Arrows[0].From);
        Assert.AreEqual("Customer", layout.Arrows[0].To);
    }

    [TestMethod]
    public void A_chain_gets_one_column_per_step()
    {
        // Line -> Order -> Customer is three layers, not two: layering takes the LONGEST path to
        // a class, so an arrow never points backwards or straight down a column.
        var layout = RelationshipLayout.Of(
            [Entity("Line", "Order"), Entity("Order", "Customer"), Entity("Customer")]);

        var x = layout.Boxes.ToDictionary(b => b.Name, b => b.X);

        Assert.IsLessThan(x["Order"], x["Line"]);
        Assert.IsLessThan(x["Customer"], x["Order"]);
    }

    [TestMethod]
    public void Several_classes_pointing_at_one_share_its_column_not_its_row()
    {
        var layout = RelationshipLayout.Of(
        [
            Entity("Record", "Inventory"),
            Entity("Retail", "Inventory"),
            Entity("Sale", "Inventory"),
            Entity("Inventory")
        ]);

        var sources = layout.Boxes.Where(b => b.Name != "Inventory").ToList();

        Assert.HasCount(3, sources);
        Assert.HasCount(1, sources.Select(b => b.X).Distinct().ToList());
        Assert.HasCount(3, sources.Select(b => b.Y).Distinct().ToList());
        Assert.HasCount(3, layout.Arrows);
    }

    [TestMethod]
    public void A_class_related_to_nothing_goes_in_the_unrelated_band()
    {
        var layout = RelationshipLayout.Of(
            [Entity("Order", "Customer"), Entity("Customer"), Entity("Bill"), Entity("User")]);

        CollectionAssert.AreEquivalent(
            new[] { "Bill", "User" },
            layout.Unrelated.Select(b => b.Name).ToList());

        Assert.IsFalse(
            layout.Boxes.Any(b => b.Name is "Bill" or "User"),
            "an unrelated class is drawn in the band, not in the graph");
    }

    [TestMethod]
    public void A_cycle_still_produces_a_layout()
    {
        // Two classes pointing at each other has no valid layering. Dropping the back edge for
        // layering ONLY - it is still drawn - is what stops this looping forever or throwing on a
        // model the miner is perfectly capable of producing.
        var layout = RelationshipLayout.Of([Entity("A", "B"), Entity("B", "A")]);

        Assert.HasCount(2, layout.Boxes);
        Assert.HasCount(2, layout.Arrows);
        Assert.IsTrue(layout.Boxes.All(b => b.X >= 0 && b.Y >= 0));
    }

    [TestMethod]
    public void A_class_pointing_at_itself_is_not_an_arrow()
    {
        // Self-reference has nowhere to go on a boxes-and-arrows picture, and DomainModelMapper
        // would not write it either: RelationshipConvention.CanAdd refuses a member named after
        // its own class.
        var layout = RelationshipLayout.Of([Entity("Node", "Node")]);

        Assert.IsEmpty(layout.Arrows);
        CollectionAssert.AreEquivalent(new[] { "Node" }, layout.Unrelated.Select(b => b.Name).ToList());
    }

    [TestMethod]
    public void A_relationship_to_a_class_that_is_not_in_the_model_is_dropped()
    {
        // The same rule DomainModelMapper applies: a target it cannot resolve is skipped, so the
        // picture must not show an arrow to a box that will not exist.
        var layout = RelationshipLayout.Of([Entity("Order", "Ghost"), Entity("Customer")]);

        Assert.IsEmpty(layout.Arrows);
        CollectionAssert.AreEquivalent(
            new[] { "Order", "Customer" },
            layout.Unrelated.Select(b => b.Name).ToList());
    }

    [TestMethod]
    public void The_same_pair_twice_is_one_arrow()
    {
        var layout = RelationshipLayout.Of([Entity("Order", "Customer", "Customer"), Entity("Customer")]);

        Assert.HasCount(1, layout.Arrows);
    }

    [TestMethod]
    public void A_target_named_in_another_case_still_matches()
    {
        // The miner and the entity list do not agree about casing, and DomainModelMapper resolves
        // its targets case-insensitively. A picture that dropped the arrow would disagree with the
        // code that is about to be written.
        var layout = RelationshipLayout.Of([Entity("Order", "customer"), Entity("Customer")]);

        Assert.HasCount(1, layout.Arrows);
        Assert.AreEqual("Customer", layout.Arrows[0].To);
    }

    [TestMethod]
    public void The_canvas_is_big_enough_for_everything_in_it()
    {
        // The SVG is sized from this, so anything outside it is simply not drawn.
        var layout = RelationshipLayout.Of(
            [Entity("Record", "Inventory"), Entity("Retail", "Inventory"), Entity("Inventory"), Entity("Bill")]);

        foreach (var box in layout.Boxes.Concat(layout.Unrelated))
        {
            Assert.IsLessThanOrEqualTo(layout.Width, box.X + RelationshipLayout.BoxWidth, box.Name);
            Assert.IsLessThanOrEqualTo(layout.Height, box.Y + RelationshipLayout.BoxHeight, box.Name);
        }
    }

    [TestMethod]
    public void An_empty_model_lays_out_to_nothing_rather_than_throwing()
    {
        var layout = RelationshipLayout.Of([]);

        Assert.IsEmpty(layout.Boxes);
        Assert.IsEmpty(layout.Arrows);
        Assert.IsEmpty(layout.Unrelated);
    }

    [TestMethod]
    public void The_layout_is_the_same_every_time_it_is_computed()
    {
        // Boxes are ordered by name within a column rather than by discovery order, so a model
        // that has not changed does not redraw itself differently after every edit on the page.
        RelationshipSource[] Model() =>
            [Entity("Sale", "Inventory"), Entity("Record", "Inventory"), Entity("Inventory")];

        var first = RelationshipLayout.Of(Model());
        var second = RelationshipLayout.Of(Model());

        CollectionAssert.AreEqual(
            first.Boxes.Select(b => $"{b.Name}:{b.X},{b.Y}").ToList(),
            second.Boxes.Select(b => $"{b.Name}:{b.X},{b.Y}").ToList());
    }

    [TestMethod]
    public void Arrows_crossing_the_same_gap_each_get_their_own_lane()
    {
        // Found by looking at the first screenshot, not by an assertion: with every elbow turning
        // at the middle of the gap, five arrows drew their vertical segments on top of each other
        // and became one line nobody could trace back to a source. Every box, every arrow and
        // every count was correct; the picture was still unreadable.
        var map = RelationshipLayout.Of(
        [
            Entity("Record", "Inventory"),
            Entity("Retail", "Inventory"),
            Entity("Sale", "Inventory"),
            Entity("Inventory")
        ]);

        var turns = map.Arrows.Select(a => a.Turn).ToList();

        Assert.HasCount(3, turns.Distinct().ToList(), "each arrow needs its own vertical segment");

        foreach (var arrow in map.Arrows)
        {
            Assert.IsGreaterThan(arrow.X1, arrow.Turn, "the turn is after the box it leaves");
            Assert.IsLessThan(arrow.X2, arrow.Turn, "and before the box it arrives at");
        }
    }

    [TestMethod]
    public void A_lone_arrow_turns_in_the_middle_of_the_gap()
    {
        var map = RelationshipLayout.Of([Entity("Order", "Customer"), Entity("Customer")]);
        var arrow = map.Arrows.Single();

        Assert.AreEqual(arrow.X1 + (arrow.X2 - arrow.X1) / 2, arrow.Turn);
    }

    private static RelationshipSource Entity(string name, params string[] targets)
        => new(name, targets);
}
