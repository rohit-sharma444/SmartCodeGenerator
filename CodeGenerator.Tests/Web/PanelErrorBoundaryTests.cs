using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerator.Web.Components;
using CodeGenerator.Web.Workflow;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The backstop for a panel that throws.
/// </summary>
/// <remarks>
/// There was none. An exception out of a component handler takes the Blazor circuit down, and this
/// page has no <c>blazor-error-ui</c> element for the framework's own "reload" bar to render into,
/// so the page simply froze — after which the host counted the tab as gone and the console reported
/// the workflow closed before it finished. A crash the user did not cause, could not see, and was
/// then told a false story about.
/// </remarks>
[TestClass]
public class PanelErrorBoundaryTests
{
    /// <summary>A panel that cannot render. Stands in for the ones that read files and start
    /// processes, whose failures cannot all be enumerated in advance.</summary>
    private sealed class Throwing : ComponentBase
    {
        protected override void OnInitialized()
            => throw new IOException("the file is open in another program");
    }

    [TestMethod]
    public void A_child_that_throws_is_reported_rather_than_escaping()
    {
        using var context = Rendered();
        Exception? caught = null;

        context.Render<PanelErrorBoundary>(p => p
            .Add(c => c.OnError, EventCallback.Factory.Create<Exception>(
                this, (Exception ex) => caught = ex))
            .Add<Throwing>(c => c.ChildContent));

        Assert.IsInstanceOfType<IOException>(caught);
    }

    [TestMethod]
    public void The_error_content_takes_the_place_of_the_panel_that_failed()
    {
        using var context = Rendered();

        var rendered = context.Render<PanelErrorBoundary>(p => p
            .Add(c => c.OnError, EventCallback.Factory.Create<Exception>(
                this, (Exception _) => { }))
            .Add<Throwing>(c => c.ChildContent)
            .Add(c => c.ErrorContent, _ => builder => builder.AddMarkupContent(
                0, "<p class=\"panel-broken\">could not finish</p>")));

        Assert.IsNotEmpty(rendered.FindAll(".panel-broken"));
    }

    /// <summary>
    /// The failure has to reach the session, or the diagram goes on showing a step in progress.
    /// <c>Fail</c> already exists and is already drawn red and revisitable — the boundary's job is
    /// only to get there.
    /// </summary>
    [TestMethod]
    public void The_session_records_the_failure_so_the_diagram_offers_a_way_back()
    {
        var session = new BuildSession("a shop", @"C:\ws", AppCustomization.Default);

        PanelFailure.Report(session, new PathTooLongException("too long"));

        Assert.AreEqual(StageState.Failed, session.StateOf(session.Current));
        Assert.IsNotNull(session.FailureMessage);
        StringAssert.Contains(session.FailureMessage, "too long for Windows");
    }

    /// <summary>
    /// A workflow that has already ended must not be failed on the way out: <c>Fail</c> refuses to
    /// touch a finished session, and an exception thrown from inside the error handler would be the
    /// one crash with nothing left to catch it.
    /// </summary>
    [TestMethod]
    public void A_workflow_that_has_already_ended_is_left_alone()
    {
        var session = new BuildSession("a shop", @"C:\ws", AppCustomization.Default);
        session.Cancel();

        PanelFailure.Report(session, new IOException("late"));

        Assert.AreEqual(SessionPhase.Cancelled, session.Phase);
        Assert.IsNull(session.FailureMessage);
    }

    [TestMethod]
    public void A_long_path_is_explained_as_a_long_path_not_as_an_IO_error()
    {
        StringAssert.Contains(
            PanelFailure.Explain(new PathTooLongException("x")), "too long for Windows");

        StringAssert.Contains(
            PanelFailure.Explain(new UnauthorizedAccessException("no")), "Access was denied");

        StringAssert.Contains(
            PanelFailure.Explain(new IOException("locked")), "locked");
    }

    private static BunitContext Rendered()
    {
        var context = new BunitContext();

        // ErrorBoundary resolves an ILoggerFactory of its own.
        context.Services.AddLogging();

        return context;
    }
}
