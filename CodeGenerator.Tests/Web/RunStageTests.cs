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

    // The last node: nothing is past it, so it offers the diagram no step and the box draws none.
    // Going back is a click on an earlier box, which is the diagram's business.
    [TestMethod]
    public void The_run_stage_offers_no_step_forward()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();
        navigation.Offer(() => Task.FromResult(true), label: "left over from the last panel");

        Render(context, out _, BuildSucceeded: true, navigation);

        Assert.IsFalse(navigation.HasStep);
    }

    // A reload builds a new circuit with a new panel, so a generate panel that trusted only its
    // own fields would offer to write — and rebuild — a project that is already on disk.
    [TestMethod]
    public void The_generate_panel_recovers_what_the_session_already_generated()
    {
        using var context = new BunitContext();
        var session = SessionOnTheRunStage(buildSucceeded: true);
        session.GoTo(BuildStage.Generate);
        var navigation = new StageNavigation();

        var rendered = context.Render<GenerateStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, navigation)
            .Add(c => c.OnAdvance, () => { }));

        Assert.IsEmpty(rendered.FindAll("button.write"));
        Assert.IsTrue(navigation.HasStep, "the way on to the run stage should be offered");
        StringAssert.Contains(rendered.Markup, @"C:\ws\Shop");
    }

    /// <summary>
    /// The one line on that panel that needs doing rather than reading. A .Custom.cs stub
    /// written before the validation pipeline is never overwritten, so a regenerate leaves it
    /// declaring a hook the regenerated half no longer has and the pair fails CS0759/CS8795.
    /// The panel says why; the reloaded panel has to say it again, because the streamed build
    /// log that carried the error went with the circuit that watched it.
    /// </summary>
    [TestMethod]
    public void The_generate_panel_says_which_validator_stub_will_stop_the_build()
    {
        using var context = new BunitContext();

        var kept = Written with
        {
            OutdatedValidatorStubs =
                ["Validation/BookValidator.Custom.cs declares ValidateCustom in the shape "
                 + "emitted before the validation pipeline"],
        };

        var session = SessionOnTheRunStage(buildSucceeded: false, scaffold: kept);
        session.GoTo(BuildStage.Generate);

        var rendered = context.Render<GenerateStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        StringAssert.Contains(rendered.Markup, "BookValidator.Custom.cs");
    }

    // The step the review panel offered outlives that panel, so a generate panel that did not
    // withdraw it would show "Generate →" and walk on to the run stage with nothing written.
    [TestMethod]
    public void The_generate_panel_offers_no_way_on_until_something_is_written()
    {
        using var context = new BunitContext();
        var navigation = new StageNavigation();
        navigation.Offer(() => Task.FromResult(true), label: "Generate");

        var session = new BuildSession("a shop", Path.GetTempPath(), AppCustomization.Default);
        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(new CandidateDomainModel());
        session.Advance();   // review
        session.Advance();   // generate, nothing written

        context.Render<GenerateStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, navigation)
            .Add(c => c.OnAdvance, () => { }));

        Assert.IsFalse(navigation.HasStep);
    }

    /// <summary>
    /// A build that never ran is not a build that failed, and the panel must not blame the
    /// compiler for a `dotnet` it could not start — the project is written, and what the user has
    /// to do next is different.
    /// </summary>
    [TestMethod]
    public void A_build_that_never_ran_says_so_rather_than_blaming_the_compiler()
    {
        using var context = new BunitContext();

        var session = new BuildSession("a shop", Path.GetTempPath(), AppCustomization.Default);
        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(new CandidateDomainModel());
        session.Advance();
        session.Advance();
        session.SetGenerated(new GeneratedApp(
            Written, BuildLog: string.Empty, BuildSucceeded: false, BuildRan: false));
        session.Advance();

        var markup = context.Render<RunStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { })).Markup;

        StringAssert.Contains(markup, "not compiled");
        Assert.DoesNotContain("did not build", markup);
    }

    [TestMethod]
    public void A_running_app_can_be_stopped_from_the_panel()
    {
        using var context = new BunitContext();
        var session = SessionOnTheRunStage(buildSucceeded: true);

        session.MarkStarted(processId: 4321);

        var rendered = context.Render<RunStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        Assert.IsNotEmpty(rendered.FindAll("button.stop"));

        // The id is on screen because a user who goes to Task Manager needs it, and because it is
        // the evidence that this panel is offering to stop the thing it actually started.
        StringAssert.Contains(rendered.Markup, "4321");
    }

    [TestMethod]
    public void Nothing_offers_to_stop_an_app_that_was_never_started()
    {
        using var context = new BunitContext();

        var rendered = Render(context, out _, BuildSucceeded: true);

        Assert.IsEmpty(rendered.FindAll("button.stop"));
    }

    /// <summary>Stopping it forgets the id, so the panel stops offering to stop something that has
    /// gone — while the stage stays answered, because the app was run.</summary>
    [TestMethod]
    public void Stopping_forgets_the_process_but_not_that_it_ran()
    {
        var session = SessionOnTheRunStage(buildSucceeded: true);

        session.MarkStarted(processId: 4321);
        session.MarkStopped();

        Assert.IsNull(session.StartedProcessId);
        Assert.IsTrue(session.Started);
        Assert.IsTrue(session.HoldsAnAnswer(BuildStage.Run));
    }

    private static IRenderedComponent<RunStage> Render(
        BunitContext context, out BuildSession session, bool BuildSucceeded,
        StageNavigation? navigation = null)
    {
        session = SessionOnTheRunStage(BuildSucceeded);

        var captured = session;

        return context.Render<RunStage>(p => p
            .Add(c => c.Session, captured)
            .Add(c => c.Navigation, navigation ?? new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));
    }

    private static BuildSession SessionOnTheRunStage(
        bool buildSucceeded, ScaffoldResult? scaffold = null)
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
        session.SetGenerated(
            new GeneratedApp(scaffold ?? Written, "Build succeeded.", buildSucceeded));
        session.Advance();   // run

        return session;
    }
}
