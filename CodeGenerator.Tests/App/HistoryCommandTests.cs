using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class HistoryCommandTests
{
    [TestMethod]
    [DataRow("history")]
    [DataRow("HISTORY")]
    [DataRow("  history  ")]
    public void IsHistory_RecognizesHistory(string input)
        => Assert.IsTrue(HistoryCommand.IsHistory(input));

    [TestMethod]
    [DataRow("")]
    [DataRow("histories")]
    [DataRow("history all")]
    [DataRow("show history")]
    public void IsHistory_RejectsEverythingElse(string input)
        => Assert.IsFalse(HistoryCommand.IsHistory(input));
}
