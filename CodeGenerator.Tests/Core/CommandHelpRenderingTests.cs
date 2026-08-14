using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// The three shapes `help` comes in. The full catalog is ~250 lines, which scrolls the thing
/// a new user was told to type straight off the screen; the overview is what they get instead.
/// </summary>
[TestClass]
public class CommandHelpRenderingTests
{
    /// <summary>Generous, but far below the full catalog — the point is that it fits a screen.</summary>
    private const int OverviewLineBudget = 30;

    private static string[] OverviewLines()
        => CommandDictionary.GetOverview().Split('\n');

    [TestMethod]
    public void Overview_FitsOnAScreen()
    {
        var overview = OverviewLines();
        var full = CommandDictionary.GetHelpText().Split('\n');

        Assert.IsLessThan(OverviewLineBudget, overview.Length,
            $"the overview has grown to {overview.Length} lines");

        // The whole reason this exists: it must be dramatically shorter than what it replaced.
        Assert.IsGreaterThan(overview.Length * 4, full.Length);
    }

    [TestMethod]
    public void Overview_NamesEveryCategory()
    {
        var overview = CommandDictionary.GetOverview();

        // A category missing here is a category a user cannot discover exists.
        foreach (var category in CommandDictionary.Categories)
            Assert.Contains(category, overview, $"'{category}' is not on the overview");
    }

    [TestMethod]
    public void Overview_SaysHowToSeeOneCategoryAndHowToSeeEverything()
    {
        var overview = CommandDictionary.GetOverview();

        Assert.Contains("help all", overview);
        Assert.Contains("help <", overview);
    }

    [TestMethod]
    public void Categories_AreTheDistinctCategoriesActuallyInTheCatalog()
    {
        var expected = CommandDictionary.Commands
            .Select(c => c.Category)
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        CollectionAssert.AreEqual(expected, CommandDictionary.Categories.ToList());
    }

    [TestMethod]
    public void EveryCategory_CanBeAskedForByName()
    {
        foreach (var category in CommandDictionary.Categories)
        {
            var help = CommandDictionary.GetTopicHelp(category);

            Assert.IsNotNull(help, $"'{category}' could not be asked for");

            // Its own commands, and only its own: a topic that quietly returned everything
            // would look like it worked.
            foreach (var command in CommandDictionary.Commands.Where(c => c.Category == category))
                Assert.Contains(command.Example, help, $"'{category}' omitted '{command.Command}'");

            var foreign = CommandDictionary.Commands.First(c => c.Category != category);
            Assert.DoesNotContain($"e.g. {foreign.Example}", help);
        }
    }

    [TestMethod]
    public void ACategoryIsFoundHoweverItIsTyped()
    {
        // "Build App" is two words; a user types it lower-case, and may well close it up.
        Assert.IsNotNull(CommandDictionary.GetTopicHelp("build app"));
        Assert.IsNotNull(CommandDictionary.GetTopicHelp("BUILD APP"));
        Assert.IsNotNull(CommandDictionary.GetTopicHelp("buildapp"));
        Assert.IsNotNull(CommandDictionary.GetTopicHelp("  Build App  "));
    }

    [TestMethod]
    public void ACommandNameIsAlsoATopic()
    {
        // `help approve` is what a user types when stuck on one command, and answering it with
        // "no such category" would be pedantry.
        var help = CommandDictionary.GetTopicHelp("approve");

        Assert.IsNotNull(help);
        Assert.Contains("approve", help);
    }

    [TestMethod]
    public void AnUnknownTopicIsNotAnswered()
    {
        // Null, so the caller can list the categories rather than print an empty screen.
        Assert.IsNull(CommandDictionary.GetTopicHelp("frobnicate"));
        Assert.IsNull(CommandDictionary.GetTopicHelp(""));
        Assert.IsNull(CommandDictionary.GetTopicHelp(null));
    }

    [TestMethod]
    public void EveryStarterCommandExistsInTheCatalog()
    {
        // The starter list is hand-curated, so it is exactly the kind of thing that rots into
        // advertising a command that was renamed or removed.
        Assert.IsNotEmpty(CommandDictionary.Starters);

        foreach (var starter in CommandDictionary.Starters)
        {
            Assert.IsTrue(
                CommandDictionary.Commands.Any(c => c.Command == starter.Command),
                $"the overview offers '{starter.Command}', which is not in the catalog");
        }
    }

    [TestMethod]
    public void EveryStarterCommandIsShownOnTheOverview()
    {
        var overview = CommandDictionary.GetOverview();

        foreach (var starter in CommandDictionary.Starters)
            Assert.Contains(starter.Command, overview);
    }

    [TestMethod]
    public void Overview_DoesNotWrapOnAnEightyColumnConsole()
    {
        // Counting lines is not enough: the first cut of this reused CommandInfo.Description for
        // the starter rows, and the one for `build app:` is over 200 characters. Twenty logical
        // lines became thirty-odd wrapped ones, which is the thing the overview exists to avoid.
        foreach (var line in OverviewLines())
        {
            Assert.IsLessThan(81, line.TrimEnd().Length,
                $"wraps at 80 columns ({line.TrimEnd().Length} chars): {line}");
        }
    }
}
