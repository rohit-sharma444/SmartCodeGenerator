using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.Web.Workflow;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// Any answer Generate reads, changed after the app was written, discards the written app.
/// </summary>
/// <remarks>
/// Before this only the description, the data and a second discovery did. Changing the app type,
/// the look, the screen split or the entry template - or editing the model on Review - left the
/// old app on the Generate node with no way to write the new one, and the Run node describing the
/// old app under the new answers.
/// </remarks>
[TestClass]
public class StaleGeneratedAppTests
{
    [TestMethod]
    public void A_new_app_type_discards_the_app()
    {
        var session = Generated();

        session.GoTo(BuildStage.AppType);
        session.SetTarget(new ClassLibraryTarget());

        AssertDiscarded(session);
    }

    [TestMethod]
    public void A_new_look_discards_the_app()
    {
        var session = Generated();

        session.GoTo(BuildStage.Look);
        session.SetCustomization(session.Customization with { Company = "Acme" });

        AssertDiscarded(session);
    }

    [TestMethod]
    public void A_new_entry_template_discards_the_app()
    {
        var session = Generated();

        session.GoTo(BuildStage.EntryTemplate);
        session.SetCustomization(session.Customization with
        {
            EntryTemplate = AppScaffolding.Screens.EntryTemplate.Tabbed
        });

        AssertDiscarded(session);
    }

    [TestMethod]
    public void A_review_edit_discards_the_app()
    {
        var session = Generated();

        session.GoTo(BuildStage.Review);
        session.Model!.Entities[0].Name = "Title";
        session.ModelEdited();

        AssertDiscarded(session);
    }

    /// <summary>The same answer again is not a change, so the app it wrote stands.</summary>
    [TestMethod]
    public void The_same_answer_again_keeps_the_app()
    {
        var session = Generated();

        session.GoTo(BuildStage.Look);
        session.SetCustomization(session.Customization);
        session.GoTo(BuildStage.AppType);
        session.SetTarget(new WpfTarget());

        Assert.IsNotNull(session.Generated);
        Assert.AreEqual(StageState.Done, session.StateOf(BuildStage.Generate));
    }

    /// <summary>The Run node said "started" about an app that had been discarded.</summary>
    [TestMethod]
    public void A_started_app_is_forgotten_but_can_still_be_stopped()
    {
        var session = Generated();
        session.MarkStarted(4242);

        session.GoTo(BuildStage.Look);
        session.SetCustomization(session.Customization with { Company = "Acme" });

        Assert.IsFalse(session.Started);
        Assert.IsFalse(session.HoldsAnAnswer(BuildStage.Run));

        // Still ours to stop: it holds its bin folder, so the next build fails until it closes.
        Assert.AreEqual(4242, session.StartedProcessId);
    }

    [TestMethod]
    public void A_changed_description_forgets_the_started_app_too()
    {
        var session = Generated();
        session.MarkStarted(4242);

        session.GoTo(BuildStage.Describe);
        session.SetDescription("a lending library");
        session.ConfirmPending();

        Assert.IsFalse(session.Started);
        Assert.IsNull(session.Generated);
    }

    /// <summary>None of this reaches the model: a changed look is not a changed domain.</summary>
    [TestMethod]
    public void The_model_is_never_at_stake()
    {
        var session = Generated();
        var model = session.Model;

        session.GoTo(BuildStage.Look);
        session.SetCustomization(session.Customization with { Company = "Acme" });

        Assert.AreSame(model, session.Model);
        Assert.IsNull(session.PendingDiscard);
    }

    private static void AssertDiscarded(BuildSession session)
    {
        Assert.IsNull(session.Generated, "the old app is still offered");
        Assert.AreEqual(StageState.Pending, session.StateOf(BuildStage.Generate));
        Assert.AreEqual(StageState.Pending, session.StateOf(BuildStage.Run));
        Assert.IsNotNull(session.Model, "the model must survive");
    }

    /// <summary>A session walked to Run with a WPF app written and built.</summary>
    private static BuildSession Generated()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Book" });

        var session = new BuildSession("a shop", @"C:\ws", AppCustomization.Default);
        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(model);

        while (session.Current != BuildStage.Generate)
            session.Advance();

        session.SetGenerated(new GeneratedApp(
            new ScaffoldResult(true, @"C:\ws\Shop", [], string.Empty),
            BuildLog: "Build succeeded.",
            BuildSucceeded: true));
        session.Advance();

        Assert.AreEqual(BuildStage.Run, session.Current);

        return session;
    }
}
