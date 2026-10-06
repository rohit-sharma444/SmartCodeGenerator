using Bunit;
using CodeGenerator.Web.Components;
using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// The diagram as the review page draws it.
/// </summary>
/// <remarks>
/// The coordinates are tested where they are computed. What is tested here is that every box and
/// every arrow the layout produced actually reaches the markup — a rendering loop that quietly
/// drops one is invisible to a layout test and, on a picture, invisible to the eye as well.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class RelationshipDiagramTests
{
    [TestMethod]
    public void Every_class_is_a_box_and_every_relationship_an_arrow()
    {
        using var context = new BunitContext();

        var page = context.Render<RelationshipDiagram>(p => p.Add(
            c => c.Sources,
            [Entity("Record", "Inventory"), Entity("Sale", "Inventory"), Entity("Inventory")]));

        Assert.HasCount(3, page.FindAll(".rel-box"));
        Assert.HasCount(2, page.FindAll(".rel-edge"));
    }

    [TestMethod]
    public void A_class_related_to_nothing_is_drawn_apart_and_named_in_words()
    {
        using var context = new BunitContext();

        var page = context.Render<RelationshipDiagram>(p => p.Add(
            c => c.Sources,
            [Entity("Order", "Customer"), Entity("Customer"), Entity("Bill")]));

        Assert.HasCount(1, page.FindAll(".rel-box-alone"));

        // The sentence matters as much as the dashed box: it is what tells someone scrolling past
        // that a class they expected to be joined up is not.
        StringAssert.Contains(page.Find(".rel-note").TextContent, "Bill");

        // "1 of 3 class is" agreed the noun with the one, not with the three.
        var note = System.Text.RegularExpressions.Regex.Replace(page.Find(".rel-note").TextContent, "[ \t\r\n]+", " ");
        StringAssert.Contains(note, "1 of 3 classes is related to nothing");
    }

    [TestMethod]
    public void A_model_with_nothing_related_says_so_rather_than_drawing_loose_boxes()
    {
        using var context = new BunitContext();

        var page = context.Render<RelationshipDiagram>(p => p.Add(
            c => c.Sources, [Entity("Bill"), Entity("User")]));

        Assert.IsEmpty(page.FindAll(".rel-edge"));
        StringAssert.Contains(page.Find(".rel-empty").TextContent, "Nothing is related yet");

        // The same diagram draws classes already on disk (show relationships), so it must not
        // promise they "will be generated".
        Assert.DoesNotContain("generated", page.Find(".rel-empty").TextContent);
    }

    [TestMethod]
    public void An_empty_model_draws_no_diagram_at_all()
    {
        using var context = new BunitContext();

        var page = context.Render<RelationshipDiagram>(p => p.Add(c => c.Sources, []));

        Assert.IsEmpty(page.FindAll("svg"));
        StringAssert.Contains(page.Find(".rel-empty").TextContent, "No classes yet");
    }

    [TestMethod]
    public void The_picture_carries_a_label_for_a_reader_that_cannot_see_it()
    {
        using var context = new BunitContext();

        var page = context.Render<RelationshipDiagram>(p => p.Add(
            c => c.Sources, [Entity("Order", "Customer"), Entity("Customer")]));

        // .rel-svg, not "svg": the legend beside the heading is an svg too, and it is the first
        // one in the markup. A bare selector silently measured the wrong element.
        var label = page.Find(".rel-svg").GetAttribute("aria-label");

        Assert.IsNotNull(label);
        StringAssert.Contains(label, "Order to Customer");
    }

    [TestMethod]
    public void A_long_class_name_is_shortened_in_the_box_but_kept_in_full_on_hover()
    {
        // The box is a fixed width because that is what makes the arrow coordinates predictable.
        // A name that would overflow it is cut, so the title is the only place the whole name
        // still exists.
        using var context = new BunitContext();

        var page = context.Render<RelationshipDiagram>(p => p.Add(
            c => c.Sources, [Entity("InternationalShippingManifest", "Order"), Entity("Order")]));

        // The drawn label and the hover title are different elements on purpose, so the check has
        // to name them: asserting on the raw markup matches the title as readily as the text and
        // passes whether or not anything was ever truncated.
        var drawn = page.Find(".rel-box text").TextContent;
        var hover = page.Find(".rel-box title").TextContent;

        Assert.AreNotEqual("InternationalShippingManifest", drawn, "the box label is cut to fit");
        StringAssert.EndsWith(drawn, "…");
        Assert.AreEqual("InternationalShippingManifest", hover, "the whole name survives on hover");
    }

    private static RelationshipSource Entity(string name, params string[] targets)
        => new(name, targets);
}
