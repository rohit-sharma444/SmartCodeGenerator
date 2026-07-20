using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Covers the history-persistence policy shared by the interactive
/// (<c>CommitCommand</c>) and redirected (<c>ReadRedirectedLine</c>) read paths.
/// </summary>
[TestClass]
public class CommandLineEditorHistoryTests
{
    [TestMethod]
    public void ShouldRecordInHistory_RealCommand_IsRecorded()
        => Assert.IsTrue(
            CommandLineEditor.ShouldRecordInHistory([], "create project Shop"));

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("\t")]
    public void ShouldRecordInHistory_BlankInput_IsNotRecorded(string command)
        => Assert.IsFalse(CommandLineEditor.ShouldRecordInHistory([], command));

    [TestMethod]
    public void ShouldRecordInHistory_ConsecutiveDuplicate_IsNotRecorded()
        => Assert.IsFalse(
            CommandLineEditor.ShouldRecordInHistory(
                ["use project Shop"], "use project Shop"));

    [TestMethod]
    [DataRow("clear")]
    [DataRow("cls")]
    [DataRow("CLEAR")]
    [DataRow("  cls  ")]
    public void ShouldRecordInHistory_ScreenClear_IsNotRecorded(string command)
    {
        // clear/cls only wipe the display; the spec requires history (↑/↓ recall
        // and .codegen_history) to look as though they were never typed, so after
        // a clear the ↑ arrow recalls the previous *real* command.
        Assert.IsFalse(CommandLineEditor.ShouldRecordInHistory([], command));
        Assert.IsFalse(
            CommandLineEditor.ShouldRecordInHistory(["create project Shop"], command));
    }
}
