using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The handover between a panel and the diagram: the panel owns the answer, the box draws the
/// control. Everything it decides is asserted here rather than through a rendered page.
/// </summary>
[TestClass]
public class StageNavigationTests
{
    [TestMethod]
    public void Offers_nothing_until_a_panel_does()
    {
        var navigation = new StageNavigation();

        Assert.IsFalse(navigation.HasStep);
        Assert.IsFalse(navigation.CanContinue);
        Assert.AreEqual(StageNavigation.DefaultLabel, navigation.Label);
    }

    [TestMethod]
    public async Task Applying_runs_what_the_panel_offered()
    {
        var navigation = new StageNavigation();
        var applied = false;

        navigation.Offer(() => { applied = true; return Task.FromResult(true); });

        Assert.IsTrue(navigation.CanContinue);

        Assert.IsTrue(await navigation.ApplyAsync());
        Assert.IsTrue(applied);
    }

    // A panel that refuses its own input — a path that is not there — says so, and the workflow
    // stays where it is rather than advancing past a stage that answered nothing.
    [TestMethod]
    public async Task A_panel_that_refuses_its_input_reports_it()
    {
        var navigation = new StageNavigation();

        navigation.Offer(() => Task.FromResult(false));

        Assert.IsFalse(await navigation.ApplyAsync());
    }

    // A step offered but not usable yet is drawn disabled rather than missing, so the user can see
    // what they are working towards.
    [TestMethod]
    public void An_unready_step_is_offered_but_cannot_be_taken()
    {
        var navigation = new StageNavigation();
        var usable = false;

        navigation.Offer(() => Task.FromResult(true), ready: () => usable);

        Assert.IsTrue(navigation.HasStep);
        Assert.IsFalse(navigation.CanContinue);

        usable = true;

        Assert.IsTrue(navigation.CanContinue);
    }

    [TestMethod]
    public async Task Applying_an_unready_step_does_nothing()
    {
        var navigation = new StageNavigation();
        var applied = false;

        navigation.Offer(() => { applied = true; return Task.FromResult(true); }, ready: () => false);

        Assert.IsFalse(await navigation.ApplyAsync());
        Assert.IsFalse(applied);
    }

    // The review stage moves on by generating, and calling that "Continue" would hide what the
    // click does.
    [TestMethod]
    public void A_panel_can_name_its_own_step()
    {
        var navigation = new StageNavigation();

        navigation.Offer(() => Task.FromResult(true), label: "Generate");

        Assert.AreEqual("Generate", navigation.Label);
    }

    [TestMethod]
    public void Withdrawing_takes_the_step_away_and_forgets_the_label()
    {
        var navigation = new StageNavigation();
        navigation.Offer(() => Task.FromResult(true), label: "Generate");

        navigation.Withdraw();

        Assert.IsFalse(navigation.HasStep);
        Assert.IsFalse(navigation.CanContinue);
        Assert.AreEqual(StageNavigation.DefaultLabel, navigation.Label);
    }

    // The panel and the diagram are siblings and neither renders the other, so the diagram only
    // finds out that a step appeared if it is told.
    [TestMethod]
    public void Offering_and_withdrawing_both_announce_themselves()
    {
        var navigation = new StageNavigation();
        var announcements = 0;

        navigation.Changed += () => announcements++;

        navigation.Offer(() => Task.FromResult(true));
        navigation.Withdraw();

        Assert.AreEqual(2, announcements);
    }

    // Clicking another box is navigation, and navigation should not cost the user the answer they
    // just gave.
    [TestMethod]
    public void A_plain_step_is_committed_when_the_user_clicks_away()
    {
        var navigation = new StageNavigation();
        navigation.Offer(() => Task.FromResult(true));

        Assert.IsTrue(navigation.ShouldCommitBeforeLeaving);
    }

    // Generating a project because someone clicked a box to look at an earlier answer would be
    // indefensible, so a named step is an action and only ever taken deliberately.
    [TestMethod]
    public void A_named_step_is_never_taken_by_clicking_away()
    {
        var navigation = new StageNavigation();
        navigation.Offer(() => Task.FromResult(true), label: "Generate");

        Assert.IsFalse(navigation.ShouldCommitBeforeLeaving);
    }

    [TestMethod]
    public void An_unusable_step_is_not_committed_by_clicking_away()
    {
        var navigation = new StageNavigation();
        navigation.Offer(() => Task.FromResult(true), ready: () => false);

        Assert.IsFalse(navigation.ShouldCommitBeforeLeaving);
    }

    [TestMethod]
    public void With_no_step_at_all_there_is_nothing_to_commit()
        => Assert.IsFalse(new StageNavigation().ShouldCommitBeforeLeaving);
}
