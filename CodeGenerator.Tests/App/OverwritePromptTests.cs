using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The question asked before a second <c>build app</c> writes over the first one's sources.
/// </summary>
[TestClass]
public class OverwritePromptTests
{
    private const string Folder = @"D:\Work\Shop";

    [TestMethod]
    public void EnterCancels()
    {
        // The safe answer is the effortless one. A user who has stopped reading and hits Enter
        // must not thereby overwrite a project.
        Assert.AreEqual(OverwriteAction.Cancel, OverwritePrompt.Parse(""));
        Assert.AreEqual(OverwriteAction.Cancel, OverwritePrompt.Parse("   "));
    }

    [TestMethod]
    public void EndOfInputCancels()
    {
        // A closed pipe is the absence of an answer, never consent to overwrite.
        Assert.AreEqual(OverwriteAction.Cancel, OverwritePrompt.Parse(null));
    }

    [TestMethod]
    public void EachActionHasALetterAndAWord()
    {
        Assert.AreEqual(OverwriteAction.Overwrite, OverwritePrompt.Parse("o"));
        Assert.AreEqual(OverwriteAction.Overwrite, OverwritePrompt.Parse("overwrite"));
        Assert.AreEqual(OverwriteAction.Rename, OverwritePrompt.Parse("r"));
        Assert.AreEqual(OverwriteAction.Rename, OverwritePrompt.Parse("rename"));
        Assert.AreEqual(OverwriteAction.Cancel, OverwritePrompt.Parse("c"));
        Assert.AreEqual(OverwriteAction.Cancel, OverwritePrompt.Parse("cancel"));
    }

    [TestMethod]
    public void AnswersAreReadCaseAndSpaceInsensitively()
    {
        Assert.AreEqual(OverwriteAction.Overwrite, OverwritePrompt.Parse("  O  "));
        Assert.AreEqual(OverwriteAction.Rename, OverwritePrompt.Parse("Rename"));
        Assert.AreEqual(OverwriteAction.Cancel, OverwritePrompt.Parse("CANCEL"));
    }

    [TestMethod]
    public void AnythingElseIsNotAnAnswer()
    {
        // Null means "ask again" rather than any action. Reading an unrecognized word as a
        // default would make a typo destructive.
        Assert.IsNull(OverwritePrompt.Parse("yes"));
        Assert.IsNull(OverwritePrompt.Parse("y"));
        Assert.IsNull(OverwritePrompt.Parse("delete"));
        Assert.IsNull(OverwritePrompt.Parse("ok"));
    }

    [TestMethod]
    public void Warning_NamesTheFolderAndSaysWhatSurvives()
    {
        var warning = string.Join("\n", OverwritePrompt.Warning(Folder));

        Assert.Contains(Folder, warning);

        // Both halves matter: what is lost is why the question is being asked, and what is kept
        // is what stops a user cancelling a regeneration that was actually safe.
        Assert.Contains("Models", warning);
        Assert.Contains(".Custom.cs", warning);
    }

    [TestMethod]
    public void Warning_OffersEveryActionParseAccepts()
    {
        // Brackets removed first: the prompt writes the accelerator as "[o]verwrite", so the
        // word is only whole once they are gone.
        var warning = string.Join("\n", OverwritePrompt.Warning(Folder))
            .ToLowerInvariant()
            .Replace("[", "")
            .Replace("]", "");

        // An action offered but not accepted, or accepted but not offered, is how these drift.
        foreach (var action in Enum.GetValues<OverwriteAction>())
        {
            var word = action.ToString().ToLowerInvariant();

            Assert.Contains(word, warning, $"{action} not offered");
            Assert.AreEqual(action, OverwritePrompt.Parse(word), $"{action} offered but not accepted");
            Assert.AreEqual(action, OverwritePrompt.Parse(word[..1]), $"{action}'s letter not accepted");
        }

        Assert.Contains("enter cancels", warning);
    }
}
