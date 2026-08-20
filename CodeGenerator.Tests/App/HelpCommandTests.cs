using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Recognizing `help`, `help &lt;topic&gt;` and `help all` before the recognition pipeline sees
/// them — LiteralCommandTable keys on the exact word "help", so a topic would otherwise fall
/// through to the NLP path and arrive mangled or not at all.
/// </summary>
[TestClass]
public class HelpCommandTests
{
    [TestMethod]
    public void BareHelpIsRecognized()
    {
        Assert.IsTrue(HelpCommand.IsHelp("help"));
        Assert.IsTrue(HelpCommand.IsHelp("  help  "));
        Assert.IsTrue(HelpCommand.IsHelp("HELP"));
    }

    [TestMethod]
    public void HelpWithATopicIsRecognized()
    {
        Assert.IsTrue(HelpCommand.IsHelp("help build app"));
        Assert.IsTrue(HelpCommand.IsHelp("help all"));
        Assert.IsTrue(HelpCommand.IsHelp("help approve"));
    }

    [TestMethod]
    public void WordsMerelyStartingWithHelpAreNotHelp()
    {
        // The same trap `IsBuildApp` documents: a prefix test would swallow these.
        Assert.IsFalse(HelpCommand.IsHelp("helper"));
        Assert.IsFalse(HelpCommand.IsHelp("helpful commands"));
        Assert.IsFalse(HelpCommand.IsHelp("show help"));
        Assert.IsFalse(HelpCommand.IsHelp(""));
    }

    [TestMethod]
    public void TheTopicIsEverythingAfterTheWord()
    {
        Assert.AreEqual("", HelpCommand.Topic("help"));
        Assert.AreEqual("build app", HelpCommand.Topic("help build app"));
        Assert.AreEqual("all", HelpCommand.Topic("  help   all  "));
    }

    [TestMethod]
    public void BareHelpRendersTheOverviewNotTheCatalog()
    {
        var rendered = HelpCommand.Render("help");

        Assert.Contains("Categories", rendered);

        // The full catalog is what `help all` is for. Ending up with it here is the whole
        // failure this change exists to prevent.
        Assert.IsLessThan(30, rendered.Split('\n').Length);
    }

    [TestMethod]
    public void HelpAllRendersEveryCommand()
    {
        var rendered = HelpCommand.Render("help all");

        foreach (var command in CodeGenerationEngine.Core.CommandDictionary.Commands)
            Assert.Contains($"e.g. {command.Example}", rendered);
    }

    [TestMethod]
    public void HelpWithAKnownTopicRendersThatTopic()
    {
        var rendered = HelpCommand.Render("help validation");

        Assert.Contains("generate validator", rendered);
        Assert.DoesNotContain("e.g. batch", rendered);
    }

    [TestMethod]
    public void HelpWithAnUnknownTopicSaysSoAndOffersTheCategories()
    {
        // Answering an unrecognized topic with silence, or with the whole catalog, both leave
        // the user with no idea what they were allowed to type.
        var rendered = HelpCommand.Render("help frobnicate");

        Assert.Contains("frobnicate", rendered);

        foreach (var category in CodeGenerationEngine.Core.CommandDictionary.Categories)
            Assert.Contains(category, rendered);
    }
    // The catalog is what `help build app` prints, and an interactive run no longer answers
    // questions in the console at all — a catalog that still describes one is a documentation bug.
    [TestMethod]
    public void The_build_app_entry_mentions_the_workflow_page()
    {
        var entry = CodeGenerationEngine.Core.CommandDictionary.Commands
            .First(c => c.Command == "build app");

        StringAssert.Contains(entry.Description.ToLowerInvariant(), "browser");
    }

}
