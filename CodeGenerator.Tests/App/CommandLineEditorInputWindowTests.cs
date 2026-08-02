using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Covers the width contract for the input row.
///
/// The editor owns exactly one row for the prompt and the command being typed; the rows
/// below it belong to the suggestion list, and the clear loop only blanks rows it counted.
/// A command that reaches the console width wraps onto the first suggestion row, which is
/// then drawn over while typing and blanked on Enter — so the tail of the command is lost
/// both on screen and in the scrollback. The row therefore has to be a sideways-scrolling
/// window onto the text, never a wrap.
/// </summary>
[TestClass]
public class CommandLineEditorInputWindowTests
{
    // 17 columns of "CodeGenerator >> " leave 62 on an 80-column console.
    private const int Available = 62;

    private const string LongCommand =
        "build app: a library management system with members and loans from D:\\data\\books.csv";

    [TestMethod]
    public void ClipInputToWindow_TextFitsTheRow_IsShownWhole()
    {
        var (text, column) = CommandLineEditor.ClipInputToWindow("show classes", 6, Available);

        Assert.AreEqual("show classes", text);
        Assert.AreEqual(6, column);
    }

    [TestMethod]
    public void ClipInputToWindow_TextExactlyFillsTheRow_IsShownWhole()
    {
        var exact = new string('x', Available);
        var (text, column) = CommandLineEditor.ClipInputToWindow(exact, Available, Available);

        Assert.AreEqual(exact, text);
        Assert.AreEqual(Available, column);
    }

    [TestMethod]
    public void ClipInputToWindow_CaretAtTheEndOfALongCommand_ShowsTheEnd()
    {
        var (text, column) =
            CommandLineEditor.ClipInputToWindow(LongCommand, LongCommand.Length, Available);

        Assert.IsLessThanOrEqualTo(Available, text.Length);
        Assert.IsTrue(
            LongCommand.EndsWith(text, StringComparison.Ordinal),
            $"the window should be the tail of the command, was '{text}'");
        Assert.AreEqual(text.Length, column, "the caret sits just past the last visible character");
    }

    [TestMethod]
    public void ClipInputToWindow_CaretNearTheStartOfALongCommand_ShowsTheStart()
    {
        var (text, column) = CommandLineEditor.ClipInputToWindow(LongCommand, 3, Available);

        Assert.AreEqual(LongCommand[..Available], text);
        Assert.AreEqual(3, column);
    }

    [TestMethod]
    public void ClipInputToWindow_EveryCaretPositionInALongCommand_StaysOnTheRow()
    {
        // The invariant the drawing depends on: the window never reaches the row width, and
        // the caret always lands on the character it is actually in front of.
        for (var caret = 0; caret <= LongCommand.Length; caret++)
        {
            var (text, column) =
                CommandLineEditor.ClipInputToWindow(LongCommand, caret, Available);

            Assert.IsLessThanOrEqualTo(
                Available, text.Length, $"window overflowed the row with the caret at {caret}");
            Assert.IsLessThanOrEqualTo(
                Available, column, $"caret left the row at {caret}");
            Assert.IsGreaterThanOrEqualTo(0, column, $"caret went negative at {caret}");

            if (caret < LongCommand.Length)
            {
                Assert.IsLessThan(
                    text.Length, column, $"caret {caret} is not on the visible slice");
                Assert.AreEqual(
                    LongCommand[caret], text[column], $"caret {caret} points at the wrong character");
            }
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(-20)]
    public void ClipInputToWindow_NoRoomLeftOnTheRow_RendersNothing(int available)
    {
        // A console narrower than the prompt itself must not throw — the editor redraws on
        // every keystroke.
        var (text, column) = CommandLineEditor.ClipInputToWindow(LongCommand, 40, available);

        Assert.AreEqual(string.Empty, text);
        Assert.AreEqual(0, column);
    }

    [TestMethod]
    public void ClipInputToWindow_CaretPastTheEndOfTheText_IsClamped()
    {
        var (text, column) = CommandLineEditor.ClipInputToWindow("abc", 99, Available);

        Assert.AreEqual("abc", text);
        Assert.AreEqual(3, column);
    }
}
