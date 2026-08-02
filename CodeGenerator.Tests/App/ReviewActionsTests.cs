using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Covers the review loop's action catalog and the bridge back to the `string[] parts` its
/// switch already understands. The loop itself reads the console and cannot be driven from a
/// test, so everything decidable about an action lives here.
/// </summary>
[TestClass]
public class ReviewActionsTests
{
    private static ReviewAction Action(string verb)
        => ReviewActions.All.Single(a => a.Verb == verb);

    [TestMethod]
    public void All_CoversExactlyTheVerbsTheSwitchHandles()
    {
        string[] expected =
            ["approve", "add", "remove", "rename", "field", "unfield", "why", "cancel"];

        CollectionAssert.AreEquivalent(
            expected, ReviewActions.All.Select(a => a.Verb).ToArray());
    }

    [TestMethod]
    public void Labels_AreTheCatalogsLabelsInOrder()
        => CollectionAssert.AreEqual(
            ReviewActions.All.Select(a => a.Label).ToArray(),
            ReviewActions.Labels.ToArray());

    [TestMethod]
    [DataRow("approve", 0)]
    [DataRow("cancel", 0)]
    [DataRow("add", 1)]
    [DataRow("remove", 1)]
    [DataRow("why", 1)]
    [DataRow("rename", 2)]
    [DataRow("field", 2)]
    [DataRow("unfield", 2)]
    public void ArgumentPrompts_MatchWhatTheSwitchCaseNeeds(string verb, int expected)
        => Assert.HasCount(expected, Action(verb).ArgumentPrompts);

    [TestMethod]
    [DataRow("rename")]
    [DataRow("field")]
    [DataRow("unfield")]
    public void Compose_TwoArgumentAction_SatisfiesItsLengthGuard(string verb)
    {
        // The switch guards these cases with `when parts.Length >= 3`.
        var parts = ReviewActions.Compose(Action(verb), ["Book", "Novel"]);

        Assert.IsGreaterThanOrEqualTo(3, parts.Length, verb);
        Assert.AreEqual(verb, parts[0]);
    }

    [TestMethod]
    [DataRow("add")]
    [DataRow("remove")]
    public void Compose_OneArgumentAction_SatisfiesItsLengthGuard(string verb)
    {
        // The switch guards these cases with `when parts.Length >= 2`.
        var parts = ReviewActions.Compose(Action(verb), ["Book"]);

        CollectionAssert.AreEqual(new[] { verb, "Book" }, parts);
    }

    [TestMethod]
    public void Compose_NoArgumentAction_IsJustTheVerb()
        => CollectionAssert.AreEqual(
            new[] { "approve" }, ReviewActions.Compose(Action("approve"), []));

    [TestMethod]
    public void Compose_WhyWithNoName_YieldsTheWholeReportForm()
    {
        // WhyLines treats an absent name as "the whole report", and the switch's `why` case has
        // no length guard — so a blank answer has to disappear rather than arrive as "".
        var parts = ReviewActions.Compose(Action("why"), [""]);

        CollectionAssert.AreEqual(new[] { "why" }, parts);
    }

    [TestMethod]
    public void Compose_WhyWithName_KeepsIt()
        => CollectionAssert.AreEqual(
            new[] { "why", "Book" }, ReviewActions.Compose(Action("why"), ["Book"]));

    [TestMethod]
    public void Compose_MultiWordAnswer_StaysOneElement()
    {
        // The typed path's Split(' ', 3) would break this into pieces; composing must not.
        // ToUserTypedEntityName turns the single element into CashRegister downstream.
        var parts = ReviewActions.Compose(Action("rename"), ["Book", "cash register"]);

        CollectionAssert.AreEqual(new[] { "rename", "Book", "cash register" }, parts);
    }

    [TestMethod]
    public void Compose_TrimsEachAnswer()
        => CollectionAssert.AreEqual(
            new[] { "add", "Book" }, ReviewActions.Compose(Action("add"), ["  Book  "]));
}
