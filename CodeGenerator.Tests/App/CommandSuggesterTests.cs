using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class CommandSuggesterTests
{
    [TestMethod]
    [DataRow("kitten", "sitting", 3)]
    [DataRow("create", "create", 0)]
    [DataRow("craete", "create", 2)]
    [DataRow("", "abc", 3)]
    public void Distance_MatchesKnownPairs(string a, string b, int expected)
        => Assert.AreEqual(expected, CommandSuggester.Distance(a, b));

    [TestMethod]
    public void Closest_SuggestsCreateProject_ForTransposedTypo()
        => CollectionAssert.Contains(
            CommandSuggester.Closest("craete project").ToList(), "create project");

    [TestMethod]
    public void Closest_SuggestsListProjects_ForMissingLetter()
        => CollectionAssert.Contains(
            CommandSuggester.Closest("lst projects").ToList(), "list projects");

    [TestMethod]
    public void Closest_PrefersTheCloseCommand_OverAShortOneMatchedOnTheFirstWordOnly()
    {
        // "lsit clases" was answered with "exit" and "chat": a command that takes no arguments
        // was compared with the first word alone, as if "clases" were its argument.
        var guesses = CommandSuggester.Closest("lsit clases");

        Assert.AreEqual("list classes", guesses[0]);
        CollectionAssert.DoesNotContain(guesses.ToList(), "exit");
        CollectionAssert.DoesNotContain(guesses.ToList(), "chat");
    }

    [TestMethod]
    public void Closest_NeverSuggestsExactlyWhatWasTyped()
        => CollectionAssert.DoesNotContain(
            CommandSuggester.Closest("Show Relationships").Select(s => s.ToLowerInvariant()).ToList(),
            "show relationships");

    [TestMethod]
    public void Closest_ReturnsEmpty_ForGibberish()
        => Assert.IsEmpty(CommandSuggester.Closest("xyzzy plughfoo"));

    [TestMethod]
    public void Closest_ReturnsEmpty_ForBlankInput()
        => Assert.IsEmpty(CommandSuggester.Closest("   "));

    [TestMethod]
    public void Closest_ExcludesTemplatedForms_WithAngleBrackets()
        => Assert.IsFalse(
            CommandSuggester.Closest("create class").Any(s => s.Contains('<')));

    [TestMethod]
    public void Closest_SuggestsTemplatedCommandPrefix_ForKeywordTypo()
        => CollectionAssert.Contains(
            CommandSuggester.Closest("add busniess validation Number should not be blank in Bill").ToList(),
            "add business validation");

    [TestMethod]
    public void Closest_Suggestions_NeverContainPlaceholdersOrOptions()
        => Assert.IsFalse(
            CommandSuggester.Closest("add busniess validation Number in Bill")
                .Any(s => s.Contains('<') || s.Contains('[')));

    [TestMethod]
    public void Closest_SuggestsNothing_ForVeryShortInput()
    {
        // The distance budget used to be a flat 3 regardless of input length, so every
        // three-letter command sat within 3 edits of any two-character input: `-h` was answered
        // with "why, add, cls" and `/?` with "add, cls, why". Confident nonsense, offered at the
        // exact moment the user is already lost.
        Assert.IsEmpty(CommandSuggester.Closest("-h"));
        Assert.IsEmpty(CommandSuggester.Closest("/?"));
        Assert.IsEmpty(CommandSuggester.Closest("x"));
        Assert.IsEmpty(CommandSuggester.Closest("qq"));
    }

    [TestMethod]
    public void Closest_StillSuggests_WhenTheTypoIsSmallRelativeToTheWord()
    {
        // The budget has to scale, not simply shrink: these are the cases the feature exists for
        // and they must survive the fix.
        CollectionAssert.Contains(CommandSuggester.Closest("hepl").ToList(), "help");
        CollectionAssert.Contains(CommandSuggester.Closest("statuss").ToList(), "status");
        CollectionAssert.Contains(CommandSuggester.Closest("btach").ToList(), "batch");
    }

    [TestMethod]
    public void Closest_NeverSuggestsAWordItWouldHaveToRewriteEntirely()
    {
        // A suggestion whose edit distance is most of its own length is not a correction, it is a
        // different word.
        foreach (var input in new[] { "-h", "/?", "zz", "?" })
        {
            foreach (var suggestion in CommandSuggester.Closest(input))
            {
                Assert.IsLessThan(
                    suggestion.Length,
                    CommandSuggester.Distance(input, suggestion),
                    $"'{input}' -> '{suggestion}' rewrites the whole word");
            }
        }
    }
}
