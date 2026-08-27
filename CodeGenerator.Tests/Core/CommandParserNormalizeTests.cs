using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// <see cref="CommandParser.Normalize(string, bool)"/> is a function every command in the
/// tool passes through, but before this class it had no direct test of its own — coverage
/// was entirely incidental to whichever intents the other command tests happened to drive.
/// The one-argument overload is the load-bearing case: it has to behave exactly like
/// <c>removeStopWords: true</c>, because every existing caller other than
/// <c>CommandEntityExtractor</c>'s validation intents relies on that being unchanged.
/// </summary>
[TestClass]
public class CommandParserNormalizeTests
{
    [TestMethod]
    public void RemoveStopWords_true_strips_the_a_and_an()
    {
        var result = CommandParser.Normalize("must be in the past", removeStopWords: true);

        Assert.AreEqual("must be in past", result);
    }

    [TestMethod]
    public void RemoveStopWords_false_keeps_the_a_and_an()
    {
        var result = CommandParser.Normalize("must be in the past", removeStopWords: false);

        Assert.AreEqual("must be in the past", result);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Input_with_no_stop_words_is_unchanged_either_way(bool removeStopWords)
    {
        var result = CommandParser.Normalize("must be positive", removeStopWords);

        Assert.AreEqual("must be positive", result);
    }

    /// <summary>
    /// The property that makes every pre-existing caller of the one-argument overload
    /// safe: it must be indistinguishable from calling the two-argument overload with
    /// <c>removeStopWords: true</c>. If this ever drifts, every intent other than
    /// AddValidation/AddBusinessValidation silently changes behaviour.
    /// </summary>
    [TestMethod]
    [DataRow("must be in the past")]
    [DataRow("please could you create a class")]
    [DataRow("must be positive")]
    [DataRow("")]
    public void One_argument_overload_matches_removeStopWords_true(string input)
    {
        Assert.AreEqual(
            CommandParser.Normalize(input, removeStopWords: true),
            CommandParser.Normalize(input));
    }
}
