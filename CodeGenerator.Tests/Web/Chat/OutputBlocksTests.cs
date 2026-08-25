using CodeGenerator.Web.Chat;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// Turning a command's captured output into blocks the page can render properly.
/// </summary>
/// <remarks>
/// Three shapes are recognised, each read out of the handler that prints it rather than assumed.
/// The other half of every case below is the one that matters more: output one line off its
/// expected shape comes back as plain text. Nothing here is allowed to guess.
/// </remarks>
[TestClass]
public class OutputBlocksTests
{
    [TestMethod]
    public void Show_class_becomes_a_code_block()
    {
        var text = "\r\n----- Order -----\r\n\r\npublic class Order\r\n{\r\n}\r\n\r\n";

        var blocks = OutputBlocks.Parse("show class Order", text);

        Assert.HasCount(1, blocks);
        var code = (CodeBlock)blocks[0];
        Assert.AreEqual("Order", code.Heading);
        StringAssert.StartsWith(code.Code, "public class Order");
        Assert.DoesNotContain("-----", code.Code);
    }

    [TestMethod]
    public void Show_class_that_did_not_find_the_class_stays_text()
    {
        // The failure path prints a warning and no header. There is no code to show.
        var blocks = OutputBlocks.Parse("show class Nope", "Class 'Nope' not found.\r\n");

        Assert.IsInstanceOfType<TextBlock>(blocks[0]);
    }

    [TestMethod]
    public void List_properties_becomes_a_table()
    {
        var text = "Properties in Order:\r\n  int Id\r\n  string Reference\r\n";

        var blocks = OutputBlocks.Parse("list properties of Order", text);

        var table = (TableBlock)blocks[0];
        Assert.AreEqual("Properties in Order", table.Heading);
        Assert.HasCount(2, table.Headers);
        Assert.AreEqual("Type", table.Headers[0]);
        Assert.AreEqual("Name", table.Headers[1]);
        Assert.HasCount(2, table.Rows);
        Assert.AreEqual("int", table.Rows[0][0]);
        Assert.AreEqual("Id", table.Rows[0][1]);
    }

    [TestMethod]
    public void List_properties_with_no_properties_stays_text()
    {
        var blocks = OutputBlocks.Parse(
            "list properties of Order", "No properties found in 'Order'.\r\n");

        Assert.IsInstanceOfType<TextBlock>(blocks[0]);
    }

    [TestMethod]
    public void A_property_line_of_the_wrong_shape_makes_the_whole_thing_text()
    {
        // One line without the two-space indent, or with three words, and the table would be
        // wrong in a way the user cannot see. Fall back rather than guess.
        var text = "Properties in Order:\r\n  int Id\r\nsomething else entirely\r\n";

        Assert.IsInstanceOfType<TextBlock>(
            OutputBlocks.Parse("list properties of Order", text)[0]);
    }

    [TestMethod]
    public void List_classes_becomes_a_list()
    {
        var text = "\r\nClasses in Shop\r\n--------------------------------\r\nCustomer\r\nOrder\r\n\r\n";

        var blocks = OutputBlocks.Parse("list classes", text);

        var list = (ListBlock)blocks[0];
        Assert.AreEqual("Classes in Shop", list.Heading);
        Assert.HasCount(2, list.Items);
        Assert.Contains("Order", list.Items);
    }

    [TestMethod]
    public void List_classes_with_none_found_stays_text()
    {
        Assert.IsInstanceOfType<TextBlock>(
            OutputBlocks.Parse("list classes", "No classes found.\r\n")[0]);
    }

    [TestMethod]
    public void List_classes_without_the_rule_of_dashes_stays_text()
    {
        var text = "\r\nClasses in Shop\r\nCustomer\r\nOrder\r\n";

        Assert.IsInstanceOfType<TextBlock>(OutputBlocks.Parse("list classes", text)[0]);
    }

    [TestMethod]
    public void Anything_unrecognised_is_one_text_block()
    {
        foreach (var input in new[] { "status", "help", "validate project", "show validation Order" })
        {
            var blocks = OutputBlocks.Parse(input, "whatever it printed\r\n");

            Assert.HasCount(1, blocks);
            Assert.IsInstanceOfType<TextBlock>(blocks[0]);
        }
    }

    [TestMethod]
    public void Empty_output_is_no_blocks()
    {
        Assert.IsEmpty(OutputBlocks.Parse("list classes", ""));
        Assert.IsEmpty(OutputBlocks.Parse("list classes", "   \r\n"));
    }
}
