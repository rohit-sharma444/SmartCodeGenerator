using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// When long output should stop for breath.
/// </summary>
/// <remarks>
/// <c>help all</c> is around 250 lines and a large candidate model can be dozens; both used to
/// scroll straight past the top of the screen.
/// </remarks>
[TestClass]
public class PagerTests
{
    private const int Window = 30;

    [TestMethod]
    public void ShortOutputIsNeverPaged()
    {
        // Interrupting output that already fits would be pure friction.
        Assert.IsFalse(Pager.ShouldPage(lineCount: 5, Window, redirected: false));
        Assert.IsFalse(Pager.ShouldPage(lineCount: Window - 2, Window, redirected: false));
    }

    [TestMethod]
    public void OutputTallerThanTheWindowIsPaged()
    {
        Assert.IsTrue(Pager.ShouldPage(lineCount: 250, Window, redirected: false));
    }

    [TestMethod]
    public void RedirectedOutputIsNeverPaged()
    {
        // A pager waiting for a key press in a pipe is a hang, and a paged transcript is not the
        // text the caller asked for. The same gate colour and the prompts use.
        Assert.IsFalse(Pager.ShouldPage(lineCount: 250, Window, redirected: true));
    }

    [TestMethod]
    public void AnUnknownWindowHeightIsNeverPaged()
    {
        // Console.WindowHeight can be 0 or throw with no console attached. Guessing a height and
        // pausing on it would hang a host that cannot answer a key press either.
        Assert.IsFalse(Pager.ShouldPage(lineCount: 250, windowHeight: 0, redirected: false));
        Assert.IsFalse(Pager.ShouldPage(lineCount: 250, windowHeight: -1, redirected: false));
    }

    [TestMethod]
    public void APageLeavesRoomForThePromptAndTheMoreLine()
    {
        // A page exactly the window's height scrolls the first line away as the prompt is drawn,
        // which defeats the point.
        Assert.IsLessThan(Window, Pager.PageSize(Window));
        Assert.IsGreaterThan(0, Pager.PageSize(Window));
    }

    [TestMethod]
    public void APageIsNeverZeroOrNegativeOnATinyWindow()
    {
        // A one-line page is slow; a zero-line page is an infinite loop.
        foreach (var height in new[] { 1, 2, 3, 4 })
            Assert.IsGreaterThan(0, Pager.PageSize(height), $"height {height}");
    }

    [TestMethod]
    public void StopIsRecognizedHoweverItIsTyped()
    {
        Assert.IsTrue(Pager.ShouldStop("q"));
        Assert.IsTrue(Pager.ShouldStop("Q"));
        Assert.IsTrue(Pager.ShouldStop("quit"));

        // EOF: nothing more is coming, so stop rather than loop.
        Assert.IsTrue(Pager.ShouldStop(null));
    }

    [TestMethod]
    public void EnterContinues()
    {
        Assert.IsFalse(Pager.ShouldStop(""));
        Assert.IsFalse(Pager.ShouldStop("   "));
        Assert.IsFalse(Pager.ShouldStop("anything else"));
    }
}
