using AppScaffolding.Targets;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ApplicationTypeMenuTests
{
    [TestMethod]
    public void Labels_AreTheSixKindsOfAppInOrder()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "WPF (Windows desktop)",
                "Windows Desktop (WinForms)",
                "ASP.NET Web API",
                "Console",
                "Class Library",
                "Worker Service (background)",
            },
            ApplicationTypeMenu.Labels.ToList());
    }

    [TestMethod]
    public void Resolve_MapsEveryLabelToItsTarget()
    {
        var keys = Enumerable.Range(0, ApplicationTypeMenu.Labels.Count)
            .Select(i => ApplicationTypeMenu.Resolve(i).Key)
            .ToList();

        CollectionAssert.AreEqual(
            new[] { "wpf", "winforms", "webapi", "console", "classlib", "worker" }, keys);
    }

    [TestMethod]
    public void LabelFor_NamesATargetTheReviewLoopCanShow()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("winforms", out var winforms));

        Assert.AreEqual("Windows Desktop (WinForms)", ApplicationTypeMenu.LabelFor(winforms));
    }

    [TestMethod]
    public void ChooseNumbered_ReturnsTheIndexTyped()
    {
        var writer = new StringWriter();

        // "2" is the second item, which is index 1. Users count from one.
        Assert.AreEqual(1, ApplicationTypeMenu.ChooseNumbered(new StringReader("2\n"), writer));
    }

    [TestMethod]
    public void ChooseNumbered_ListsEveryOptionWithItsNumber()
    {
        var writer = new StringWriter();
        ApplicationTypeMenu.ChooseNumbered(new StringReader("1\n"), writer);

        var written = writer.ToString();

        StringAssert.Contains(written, "1) WPF (Windows desktop)");
        StringAssert.Contains(written, "6) Worker Service (background)");
        StringAssert.Contains(written, "[1-6]");
    }

    [TestMethod]
    public void ChooseNumbered_BlankTakesTheDefault()
    {
        var writer = new StringWriter();

        Assert.AreEqual(0, ApplicationTypeMenu.ChooseNumbered(new StringReader("\n"), writer));
    }

    [TestMethod]
    public void ChooseNumbered_ReAsksOnSomethingOutOfRange()
    {
        var writer = new StringWriter();

        var chosen = ApplicationTypeMenu.ChooseNumbered(new StringReader("9\nzero\n3\n"), writer);

        Assert.AreEqual(2, chosen);
        StringAssert.Contains(writer.ToString(), "Choose a number between 1 and 6");
    }

    [TestMethod]
    public void ChooseNumbered_CancelsOnCAndOnEndOfInput()
    {
        Assert.IsNull(ApplicationTypeMenu.ChooseNumbered(new StringReader("c\n"), new StringWriter()));

        // EOF is the absence of an answer, not the default answer — the rule
        // DataSourceInterview already applies.
        Assert.IsNull(ApplicationTypeMenu.ChooseNumbered(new StringReader(""), new StringWriter()));
    }
}
