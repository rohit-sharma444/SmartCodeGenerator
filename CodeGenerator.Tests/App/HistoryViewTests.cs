using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class HistoryViewTests
{
    [TestMethod]
    public void Format_Empty_SaysSo()
        => StringAssert.Contains(HistoryView.Format([], 20), "No commands in history");

    [TestMethod]
    public void Format_NumbersEntriesOldestFirst()
    {
        var text = HistoryView.Format(["create project Shop", "use project Shop"], 20);
        StringAssert.Contains(text, "1");
        StringAssert.Contains(text, "create project Shop");
        StringAssert.Contains(text, "2");
        StringAssert.Contains(text, "use project Shop");
        Assert.IsTrue(
            text.IndexOf("create project Shop") < text.IndexOf("use project Shop"),
            "oldest entry should be listed first");
    }

    [TestMethod]
    public void Format_ShowsOnlyTheLastMaxEntries()
    {
        var entries = Enumerable.Range(1, 30).Select(i => $"cmd{i}").ToList();
        var text = HistoryView.Format(entries, 20);
        Assert.IsFalse(text.Contains("cmd10"), "older-than-window entries are omitted");
        StringAssert.Contains(text, "cmd11");
        StringAssert.Contains(text, "cmd30");
    }
}
