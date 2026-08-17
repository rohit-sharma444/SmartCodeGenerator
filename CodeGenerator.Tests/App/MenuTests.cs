using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The one place a list of options becomes a chosen row. Painting it is
/// <see cref="SelectionPrompt"/>'s job and needs a real console; the fallback every redirected run
/// gets is here, and is what these tests drive.
/// </summary>
[TestClass]
public class MenuTests
{
    private static readonly string[] Colours = ["Red", "Green", "Blue"];

    [TestMethod]
    public void ChooseNumbered_ReturnsTheIndexTyped()
    {
        // Users count from one; the caller indexes from zero.
        Assert.AreEqual(
            1,
            Menu.ChooseNumbered(new StringReader("2\n"), new StringWriter(), "Colour", Colours, 0));
    }

    [TestMethod]
    public void ChooseNumbered_ListsTheTitleAndEveryOptionWithItsNumber()
    {
        var writer = new StringWriter();

        Menu.ChooseNumbered(new StringReader("1\n"), writer, "Colour", Colours, 0);

        var written = writer.ToString();

        StringAssert.Contains(written, "Colour:");
        StringAssert.Contains(written, "1) Red");
        StringAssert.Contains(written, "3) Blue");
        StringAssert.Contains(written, "[1-3]");
    }

    [TestMethod]
    public void ChooseNumbered_BlankTakesTheDefaultRow_AndSaysWhichItIs()
    {
        var writer = new StringWriter();

        // The default is the value the user already has, so Enter must keep it here exactly as it
        // does on the arrow-key list — otherwise a redirected run silently resets every answer.
        Assert.AreEqual(
            2, Menu.ChooseNumbered(new StringReader("\n"), writer, "Colour", Colours, 2));

        StringAssert.Contains(writer.ToString(), "default 3");
    }

    [TestMethod]
    public void ChooseNumbered_ReAsksOnSomethingOutOfRange()
    {
        var writer = new StringWriter();

        var chosen = Menu.ChooseNumbered(
            new StringReader("9\nzero\n3\n"), writer, "Colour", Colours, 0);

        Assert.AreEqual(2, chosen);
        StringAssert.Contains(writer.ToString(), "Choose a number between 1 and 3");
    }

    [TestMethod]
    public void ChooseNumbered_CancelsOnCAndOnEndOfInput()
    {
        Assert.IsNull(Menu.ChooseNumbered(
            new StringReader("c\n"), new StringWriter(), "Colour", Colours, 0));

        // EOF is the absence of an answer, not the default answer — the rule the rest of the
        // prompts already apply.
        Assert.IsNull(Menu.ChooseNumbered(
            new StringReader(""), new StringWriter(), "Colour", Colours, 0));
    }

    [TestMethod]
    public void ChooseNumbered_ClampsADefaultThatIsNotARow()
    {
        // A caller computing a default from a value that has since gone (a layout no longer
        // offered, say) must not be told its answer is row 9 of 3.
        Assert.AreEqual(
            0, Menu.ChooseNumbered(new StringReader("\n"), new StringWriter(), "C", Colours, 9));

        Assert.AreEqual(
            0, Menu.ChooseNumbered(new StringReader("\n"), new StringWriter(), "C", Colours, -1));
    }
}
