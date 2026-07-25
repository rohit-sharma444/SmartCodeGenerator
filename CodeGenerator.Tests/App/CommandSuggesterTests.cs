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
}
