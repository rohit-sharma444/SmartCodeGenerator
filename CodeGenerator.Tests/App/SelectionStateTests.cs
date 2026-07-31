using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class SelectionStateTests
{
    private static SelectionState ThreeItems() =>
        new(new[] { "First", "Second", "Third" });

    [TestMethod]
    public void StartsOnTheFirstItemByDefault()
    {
        Assert.AreEqual(0, ThreeItems().Selected);
    }

    [TestMethod]
    public void DownThenUp_ReturnsToWhereItStarted()
    {
        var state = ThreeItems();

        state.Move(ConsoleKey.DownArrow);
        Assert.AreEqual(1, state.Selected);

        state.Move(ConsoleKey.UpArrow);
        Assert.AreEqual(0, state.Selected);
    }

    [TestMethod]
    public void Up_ClampsAtTheTop()
    {
        var state = ThreeItems();

        state.Move(ConsoleKey.UpArrow);
        state.Move(ConsoleKey.UpArrow);

        // Not wrapping: a menu that jumps to the bottom when you overshoot the top reads as
        // a mis-press.
        Assert.AreEqual(0, state.Selected);
    }

    [TestMethod]
    public void Down_ClampsAtTheBottom()
    {
        var state = ThreeItems();

        for (var i = 0; i < 10; i++)
            state.Move(ConsoleKey.DownArrow);

        Assert.AreEqual(2, state.Selected);
    }

    [TestMethod]
    public void OtherKeys_ChangeNothing()
    {
        var state = ThreeItems();

        state.Move(ConsoleKey.LeftArrow);
        state.Move(ConsoleKey.Spacebar);

        Assert.AreEqual(0, state.Selected);
    }

    [TestMethod]
    public void Lines_MarkExactlyTheSelectedRow()
    {
        var state = ThreeItems();
        state.Move(ConsoleKey.DownArrow);

        var lines = state.Lines();

        Assert.HasCount(3, lines);
        Assert.AreEqual(1, lines.Count(l => l.Contains('❯')));
        StringAssert.Contains(lines[1], "❯ Second");

        // The unselected rows are padded to the same width, so the labels line up under the
        // marker instead of shifting left.
        Assert.AreEqual(lines[1].IndexOf("Second", StringComparison.Ordinal),
                        lines[0].IndexOf("First", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ASelectedIndexOutOfRange_IsClamped()
    {
        Assert.AreEqual(2, new SelectionState(new[] { "a", "b", "c" }, 99).Selected);
        Assert.AreEqual(0, new SelectionState(new[] { "a", "b", "c" }, -5).Selected);
    }
}
