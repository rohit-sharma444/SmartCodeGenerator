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
    public void Every_example_fits_on_one_line()
    {
        // The constraint belongs to the catalog, not to the renderer. Wrapping a long example
        // would keep the output tidy and hand the reader a command they have to reassemble from
        // two lines before they can type it, so the examples are kept short enough instead.
        const int Gutter = 12;

        foreach (var command in CommandDictionary.Commands)
        {
            Assert.IsLessThan(81, command.Example.Length + Gutter,
                $"the example for `{command.Command}` is too long to print on one line");
        }
    }

    [TestMethod]
    public void No_help_rendering_wraps_on_an_eighty_column_console()
    {
        // The overview had this guarantee and the other two did not, which is exactly where the
        // formatting went wrong:  printed a 45-character command column followed by a
        // whole description, so entries ran past 150 characters and the terminal folded them at
        // its own margin, mid-sentence, under the left edge.
        var renderings = new List<(string What, string Text)>
        {
            ("help", CommandDictionary.GetOverview()),
            ("help all", CommandDictionary.GetHelpText()),
        };

        renderings.AddRange(CommandDictionary.Categories
            .Select(c => ($"help {c}", CommandDictionary.GetTopicHelp(c)!)));

        foreach (var (what, text) in renderings)
        {
            foreach (var line in text.Split('\n'))
            {
                Assert.IsLessThan(81, line.TrimEnd().Length,
                    $"{what} wraps at 80 columns ({line.TrimEnd().Length} chars): {line}");
            }
        }
    }

    [TestMethod]
    public void Every_command_is_shown_with_what_it_does_and_how_it_is_typed()
    {
        // The format is three stacked lines per entry rather than a column, so the check is that
        // all three are still there for every entry - a wrap that dropped one would look tidy.
        foreach (var category in CommandDictionary.Categories)
        {
            var text = CommandDictionary.GetTopicHelp(category)!;

            foreach (var command in CommandDictionary.Commands.Where(c => c.Category == category))
            {
                StringAssert.Contains(text, command.Command);
                StringAssert.Contains(text, command.Example);
            }
        }
    }

    [TestMethod]
    public void Overview_SaysWhatTheToolDoesWithoutBeingAsked()
    {
        // The relationship offer is not a command, so no amount of reading the catalog finds it.
        // Someone working from the console would never learn it exists.
        var overview = CommandDictionary.GetOverview();

        StringAssert.Contains(overview, "Set for you");

        foreach (var offer in CommandDictionary.Unprompted)
            StringAssert.Contains(overview, offer.Command);
    }

    [TestMethod]
    public void HelpAll_SaysIt_Too_AndAddsThePipedCase()
    {
        var full = CommandDictionary.GetHelpText();

        StringAssert.Contains(full, "SET FOR YOU");

        foreach (var offer in CommandDictionary.Unprompted)
            StringAssert.Contains(full, offer.Summary);

        // Only in the long form: someone whose scripted run never shows the offer needs to know
        // that is deliberate, and the overview has no room to say so.
        StringAssert.Contains(full, "never asked");
    }

    [TestMethod]
    public void The_note_is_one_definition_rendered_three_ways()
    {
        // help, help all and docs/commands.md all render CommandDictionary.Unprompted. Two
        // spellings of this is how the manual appendix drifted from the catalog once already.
        foreach (var offer in CommandDictionary.Unprompted)
        {
            StringAssert.Contains(CommandDictionary.GetOverview(), offer.Command);
            StringAssert.Contains(CommandDictionary.GetHelpText(), offer.Command);
            StringAssert.Contains(CommandDictionary.GetMarkdownReference(), offer.Command);
        }
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
