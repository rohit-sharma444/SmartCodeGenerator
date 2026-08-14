using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>Recognizing the <c>status</c> command.</summary>
[TestClass]
public class StatusCommandTests
{
    [TestMethod]
    public void StatusIsRecognized()
    {
        Assert.IsTrue(StatusCommand.IsStatus("status"));
        Assert.IsTrue(StatusCommand.IsStatus("  STATUS  "));
    }

    [TestMethod]
    public void WordsMerelyContainingStatusAreNot()
    {
        // `show status` belongs to the recognition pipeline if it belongs anywhere; this guard is
        // for the bare word only, the same shape ClearCommand and HistoryCommand use.
        Assert.IsFalse(StatusCommand.IsStatus("statuses"));
        Assert.IsFalse(StatusCommand.IsStatus("status project"));
        Assert.IsFalse(StatusCommand.IsStatus("show status"));
        Assert.IsFalse(StatusCommand.IsStatus(""));
        Assert.IsFalse(StatusCommand.IsStatus(null));
    }
}
