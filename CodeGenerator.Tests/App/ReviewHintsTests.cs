using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The prompt shown under the candidate model.
/// </summary>
/// <remarks>
/// This was a single 140-character line listing eight commands with their argument shapes,
/// reprinted after every action — so it is the text a user looks at more than any other in the
/// tool, and it wrapped on any normal terminal.
/// </remarks>
[TestClass]
public class ReviewHintsTests
{
    /// <remarks>
    /// Brackets stripped: the prompt writes accelerators as "[a]pprove", so the word is only whole
    /// once they are gone. The same trap the help overview hit — a plain substring check silently
    /// looks for a word the display deliberately splits.
    /// </remarks>
    private static string Short =>
        string.Join("\n", ReviewHints.Short).Replace("[", "").Replace("]", "");

    private static string Full => string.Join("\n", ReviewHints.Full);

    [TestMethod]
    public void TheShortFormFitsOnAnEightyColumnConsole()
    {
        foreach (var line in ReviewHints.Short)
        {
            Assert.IsLessThan(81, line.TrimEnd().Length,
                $"wraps at 80 ({line.TrimEnd().Length}): {line}");
        }
    }

    [TestMethod]
    public void TheShortFormOffersTheActionsMostRunsNeed()
    {
        // Approve and cancel end the loop; add and remove are the edits almost every run makes.
        foreach (var verb in new[] { "approve", "add", "remove", "cancel" })
            Assert.Contains(verb, Short, $"'{verb}' is not on the short form");
    }

    [TestMethod]
    public void TheShortFormSaysHowToSeeTheRest()
    {
        // Trimming the list is only acceptable if the rest is one keystroke away.
        Assert.Contains("?", Short);
    }

    [TestMethod]
    public void TheFullFormNamesEveryVerbTheLoopAccepts()
    {
        // A verb the loop handles but never mentions is a feature nobody finds.
        foreach (var action in ReviewActions.All)
            Assert.Contains(action.Verb, Full, $"'{action.Verb}' is not on the full form");
    }

    [TestMethod]
    public void TheFullFormShowsArgumentShapes()
    {
        // The reason the long line existed: `field` and `unfield` take two arguments and one of
        // them is optional-typed, which nobody guesses.
        Assert.Contains("<name>", Full);
        Assert.Contains("[:<type>]", Full);
    }

    [TestMethod]
    public void TheFullFormAlsoFitsOnAnEightyColumnConsole()
    {
        foreach (var line in ReviewHints.Full)
        {
            Assert.IsLessThan(81, line.TrimEnd().Length,
                $"wraps at 80 ({line.TrimEnd().Length}): {line}");
        }
    }

    [TestMethod]
    public void QuestionMarkIsRecognizedAsTheRequestForMore()
    {
        Assert.IsTrue(ReviewHints.IsMoreRequest("?"));
        Assert.IsTrue(ReviewHints.IsMoreRequest("  ?  "));
        Assert.IsTrue(ReviewHints.IsMoreRequest("help"));

        Assert.IsFalse(ReviewHints.IsMoreRequest("approve"));
        Assert.IsFalse(ReviewHints.IsMoreRequest(""));
        Assert.IsFalse(ReviewHints.IsMoreRequest(null));
    }
}
