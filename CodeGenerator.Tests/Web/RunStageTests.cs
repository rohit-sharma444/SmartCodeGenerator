using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerator.Web;
using CodeGenerator.Web.Components.Stages;
using CodeGenerator.Web.Workflow;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The run stage. Starting a process is the one thing here a test cannot do, so what is asserted
/// is everything around it: whether there is anything to start, what the panel says when there is
/// not, and that finishing hands the console its report.
/// </summary>
[TestClass]
public class RunStageTests
{
    private static readonly ScaffoldResult Written =
        new(Success: true, ProjectPath: @"C:\ws\Shop", FilesWritten: [], Message: string.Empty);

    [TestMethod]
    public void A_green_build_can_be_run()
        => Assert.IsTrue(
            GenerateStep.CanRun(
                new GeneratedApp(Written, BuildLog: string.Empty, BuildSucceeded: true),
                new WpfTarget()));

    // Nothing is known to work, and starting an app whose build failed would only show the user
    // a second failure.
    [TestMethod]
    public void A_red_build_cannot_be_run()
        => Assert.IsFalse(
            GenerateStep.CanRun(
                new GeneratedApp(Written, BuildLog: string.Empty, BuildSucceeded: false),
                new WpfTarget()));

    // `dotnet run` on a class library fails with MSB4057, which NextSteps already documents.
    [TestMethod]
    public void A_class_library_cannot_be_run()
        => Assert.IsFalse(
            GenerateStep.CanRun(
                new GeneratedApp(Written, BuildLog: string.Empty, BuildSucceeded: true),
                new ClassLibraryTarget()));

    [TestMethod]
    public void Nothing_generated_cannot_be_run()
        => Assert.IsFalse(GenerateStep.CanRun(null, new WpfTarget()));

    [TestMethod]
    public void Offers_to_start_an_app_that_built()
    {
        using var context = new BunitContext();

        var rendered = Render(context, out _, BuildSucceeded: true);

        Assert.IsNotEmpty(rendered.FindAll("button.run"));
        Assert.IsNotEmpty(rendered.FindAll("button.finish"));
    }

    // Said, not silently hidden: a user looking at a Run node wants to know why it is not
    // offering to run anything.
    [TestMethod]
    public void Explains_itself_when_there_is_nothing_to_start()
    {
        using var context = new BunitContext();

        var rendered = Render(context, out _, BuildSucceeded: false);

        Assert.IsEmpty(rendered.FindAll("button.run"));
        StringAssert.Contains(rendered.Markup, "did not build");
        Assert.IsNotEmpty(rendered.FindAll("button.finish"));
    }

    [TestMethod]
    public void Finishing_ends_the_workflow_with_the_lines_the_console_prints()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session, BuildSucceeded: true);

        rendered.Find("button.finish").Click();

        Assert.AreEqual(SessionPhase.Generated, session.Phase);
        Assert.IsNotNull(session.Outcome);
        Assert.IsTrue(
            session.Outcome!.Lines.Any(l => l.Contains(@"C:\ws\Shop")),
            "the outcome should name the project that was written");
    }

    [TestMethod]
    public void Going_back_reopens_the_generate_stage()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session, BuildSucceeded: true);

        rendered.Find("button.back").Click();

        Assert.AreEqual(BuildStage.Generate, session.Current);
    }

    // A reload builds a new circuit with a new panel, so a generate panel that trusted only its
    // own fields would offer to write — and rebuild — a project that is already on disk.
    [TestMethod]
    public void The_generate_panel_recovers_what_the_session_already_generated()
    {
        using var context = new BunitContext();
        var session = SessionOnTheRunStage(buildSucceeded: true);
        session.GoTo(BuildStage.Generate);

        var rendered = context.Render<GenerateStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.OnAdvance, () => { }));

        Assert.IsEmpty(rendered.FindAll("button.write"));
        Assert.IsNotEmpty(rendered.FindAll("button.next"));
        StringAssert.Contains(rendered.Markup, @"C:\ws\Shop");
    }

    private static IRenderedComponent<RunStage> Render(
        BunitContext context, out BuildSession session, bool BuildSucceeded)
    {
        session = SessionOnTheRunStage(BuildSucceeded);

        var captured = session;

        return context.Render<RunStage>(p => p
            .Add(c => c.Session, captured)
            .Add(c => c.OnAdvance, () => { }));
    }

    private static BuildSession SessionOnTheRunStage(bool buildSucceeded)
    {
        var session = new BuildSession("a shop", Path.GetTempPath(), AppCustomization.Default);

        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(new CandidateDomainModel());
        session.Advance();   // review
        session.Advance();   // generate
        session.SetGenerated(new GeneratedApp(Written, "Build succeeded.", buildSucceeded));
        session.Advance();   // run

        return session;
    }
}
