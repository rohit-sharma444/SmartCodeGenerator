using AppScaffolding.Incremental;
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

    /// <summary>
    /// Every action Parse accepts is a real answer, whether or not a given prompt offers it.
    /// </summary>
    /// <remarks>
    /// This used to assert that every enum member appears in <c>Warning(folder)</c>, which was
    /// true while the two sets were the same. <c>Add</c> broke that honestly: the one-argument
    /// overload has no delta and cannot know whether anything is new, so it must not offer it.
    /// The invariant worth keeping is per-prompt - everything OFFERED is accepted - and it is
    /// asserted for each overload below.
    /// </remarks>
    [TestMethod]
    public void EveryActionHasAWordAndALetterParseAccepts()
    {
        foreach (var action in Enum.GetValues<OverwriteAction>())
        {
            var word = action.ToString().ToLowerInvariant();

            Assert.AreEqual(action, OverwritePrompt.Parse(word), $"{action} not accepted");
            Assert.AreEqual(
                action, OverwritePrompt.Parse(word[..1]), $"{action}'s letter not accepted");
        }
    }

    /// <summary>An action offered but not accepted is how a prompt and its parser drift.</summary>
    [TestMethod]
    public void WarningOffersOnlyActionsParseAccepts()
    {
        AssertEverythingOfferedIsAccepted(OverwritePrompt.Warning(Folder));
    }

    [TestMethod]
    public void Warning_StillSaysEnterCancels()
    {
        var warning = string.Join("\n", OverwritePrompt.Warning(Folder)).ToLowerInvariant();

        Assert.Contains("enter cancels", warning);
    }

    /// <summary>Brackets removed first: the prompt writes the accelerator as "[o]verwrite", so
    /// the word is only whole once they are gone.</summary>
    private static void AssertEverythingOfferedIsAccepted(IReadOnlyList<string> lines)
    {
        var text = string.Join("\n", lines)
            .ToLowerInvariant()
            .Replace("[", "")
            .Replace("]", "");

        foreach (var action in Enum.GetValues<OverwriteAction>())
        {
            var word = action.ToString().ToLowerInvariant();

            // "add the 2 new" contains "add"; an action absent from this prompt is fine, an
            // action present but unparseable is not.
            if (text.Contains(word, StringComparison.Ordinal))
                Assert.AreEqual(
                    action, OverwritePrompt.Parse(word), $"{action} offered but not accepted");
        }
    }

    [TestMethod]
    public void Add_is_an_answer()
    {
        Assert.AreEqual(OverwriteAction.Add, OverwritePrompt.Parse("a"));
        Assert.AreEqual(OverwriteAction.Add, OverwritePrompt.Parse("add"));
    }

    /// <summary>
    /// An answer that would do nothing must not be on the menu. Offering "add" when the model
    /// has nothing new in it is a lie about what the tool can do.
    /// </summary>
    [TestMethod]
    public void Add_is_not_offered_when_nothing_is_new()
    {
        var delta = new[]
        {
            new ClassDelta("Customer", ClassFate.Unchanged),
            new ClassDelta("Order", ClassFate.Changed),
        };

        var lines = string.Join("\n", OverwritePrompt.Warning(Folder, delta));

        Assert.DoesNotContain("[a]dd", lines);
        StringAssert.Contains(lines, "[o]verwrite");
    }

    [TestMethod]
    public void Two_changed_classes_differ_and_one_differs()
    {
        var two = string.Join(" ", OverwritePrompt.Warning(Folder,
            [new ClassDelta("Customer", ClassFate.Changed), new ClassDelta("Order", ClassFate.Changed)]));
        var one = string.Join(" ", OverwritePrompt.Warning(Folder,
            [new ClassDelta("Customer", ClassFate.Changed), new ClassDelta("Order", ClassFate.Unchanged)]));

        StringAssert.Contains(two, "2 classes differ:");
        StringAssert.Contains(one, "1 class differs:");
    }

    [TestMethod]
    public void Add_is_offered_with_a_count_when_something_is_new()
    {
        var delta = new[]
        {
            new ClassDelta("Customer", ClassFate.Unchanged),
            new ClassDelta("Supplier", ClassFate.New),
            new ClassDelta("PurchaseOrder", ClassFate.New),
        };

        var lines = string.Join("\n", OverwritePrompt.Warning(Folder, delta));

        StringAssert.Contains(lines, "[a]dd the 2 new");
        StringAssert.Contains(lines, "3");
    }

    /// <summary>
    /// A changed class is named, because adding will not reconcile it and overwriting would.
    /// The prompt must not let someone choose "add" believing it will.
    /// </summary>
    [TestMethod]
    public void A_changed_class_is_named_and_its_fate_stated()
    {
        var delta = new[]
        {
            new ClassDelta("Customer", ClassFate.Changed),
            new ClassDelta("Supplier", ClassFate.New),
        };

        var lines = string.Join("\n", OverwritePrompt.Warning(Folder, delta));

        StringAssert.Contains(lines, "Customer");
        StringAssert.Contains(lines, "differs");
    }

    [TestMethod]
    public void The_delta_prompt_offers_only_actions_parse_accepts()
    {
        var delta = new[] { new ClassDelta("Supplier", ClassFate.New) };

        AssertEverythingOfferedIsAccepted(OverwritePrompt.Warning(Folder, delta));
    }

    /// <summary>Enter still cancels: the effortless answer has to be the safe one, and a
    /// closed pipe is the absence of an answer rather than consent.</summary>
    [TestMethod]
    public void Enter_still_cancels_with_add_on_the_menu()
        => Assert.AreEqual(OverwriteAction.Cancel, OverwritePrompt.Parse(string.Empty));
}
