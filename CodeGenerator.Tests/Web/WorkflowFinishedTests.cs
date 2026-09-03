using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerator.Web;
using CodeGenerator.Web.Components;
using CodeGenerator.Web.Workflow;
using DomainDiscovery.Model;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// What the page shows once the workflow is over.
/// </summary>
/// <remarks>
/// Done used to leave the reader on the panel that was already open: the click changed nothing on
/// screen, and the only thing that did happen was the host going away underneath it — which the
/// browser then reported as a lost server. What the console is told is
/// <c>WorkflowOutcomeTests</c>'s job; this is the half the reader sees.
/// </remarks>
[TestClass]
public class WorkflowFinishedTests
{
    private static readonly ScaffoldResult Written =
        new(Success: true, ProjectPath: @"C:\ws\Shop", FilesWritten: [], Message: string.Empty);

    [TestMethod]
    public void Done_ends_the_page_rather_than_leaving_the_panel_up()
    {
        using var context = PageFor(OnTheRunStage(), out var session);

        var page = context.Render<BuildWorkflow>();

        page.Find("button.finish").Click();

        Assert.AreEqual(SessionPhase.Generated, session.Phase);
        Assert.IsNotEmpty(page.FindAll(".finished"));
        Assert.IsEmpty(page.FindAll(".panel"));
    }

    /// <summary>
    /// The diagram goes with the panel, and not for tidiness: every box on it is a click into
    /// <see cref="BuildSession.GoTo"/>, which throws on a workflow that has ended. Leaving it up
    /// would hand the reader a row of buttons that can only fail.
    /// </summary>
    [TestMethod]
    public void The_stage_diagram_goes_with_it()
    {
        using var context = PageFor(OnTheRunStage(), out _);

        var page = context.Render<BuildWorkflow>();

        page.Find("button.finish").Click();

        Assert.IsEmpty(page.FindAll(".stage-diagram"));
    }

    /// <summary>Both surfaces say the same thing, which is the whole reason the page reads the
    /// outcome the host is about to print rather than composing one of its own.</summary>
    [TestMethod]
    public void The_finished_view_says_what_the_console_is_told()
    {
        using var context = PageFor(OnTheRunStage(), out var session);

        var page = context.Render<BuildWorkflow>();

        page.Find("button.finish").Click();

        var shown = page.Find(".finished").TextContent;

        foreach (var line in BuildAppWebHost.OutcomeFor(session).Lines.Where(l => l.Length > 0))
            StringAssert.Contains(shown, line);
    }

    /// <summary>
    /// The tool stops once the workflow ends, so the tab is about to lose its server whatever the
    /// page says. Saying so is what turns that from a fault into the expected end of a run.
    /// </summary>
    [TestMethod]
    public void The_finished_view_tells_the_reader_the_tab_can_be_closed()
    {
        using var context = PageFor(OnTheRunStage(), out _);

        var page = context.Render<BuildWorkflow>();

        page.Find("button.finish").Click();

        StringAssert.Contains(page.Find(".finished").TextContent, "close this tab");
    }

    /// <summary>A cancelled run leaves the same way, and for the same reason: the host stops on
    /// any ended workflow, not only a generated one.</summary>
    [TestMethod]
    public void Cancelling_ends_on_the_same_view()
    {
        using var context = PageFor(Discovered(), out _);

        var page = context.Render<BuildWorkflow>();

        page.Find(".cancel").Click();
        page.Find(".cancel-confirm").Click();

        Assert.IsNotEmpty(page.FindAll(".finished"));
        Assert.IsEmpty(page.FindAll(".panel"));
    }

    private static BunitContext PageFor(BuildSession session, out BuildSession registered)
    {
        var context = new BunitContext();

        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddSingleton(session);
        registered = session;

        return context;
    }

    private static BuildSession New()
        => new("a shop", Path.GetTempPath(), AppCustomization.Default);

    private static BuildSession OnTheRunStage()
    {
        var session = New();

        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(new CandidateDomainModel());
        session.Advance();   // review
        session.Advance();   // relationships
        session.Advance();   // generate
        session.SetGenerated(new GeneratedApp(Written, "Build succeeded.", BuildSucceeded: true));
        session.Advance();   // run

        return session;
    }

    /// <summary>Far enough in to have something to cancel, which is what raises the question
    /// rather than ending the run outright.</summary>
    private static BuildSession Discovered()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Book" });

        var session = New();

        session.SetTarget(new ConsoleTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(model);
        session.Advance();

        session.GoTo(BuildStage.Describe);

        return session;
    }
}
