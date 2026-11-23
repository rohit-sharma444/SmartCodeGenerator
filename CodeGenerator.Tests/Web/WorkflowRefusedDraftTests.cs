using System.Reflection;
using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerator.Web.Components;
using CodeGenerator.Web.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// A panel that refuses its own draft does not keep the user on its node.
/// </summary>
/// <remarks>
/// Found in the browser: a missing file in Data's Path, then a click on another node, left the
/// page on Data saying "No file at ...". The click waited as though the discard question were on
/// screen, and no question was. And a refused Continue armed the step that a later "Change
/// anyway" runs, and nothing cleared it.
/// </remarks>
[TestClass]
public class WorkflowRefusedDraftTests
{
    [TestMethod]
    public void A_missing_file_does_not_keep_the_user_on_Data()
    {
        using var context = PageFor(OnData(), out var session);
        var page = context.Render<BuildWorkflow>();

        TypeAMissingFile(page);
        Box(page, "Describe").Click();

        Assert.AreEqual(BuildStage.Describe, session.Current);
        Assert.IsNull(session.Source, "the refused draft was applied");
    }

    [TestMethod]
    public void A_refused_Continue_leaves_nothing_for_a_later_question_to_run()
    {
        using var context = PageFor(OnData(), out var session);
        var page = context.Render<BuildWorkflow>();

        TypeAMissingFile(page);
        page.Find(".stage-continue").Click();

        Assert.AreEqual(BuildStage.Data, session.Current, "the missing file was not refused");
        Assert.IsNull(AfterConfirming(page), "a refusal with no question armed a step");
    }

    [TestMethod]
    public void A_successful_Continue_clears_a_step_armed_by_a_question()
    {
        using var context = PageFor(OnData(), out var session);
        var page = context.Render<BuildWorkflow>();
        Field(page).SetValue(page.Instance, (Func<Task>)(() => Task.CompletedTask));

        page.Find("input[value=none]").Change(true);
        page.Find(".stage-continue").Click();

        Assert.AreEqual(BuildStage.Look, session.Current);
        Assert.IsNull(AfterConfirming(page));
    }

    private static void TypeAMissingFile(IRenderedComponent<BuildWorkflow> page)
    {
        page.Find("input[value=file]").Change(true);
        page.Find("input.path").Change("nosuch.csv");
    }

    private static FieldInfo Field(IRenderedComponent<BuildWorkflow> page)
        => typeof(BuildWorkflow).GetField("_afterConfirming", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static object? AfterConfirming(IRenderedComponent<BuildWorkflow> page)
        => Field(page).GetValue(page.Instance);

    private static AngleSharp.Dom.IElement Box(IRenderedComponent<BuildWorkflow> page, string title)
        => page.FindAll(".node-open").First(b => b.TextContent.Contains(title));

    /// <summary>A session on the Data node, nothing discovered.</summary>
    private static BuildSession OnData()
    {
        var session = new BuildSession(
            "a shop", Path.Combine(Path.GetTempPath(), "bh2f-none"), AppCustomization.Default);

        session.SetTarget(new ConsoleTarget());
        session.Advance();

        return session;
    }

    private static BunitContext PageFor(BuildSession session, out BuildSession registered)
    {
        var context = new BunitContext();

        // The page focuses the open panel, which is an interop call the strict default refuses.
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        // The error boundary around the panel resolves an ILoggerFactory of its own.
        context.Services.AddLogging();
        context.Services.AddSingleton(session);
        registered = session;

        return context;
    }
}
